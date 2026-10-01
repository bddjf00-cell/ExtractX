using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using ExtractX.Core;
using Microsoft.Win32;

namespace ExtractX;

/// <summary>
/// Mini ventana: doble clic a un archivo -> extrae en 2 clics.
/// Clic derecho "Comprimir con ExtractX" -> comprime en 1 clic.
/// </summary>
public partial class MiniWindow : Window
{
    private readonly string? _archive;
    private readonly List<string> _sources = new();
    private readonly AppStore _store;
    private CancellationTokenSource? _cts;
    private bool _busy;

    // Modo extracción
    public MiniWindow(string archivePath)
    {
        InitializeComponent();
        _store = AppStore.Load();
        _archive = archivePath;
        PanelExtract.Visibility = Visibility.Visible;
        PanelCompress.Visibility = Visibility.Collapsed;
        TxtMode.Text = "· Extracción rápida";
        LoadArchiveInfo();
    }

    // Modo compresión
    public MiniWindow(List<string> sources)
    {
        InitializeComponent();
        _store = AppStore.Load();
        _sources.AddRange(sources.Where(File.Exists).Cast<string>().Concat(sources.Where(Directory.Exists)));
        PanelExtract.Visibility = Visibility.Collapsed;
        PanelCompress.Visibility = Visibility.Visible;
        TxtMode.Text = "· Compresión rápida";
        SelectCombo(MiniFormat, _store.Settings.DefaultCompressFormat);
        SelectCombo(MiniLevel, _store.Settings.DefaultCompressLevel);
        RefreshCompressList();
    }

    private static void SelectCombo(System.Windows.Controls.ComboBox box, string content)
    {
        foreach (var it in box.Items.OfType<System.Windows.Controls.ComboBoxItem>())
            if ((it.Content?.ToString() ?? "") == content) { box.SelectedItem = it; return; }
    }

    private void ShowBusy(string msg)
    {
        TxtMiniBusy.Text = msg;
        MiniBusy.Visibility = Visibility.Visible;
        LogoFx.Spin(MiniBusyLogo);
    }

    private void HideBusy()
    {
        LogoFx.Stop(MiniBusyLogo);
        MiniBusy.Visibility = Visibility.Collapsed;
    }

    private void LoadArchiveInfo()
    {
        if (_archive == null) return;
        var fi = new FileInfo(_archive);
        TxtFileName.Text = fi.Name;
        TxtFileMeta.Text = $"{ArchiveService.DetectType(_archive)} · {ArchiveService.FormatSize(fi.Length)}";
        try
        {
            var entries = ArchiveService.ListEntries(_archive);
            TxtFileContent.Text = $"{entries.Count} archivos · {ArchiveService.FormatSize(entries.Sum(x => x.Size))}";
        }
        catch
        {
            TxtFileContent.Text = "Protegido con contraseña o ilegible: introduce la clave.";
        }
    }

    private void RefreshCompressList()
    {
        CompressList.Items.Clear();
        foreach (var s in _sources) CompressList.Items.Add(s);
        TxtCompressTitle.Text = $"{_sources.Count} elemento(s) para comprimir";
    }

    // ---------- barra ----------
    private void Bar_MouseDown(object sender, MouseButtonEventArgs e)
    { if (e.ChangedButton == MouseButton.Left) DragMove(); }
    private void BtnMin_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void BtnClose_Click(object s, RoutedEventArgs e) => Close();
    private void BtnFull_Click(object s, RoutedEventArgs e)
    {
        var full = new MainWindow();
        if (_archive != null && File.Exists(_archive)) full.LoadExternalFile(_archive);
        full.Show();
        Close();
    }

    // ---------- extracción ----------
    private void BtnExtractHere_Click(object s, RoutedEventArgs e)
    {
        if (_archive == null) return;
        _ = RunExtractAsync(Path.GetDirectoryName(_archive)!);
    }
    private void BtnExtractTo_Click(object s, RoutedEventArgs e)
    {
        if (_archive == null) return;
        var dlg = new OpenFolderDialog { Title = "Carpeta destino" };
        if (dlg.ShowDialog() == true) _ = RunExtractAsync(dlg.FolderName);
    }
    private void BtnCancel_Click(object s, RoutedEventArgs e) => _cts?.Cancel();

