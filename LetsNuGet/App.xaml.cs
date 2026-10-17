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
            Visibility    = Visibility.Hidden,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowState   = WindowState.Minimized
        };

        MainWindow.Show();
        MainWindow.Hide();
        MainWindow.WindowState   = WindowState.Normal;
        MainWindow.ShowInTaskbar = true;
    }

    protected override void OnExit(ExitEventArgs e) {
        Console.WriteLine("Application exiting");
        base.OnExit(e);
    }

}