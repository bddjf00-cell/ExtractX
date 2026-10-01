using System.IO;
using System.Windows;

namespace ExtractX.Setup;

public partial class App : Application
{
    public App()
    {
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;
        bool Has(string f) => args.Any(a => a.Equals(f, StringComparison.OrdinalIgnoreCase));
        string? Val(string f) => args.FirstOrDefault(a => a.StartsWith(f + "=", StringComparison.OrdinalIgnoreCase))?[(f.Length + 1)..].Trim('"');

        if (Has("--uninstall"))
        {
            if (Has("--silent"))
            {
                // Ventana oculta: mantiene vivo el dispatcher durante el await
                var guard = new Window { Width = 0, Height = 0, WindowStyle = WindowStyle.None, ShowInTaskbar = false, Visibility = Visibility.Hidden };
                guard.Show();
                try { await Installer.UninstallAsync(null, CancellationToken.None); }
                catch { }
                guard.Close();
                Shutdown();
            }
            else new MainWindow(uninstallMode: true).Show();
            return;
        }

        if (Has("--silent"))
        {
            var guard = new Window { Width = 0, Height = 0, WindowStyle = WindowStyle.None, ShowInTaskbar = false, Visibility = Visibility.Hidden };
            guard.Show();
            try
            {
                bool allUsers = Has("--all-users");
                if (allUsers && !Installer.IsAdmin()) { Installer.RelaunchElevated(string.Join(' ', args)); guard.Close(); Shutdown(); return; }
                var formats = (Val("--formats") ?? "zip,rar,7z")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(f => f != "none").Select(f => f.StartsWith(".") ? f : "." + f).ToList();
                var opts = new InstallOptions(
                    Val("--dir") ?? Installer.DefaultDir(allUsers), allUsers, formats,
                    !Has("--no-shortcuts"), !Has("--no-desktop"), Has("--launch"));
                await Installer.InstallAsync(opts, null, CancellationToken.None);
            }
            catch { }
            guard.Close();
            Shutdown();
            return;
        }

        new MainWindow(uninstallMode: false).Show();
    }
}