    private async Task RunExtractAsync(string dest)
    {
        if (_archive == null || _busy) return;
        _busy = true;
        _cts = new CancellationTokenSource();
        BtnCancel.Visibility = Visibility.Visible;
        BtnExtractHere.IsEnabled = BtnExtractTo.IsEnabled = false;
        LogoFx.Spin(MiniLogo);
        ShowBusy($"Extrayendo {Path.GetFileName(_archive)}...");
        try
        {
            var pwd = string.IsNullOrEmpty(PwdBox.Password) ? null : PwdBox.Password;
            var progress = new Progress<(double pct, string current)>(t =>
            {
                MiniProgress.Value = t.pct;
                TxtPct.Text = $"{t.pct:0}%";
                TxtStatus.Text = string.IsNullOrEmpty(t.current) ? "Extrayendo..." : t.current;
            });
            await ArchiveService.ExtractAsync(_archive, dest, pwd, progress, _cts.Token);
            var fi = new FileInfo(_archive);
            _store.History.Insert(0, new HistoryEntry
            {
                FileName = fi.Name, SourcePath = _archive, Destination = dest,
                SizeBytes = fi.Length, SizeText = ArchiveService.FormatSize(fi.Length),
                Date = DateTime.Now, Status = "Extraído", Success = true
            });
            _store.Save();
            TxtStatus.Text = "✔ Extracción completada.";
            if (_store.Settings.AutoOpen)
                try { Process.Start("explorer.exe", dest); } catch { }
            await Task.Delay(900);
            Close();
        }
        catch (OperationCanceledException) { TxtStatus.Text = "Cancelado."; }
        catch (Exception ex) { TxtStatus.Text = "✘ " + Short(ex.Message); }
        finally
        {
            _busy = false;
            LogoFx.Stop(MiniLogo);
            HideBusy();
            BtnCancel.Visibility = Visibility.Collapsed;
            BtnExtractHere.IsEnabled = BtnExtractTo.IsEnabled = true;
        }
    }

    // ---------- compresión ----------
    private void BtnCompressAdd_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Multiselect = true, Filter = "Todos|*.*" };
        if (dlg.ShowDialog() == true) { _sources.AddRange(dlg.FileNames); RefreshCompressList(); }
    }

    private async void BtnCompressGo_Click(object s, RoutedEventArgs e)
    {
        if (_sources.Count == 0 || _busy) return;
        string format = (MiniFormat.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "ZIP";
        var level = ArchiveService.ParseLevel((MiniLevel.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString());
        string? pwd = string.IsNullOrEmpty(MiniPwd.Password) ? null : MiniPwd.Password;
        string first = _sources[0];
        string defDir = File.Exists(first) ? Path.GetDirectoryName(first)! : first;
        string ext = ArchiveService.DefaultExtension(format);
        string defName = _sources.Count == 1
            ? Path.GetFileNameWithoutExtension(first.TrimEnd('\\')) + ext
            : Path.GetFileName(defDir.TrimEnd('\\')) + ext;
        var dlg = new SaveFileDialog { Filter = $"{format}|*{ext}", FileName = defName, InitialDirectory = defDir };
        if (dlg.ShowDialog() != true) return;
        _busy = true;
        BtnCompressGo.IsEnabled = false;
        var spinTarget = PanelCompress.Visibility == Visibility.Visible ? BarLogo : MiniLogo;
        LogoFx.Spin(spinTarget);
        ShowBusy($"Comprimiendo a {format}...");
        try
        {
            var p = new Progress<double>(v =>
            {
                CompressProgress.Value = v;
                TxtCompressStatus.Text = $"Comprimiendo a {format}... {v:0}%";
            });
            await ArchiveService.CompressAsync(_sources, dlg.FileName, format, pwd, p, CancellationToken.None, level);
            TxtCompressStatus.Text = "✔ Comprimido: " + dlg.FileName;
            await Task.Delay(900);
            Close();
        }
        catch (Exception ex) { TxtCompressStatus.Text = "✘ " + Short(ex.Message); }
        finally { _busy = false; LogoFx.Stop(BarLogo); LogoFx.Stop(MiniLogo); HideBusy(); BtnCompressGo.IsEnabled = true; }
    }

    private static string Short(string m) => m.Length > 140 ? m[..140] + "…" : m;
}
