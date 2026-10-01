using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace ExtractX.Setup;

public partial class MainWindow : Window
{
    private readonly bool _uninstallMode;
    private int _page;
    private bool _done;
    private CancellationTokenSource? _cts;

    private readonly FrameworkElement[] _pages = null!;
    private readonly TextBlock[] _steps = null!;
    private readonly string[] _titles = { "Bienvenida", "Licencia", "Destino", "Formatos", "Instalando", "Listo" };

    public MainWindow(bool uninstallMode)
    {
        InitializeComponent();
        _uninstallMode = uninstallMode;
        TxtSideVer.Text = $"Instalador v{Installer.Version}";
        _pages = new FrameworkElement[] { Pg0, Pg1, Pg2, Pg3, Pg4, Pg5 };
        _steps = new TextBlock[] { Step0, Step1, Step2, Step3, Step4, Step5 };

        if (_uninstallMode)
        {
            TxtSideTitle.Text = "Desinstalar";
            TxtTopStep.Text = "Desinstalar";
            foreach (var s in _steps) s.Visibility = Visibility.Collapsed;
            ShowOnly(PgUn);
            TxtUnDir.Text = "Instalación encontrada en:\n" + (Installer.FindInstalledDir() ?? "(no encontrada)");
            BtnBack.Visibility = Visibility.Collapsed;
            BtnNext.Visibility = Visibility.Collapsed;
            BtnCancelSetup.Content = "Cerrar";
        }
        else
        {
            TxtDir.Text = Installer.DefaultDir(false);
            TxtRuntime.Text = Installer.HasDesktopRuntime()
                ? "✔ Requisito .NET 9 listo en este equipo."
                : "⚠ Este equipo no tiene .NET Desktop Runtime: el instalador lo descargará solo.";
            ChkAllUsers.Checked += (_, _) => TxtDir.Text = Installer.DefaultDir(true);
            ChkAllUsers.Unchecked += (_, _) => TxtDir.Text = Installer.DefaultDir(false);
            ShowPage(0);
        }
    }

    // ---------- navegación ----------
    private void ShowOnly(FrameworkElement el)
    {
        foreach (var p in _pages) p.Visibility = Visibility.Collapsed;
        PgUn.Visibility = Visibility.Collapsed;
        el.Visibility = Visibility.Visible;
    }

