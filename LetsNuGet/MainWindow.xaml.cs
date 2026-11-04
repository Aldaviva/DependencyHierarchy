using Dark.Net;
using Microsoft.Web.WebView2.Core;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;

namespace LetsNuGet;

public sealed partial class MainWindow {

    private readonly string apiKeyName;

    private readonly ConcurrentDictionary<ulong, Uri> navigationIdUrls = new();

    public MainWindow(string apiKeyName) {
        Console.WriteLine("Constructing MainWindow");
        this.apiKeyName = apiKeyName;
        InitializeComponent();
        DarkNet.Instance.SetWindowThemeWpf(this, Theme.Auto);

        Loaded += async (_, _) => {
            Console.WriteLine("MainWindow loaded");
            Console.WriteLine("Initializing Edge WebView2");
            await webview.EnsureCoreWebView2Async();
            Console.WriteLine("Initialized Edge WebView2");
            webview.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled =  false;
            webview.NavigationStarting                                   += onNavigationStarting;
            webview.NavigationCompleted                                  += onNavigationCompleted;
            webview.WebMessageReceived                                   += onWebMessageReceived;
            webview.CoreWebView2.ScriptDialogOpening                     += onShowDialogBox;
            Console.WriteLine("Navigating to NuGet Gallery API Keys page");
            webview.Source = new Uri("https://www.nuget.org/account/apikeys?forceApiKeys=true");
        };

        Console.WriteLine("Constructed MainWindow");
    }

    private void onNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs nav) {
        Uri candidateUrl = new(nav.Uri);
        navigationIdUrls[nav.NavigationId] = candidateUrl;
        switch (candidateUrl) {
            case { Scheme: "https", Host: "login.microsoftonline.com", AbsolutePath: "/common/oauth2/v2.0/authorize" } when candidateUrl.QueryParams["prompt"] == "select_account": {
                Console.WriteLine("Skipping Microsoft account choice prompt if already signed in");
                nav.Cancel = true;
                string fastUrl = candidateUrl.ToBuilder().QueryParam("prompt", (object?) null).ToString();

                Console.WriteLine($"Redirecting to {fastUrl}");
                webview.CoreWebView2.Navigate(fastUrl);
                break;
            }
        }
    }

    private async void onNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs nav) {
        try {
            switch (navigationIdUrls.TryRemove(nav.NavigationId, out Uri? navUrl) ? navUrl : null) {
                case { Scheme: "https", Host: "www.nuget.org", AbsolutePath: "/users/account/LogOn" }:
                    Console.WriteLine("Clicking Sign In With Microsoft");
                    await webview.ExecuteScriptAsync("""document.querySelector(".provider-button").click();""" /*lang=javascript*/);
                    break;

                case { Scheme: "https", Host: "login.microsoftonline.com", AbsolutePath: "/common/oauth2/v2.0/authorize" }:
                    bool hasExistingAccount = JsonSerializer.Deserialize<bool>(await webview.ExecuteScriptAsync("""!!document.querySelector(".new-session-popup-v2sso");""" /*lang=javascript*/));
                    if (!hasExistingAccount) {
                        Console.WriteLine("Not already signed into an account, showing sign-in window");
                        Show();
                        WindowState   = WindowState.Normal;
                        ShowInTaskbar = true;
                    } else {
                        Console.WriteLine("Already signed into an account, not showing sign-in window");
                    }
                    break;

                case { Scheme: "https", Host: "www.nuget.org", AbsolutePath: "/account/apikeys" } u when u.QueryParams["forceApiKeys"] == "true":
                    Hide();
                    Console.WriteLine($"Clicking Regenerate button on the {apiKeyName} API key and waiting for the new value");
                    await webview.ExecuteScriptAsync("""
                        (apiKeyName => {
                            new MutationObserver((mutations, observer) => {
                                const apiKeyEl = findApiKeyEl(apiKeyName);
                                const apiKeyModel = window.ko.dataFor(apiKeyEl);
                                const result = {
                                    type: "apiKeyGenerated",
                                    apiKey: apiKeyModel.Value(),
                                    expirationDate: apiKeyModel.Expires()
                                };
                                if (result.apiKey?.length ?? false) {
                                    observer.disconnect();
                                    window.chrome.webview.postMessage(result);
                                }
                            }).observe(document.querySelector("section.page-api-keys"), { subtree: true, childList: true, attributes: true });
                                
                            findApiKeyEl(apiKeyName).querySelector("a:has(i.ms-Icon--Refresh)").click();

                            function findApiKeyEl(name) {
                                return Array.from(document.querySelectorAll(".api-key-details"))
                                    .find(el => el.querySelector("h3").textContent === name);
                            }
                        })
                        """ /*lang=javascript*/ + $"({JsonSerializer.Serialize(apiKeyName)})");
                    break;
            }
        } catch (Exception e) when (e is not OutOfMemoryException) {
            onException(e);
        }
    }

    private void onShowDialogBox(object? _, CoreWebView2ScriptDialogOpeningEventArgs args) {
        if (args.Kind == CoreWebView2ScriptDialogKind.Confirm && args.Message.Equals("Are you sure you want to regenerate the API key?", StringComparison.CurrentCulture)) {
            Console.WriteLine("Accepting confirmation dialog box to regenerate API key");
            args.Accept();
        }
    }

    private async void onWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs message) {
        try {
            Console.WriteLine($"Received message from {message.Source}");
            if (new Uri(message.Source) is not { Scheme: "https", Host: "www.nuget.org", AbsolutePath: "/account/apikeys" }) return;

            JsonNode? jsonNode = JsonSerializer.Deserialize<JsonNode>(message.WebMessageAsJson);
            switch (jsonNode?["type"]?.GetValue<string>()) {
                case GeneratedApiKey.TYPE:
                    GeneratedApiKey generatedApiKey = jsonNode.Deserialize<GeneratedApiKey>();
                    Console.WriteLine($"Received {generatedApiKey}");
                    await onApiKeyGenerated(generatedApiKey);
                    break;
            }
        } catch (Exception e) when (e is not OutOfMemoryException) {
            onException(e);
        }
    }

    private async Task onApiKeyGenerated(GeneratedApiKey generatedApiKey) {
        Console.WriteLine($"Configuring NuGet client to use new API key {generatedApiKey.apiKey}, which expires {generatedApiKey.expirationDate:f}");
        (await Process.ExecFile("nuget.exe", ["setapikey", generatedApiKey.apiKey, "-NonInteractive"], hideWindow: true)).EnsureSuccessful();

        Console.WriteLine("Done, closing");
        Close();
    }

    private void onException(Exception exception) =>
        MessageBox.Show(this, exception.Message + "\n\n" + exception.StackTrace, $"Uncaught {exception.GetType().Name}", MessageBoxButton.OK, MessageBoxImage.Error);

    private readonly record struct GeneratedApiKey {

        public const string TYPE = "apiKeyGenerated";

        public required string apiKey { get; init; }
        public required DateTimeOffset expirationDate { get; init; }

    }

}