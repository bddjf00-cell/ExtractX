using System.IO;
using System.Windows;

namespace ExtractX.Setup;

public partial class App : Application
{
    public App()
    {
    }

    private static string SetupLog => Path.Combine(Path.GetTempPath(), "ExtractX-Setup.log");

    private static void Log(string msg)
    {
        try { File.AppendAllText(SetupLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}\n"); } catch { }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;
        bool Has(string f) => args.Any(a => a.Equals(f, StringComparison.OrdinalIgnoreCase));
        string? Val(string f) => args.FirstOrDefault(a => a.StartsWith(f + "=", StringComparison.OrdinalIgnoreCase))?[(f.Length + 1)..].Trim('"');

        InstallOptions ParseOptions(bool allUsers) => new(
            Val("--dir") ?? Installer.DefaultDir(allUsers), allUsers,
            (Val("--formats") ?? "zip,rar,7z")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(f => f != "none").Select(f => f.StartsWith(".") ? f : "." + f).ToList(),
            !Has("--no-shortcuts"), !Has("--no-desktop"), Has("--launch"));

        if (Has("--uninstall"))
        {
            if (Has("--silent"))
            {
                // Ventana oculta: mantiene vivo el dispatcher durante el await
                var guard = new Window { Width = 0, Height = 0, WindowStyle = WindowStyle.None, ShowInTaskbar = false, Visibility = Visibility.Hidden };
                guard.Show();
                try { await Installer.UninstallAsync(null, CancellationToken.None); }
                catch (Exception ex) { Log("UNINSTALL ERROR: " + ex); }
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
                await Installer.InstallAsync(ParseOptions(allUsers), null, CancellationToken.None);
            }
            catch (Exception ex) { Log("INSTALL ERROR: " + ex); }
            guard.Close();
            Shutdown();
            return;
        }

        // Ventana elevada: viene del asistente sin permisos, con las opciones ya elegidas.
        // Muestra el progreso (nada de silencioso: el usuario debe ver qué pasa).
        if (Has("--elevated"))
        {
            var win = new MainWindow(uninstallMode: false);
            win.Show();
            _ = win.BeginAutoInstall(ParseOptions(allUsers: true));
            return;
        }

        new MainWindow(uninstallMode: false).Show();
    }
}