    private void ShowPage(int i)
    {
        _page = i;
        ShowOnly(_pages[i]);
        TxtTopStep.Text = _titles[i];
        for (int s = 0; s < _steps.Length; s++)
        {
            _steps[s].Foreground = s == i
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF))
                : s < i ? new SolidColorBrush(Color.FromRgb(0x2B, 0x9B, 0xF4))
                        : new SolidColorBrush(Color.FromRgb(0x5C, 0x6B, 0x80));
        }
        BtnBack.IsEnabled = i is 1 or 2 or 3;
        BtnNext.Content = i switch { 0 => "Siguiente →", 1 => "Siguiente →", 2 => "Siguiente →", 3 => "Instalar", _ => "Siguiente →" };
        BtnNext.Visibility = i is 4 or 5 ? Visibility.Collapsed : Visibility.Visible;
        BtnCancelSetup.Content = i == 5 ? "Cerrar" : "Cancelar";
    }

    private async void BtnNext_Click(object s, RoutedEventArgs e)
    {
        if (_page == 1 && ChkLicense.IsChecked != true)
        {
            MessageBox.Show("Debes aceptar la licencia para continuar.", "ExtractX", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_page == 3) { ShowPage(4); await RunInstallAsync(); return; }
        ShowPage(Math.Min(3, _page + 1));
    }

    private void BtnBack_Click(object s, RoutedEventArgs e)
    {
        if (_page is 1 or 2 or 3) ShowPage(_page - 1);
    }

    // ---------- instalación ----------
    private async Task RunInstallAsync()
    {
        BtnBack.IsEnabled = false;
        BtnCancelSetup.IsEnabled = false;
        bool allUsers = ChkAllUsers.IsChecked == true;
        if (allUsers && !Installer.IsAdmin())
        {
            // Re-lanza elevado pasando la selección actual
            var args = $"--silent --all-users --dir=\"{TxtDir.Text}\" --formats={SelectedFormats()}";
            try { Installer.RelaunchElevated(args); } catch { }
            TxtSetupMsg.Text = "Se solicitó permiso de administrador: continúa en la ventana elevada.";
            BtnCancelSetup.IsEnabled = true;
            BtnCancelSetup.Content = "Cerrar";
            return;
        }

        var opts = new InstallOptions(TxtDir.Text.Trim(), allUsers, ParseFormats(SelectedFormats()),
            ChkStartMenu.IsChecked == true, ChkDesktop.IsChecked == true, ChkLaunch.IsChecked == true);
        _cts = new CancellationTokenSource();
        var progress = new Progress<(int pct, string msg)>(t =>
        {
            SetupProgress.Value = t.pct;
            TxtSetupMsg.Text = t.msg;
            TxtSetupLog.AppendText($"[{t.pct}%] {t.msg}\n");
            TxtSetupLog.ScrollToEnd();
        });
        try
        {
            await Installer.InstallAsync(opts, progress, _cts.Token);
            _done = true;
            TxtDoneSub.Text = $"Instalado en {opts.Directory} con {opts.Formats.Count} formato(s) asociado(s).";
            ShowPage(5);
            if (opts.LaunchAfter)
                try { Process.Start(new ProcessStartInfo(Path.Combine(opts.Directory, "ExtractX.exe")) { UseShellExecute = true }); } catch { }
        }
        catch (Exception ex)
        {
            TxtSetupMsg.Text = "✘ Error: " + ex.Message;
            TxtSetupLog.AppendText("ERROR: " + ex + "\n");
            BtnCancelSetup.IsEnabled = true;
        }
    }

    private string SelectedFormats()
    {
        var list = new List<string>();
        if (F_Zip.IsChecked == true) list.Add("zip");
        if (F_Rar.IsChecked == true) list.Add("rar");
        if (F_7z.IsChecked == true) list.Add("7z");
        if (F_Tar.IsChecked == true) list.Add("tar");
        if (F_Gz.IsChecked == true) list.Add("gz");
        if (F_Iso.IsChecked == true) list.Add("iso");
        return string.Join(',', list);
    }

    private static List<string> ParseFormats(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
           .Select(f => "." + f.TrimStart('.')).ToList();

    // ---------- desinstalación ----------
    private async void BtnUnGo_Click(object s, RoutedEventArgs e)
    {
        BtnUnGo.IsEnabled = false;
        var progress = new Progress<(int pct, string msg)>(t =>
        {
            UnProgress.Value = t.pct;
            TxtUnMsg.Text = t.msg;
        });
        try
        {
            await Installer.UninstallAsync(progress, CancellationToken.None);
            TxtUnMsg.Text = "✔ ExtractX desinstalado. Puedes cerrar esta ventana.";
        }
        catch (Exception ex) { TxtUnMsg.Text = "✘ " + ex.Message; }
    }

    // ---------- varios ----------
    private void BtnBrowse_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Carpeta de instalación" };
        if (dlg.ShowDialog() == true) TxtDir.Text = Path.Combine(dlg.FolderName, "ExtractX");
    }

    private void BtnDefaultApps_Click(object s, RoutedEventArgs e) => Installer.OpenDefaultApps();
    private void Bar_MouseDown(object sender, MouseButtonEventArgs e)
    { if (e.ChangedButton == MouseButton.Left) DragMove(); }
    private void BtnMin_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void BtnClose_Click(object s, RoutedEventArgs e)
    {
        if (_page == 4 && !_done)
        {
            var r = MessageBox.Show("La instalación está en curso. ¿Cancelar?", "ExtractX",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            _cts?.Cancel();
        }
        Close();
    }
}
