using Dark.Net;
using LetsNuGet.Cli;
using System.Windows;

namespace LetsNuGet;

public sealed partial class App {

    protected override void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);
        DarkNet.Instance.SetCurrentProcessTheme(Theme.Auto);

        if (Options.parse(e.Args) is not {} options) {
            Shutdown(1);
            return;
        }

        MainWindow = new MainWindow(options.apiKeyName) {
            ShowActivated = false,
            WindowState   = WindowState.Minimized,
            ShowInTaskbar = false
        };

        MainWindow.Show();
        MainWindow.Hide();
    }

    protected override void OnExit(ExitEventArgs e) {
        Console.WriteLine("Application exiting");
        base.OnExit(e);
    }

}