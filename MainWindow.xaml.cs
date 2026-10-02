using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ExtractX.Core;
using Microsoft.Win32;
using SharpCompress.Archives;
using SharpCompress.Common;
using System.IO.Compression;

namespace ExtractX;

public partial class MainWindow : Window
{
    private readonly AppStore _store;
    private string? _currentFile;
    private string? _currentPassword;
    private List<EntryInfo> _currentEntries = new();
    private readonly List<string> _masivaFiles = new();
    private readonly List<string> _compressFiles = new();
    private CancellationTokenSource? _cts;
    private string _lastDest = "";
    private string? _pendingDest;
    private bool _loading = true;
    private readonly Dictionary<string, FrameworkElement> _pages = new();
    private readonly Dictionary<string, Button> _nav = new();

    public MainWindow()
    {
        InitializeComponent();
        _store = AppStore.Load();
        _pages = new()
        {
            ["Inicio"] = PageInicio, ["Extraer"] = PageExtraer, ["Masiva"] = PageMasiva,
            ["Historial"] = PageHistorial, ["Favoritos"] = PageFavoritos, ["Passwords"] = PagePasswords,
            ["Tools"] = PageTools, ["Config"] = PageConfig, ["Acerca"] = PageAcerca,
        };
        _nav = new()
        {
            ["Inicio"] = BtnNavInicio, ["Extraer"] = BtnNavExtraer, ["Masiva"] = BtnNavMasiva,
            ["Historial"] = BtnNavHistorial, ["Favoritos"] = BtnNavFavoritos, ["Passwords"] = BtnNavPasswords,
            ["Tools"] = BtnNavTools, ["Config"] = BtnNavConfig, ["Acerca"] = BtnNavAcerca,
        };
        LoadConfigToUi();
        RefreshAll();
        ShowPage("Inicio");
        _loading = false;
        ApplyCompressDefaults();
        _ = ShowStartupSplashAsync();
        if (_store.Settings.CheckUpdates) _ = AutoCheckUpdatesAsync();
    }

    private async Task ShowStartupSplashAsync()
    {
        try
        {
            ShowSplash("Iniciando ExtractX...");
            await Task.Delay(1400);
        }
        finally { HideSplash(); }
    }

    private void ShowSplash(string msg, bool indeterminate = true)
    {
        TxtSplashMsg.Text = msg;
        SplashBar.IsIndeterminate = indeterminate;
        if (indeterminate) SplashBar.Value = 0;
        SplashOverlay.Visibility = Visibility.Visible;
        LogoFx.Spin(SplashLogo);
    }

    private void HideSplash()
    {
        LogoFx.Stop(SplashLogo);
        SplashOverlay.Visibility = Visibility.Collapsed;
    }

    private static void SelectCombo(ComboBox box, string content)
    {
        foreach (var it in box.Items.OfType<ComboBoxItem>())
            if ((it.Content?.ToString() ?? "") == content) { box.SelectedItem = it; return; }
    }

    private void ApplyCompressDefaults()
    {
        SelectCombo(CmbCompressFormat, _store.Settings.DefaultCompressFormat);
        SelectCombo(CmbCompressLevel, _store.Settings.DefaultCompressLevel);
    }

    private async Task AutoCheckUpdatesAsync()
    {
        try
        {
            await Task.Delay(2500);
            var info = await UpdateService.CheckAsync(_store.Settings.UpdateRepo, UpdateService.LoadToken());
            if (info == null || info.Tag == _store.Settings.SkippedVersion) return;
            ShowUpdateDialog(info);
        }
        catch { }
    }

    private UpdateService.UpdateInfo? _pendingUpdate;

    private void ShowUpdateDialog(UpdateService.UpdateInfo info)
    {
        _pendingUpdate = info;
        TxtUpdTitle.Text = $"Nueva versión disponible: v{info.Tag}";
        TxtUpdKind.Text = info.Incremental ? "Parche incremental (descarga ligera)" : "Instalador completo";
        string notes = info.Notes ?? "";
        TxtUpdNotes.Text = string.IsNullOrWhiteSpace(notes) ? "(Sin notas de la versión.)"
            : notes.Length > 1500 ? notes[..1500] + "\n…" : notes;
        UpdOverlay.Visibility = Visibility.Visible;
    }

    private void BtnUpdLater_Click(object s, RoutedEventArgs e) => UpdOverlay.Visibility = Visibility.Collapsed;

    private void BtnUpdSkip_Click(object s, RoutedEventArgs e)
    {
        if (_pendingUpdate != null)
        {
            _store.Settings.SkippedVersion = _pendingUpdate.Tag;
            _store.Save();
        }
        UpdOverlay.Visibility = Visibility.Collapsed;
    }

    private async void BtnUpdGo_Click(object s, RoutedEventArgs e)
    {
        var info = _pendingUpdate;
        UpdOverlay.Visibility = Visibility.Collapsed;
        if (info == null) return;
        try
        {
            ShowSplash($"Descargando actualización v{info.Tag}...", indeterminate: false);
            var prog = new Progress<double>(v =>
            {
                SplashBar.Value = v;
                TxtSplashMsg.Text = $"Descargando v{info.Tag}... {v:0}%";
                MainProgress.Value = v; TxtProgressPct.Text = $"{v:0}%";
            });
            await UpdateService.ApplyAsync(info, prog, CancellationToken.None);
            TxtStatus.Text = "Instalador de la actualización lanzado.";
        }
        catch (Exception ex) { ShowError("No se pudo actualizar: " + ex.Message); }
        finally { HideSplash(); }
    }

    // ---------- Navegación ----------
    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string tag) ShowPage(tag);
    }
    private void GotoHistory_Click(object sender, RoutedEventArgs e) => ShowPage("Historial");
    private void BtnNavTools_Click(object sender, RoutedEventArgs e)
    {
        ToolsSubmenu.Visibility = ToolsSubmenu.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }
    private void ShowPage(string key)
    {
        foreach (var kv in _pages) kv.Value.Visibility = kv.Key == key ? Visibility.Visible : Visibility.Collapsed;
        foreach (var kv in _nav)
            kv.Value.Background = kv.Key == key ? new SolidColorBrush(Color.FromRgb(0x14, 0x3B, 0x66)) : Brushes.Transparent;
    }

    // ---------- Titlebar ----------
    private void Titlebar_MouseDown(object sender, MouseButtonEventArgs e)
    { if (e.ChangedButton == MouseButton.Left) DragMove(); }
    private void BtnMin_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void BtnMax_Click(object s, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void BtnClose_Click(object s, RoutedEventArgs e) { _store.Save(); Close(); }

    private void BtnWelcomeStart_Click(object s, RoutedEventArgs e) => WelcomeOverlay.Visibility = Visibility.Collapsed;

    // ---------- Selección / Drag&Drop ----------
    private void BtnSelect_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = ArchiveService.OpenFilter, Multiselect = false };
        if (dlg.ShowDialog() == true) SetCurrentFile(dlg.FileName);
    }
    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0) SetCurrentFile(files[0]);
        }
    }
    private void DropZone_DragEnter(object s, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.Copy; DropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x9B, 0xF4)); }
    }
    private void DropZone_DragLeave(object s, DragEventArgs e) =>
        DropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x6C, 0xB0));

    private void SetCurrentFile(string path)
    {
        _currentFile = path;
        _currentPassword = null;
        PwdBox.Password = "";
        var fi = new FileInfo(path);
        TxtFileName.Text = fi.Name;
        TxtFileType.Text = "Tipo: " + ArchiveService.DetectType(path);
        TxtFileSize.Text = $"Tamaño: {ArchiveService.FormatSize(fi.Length)} ({fi.Length:N0} bytes)";
        TxtFileDate.Text = "Fecha: " + fi.LastWriteTime.ToString("dd/MM/yyyy HH:mm");
        TxtExtractFile.Text = path;
        TxtDropHint.Text = fi.Name;
        TxtStatus.Text = "Analizando contenido...";
        MainProgress.Value = 0; TxtProgressPct.Text = "0%";
        try
        {
            if (ArchiveService.NeedsPassword(path) && string.IsNullOrEmpty(_currentPassword))
            {
                TxtPwdFile.Text = fi.Name;
                PasswordOverlay.Visibility = Visibility.Visible;
                TxtStatus.Text = "Protegido con contraseña.";
                return;
            }
            LoadEntries();
        }
        catch (Exception ex) { ShowError("No se pudo leer el archivo: " + ex.Message); }
    }

    private void LoadEntries()
    {
        if (_currentFile == null) return;
        var pwd = !string.IsNullOrEmpty(PwdBox.Password) ? PwdBox.Password : _currentPassword;
        _currentEntries = ArchiveService.ListEntries(_currentFile, pwd);
        _currentPassword = pwd;
        long comp = new FileInfo(_currentFile).Length;
        long uncomp = _currentEntries.Sum(x => x.Size);
        TxtExtractCount.Text = _currentEntries.Count.ToString();
        TxtCompSize.Text = ArchiveService.FormatSize(comp);
        TxtUncompSize.Text = ArchiveService.FormatSize(uncomp);
        TxtFileContent.Text = $"Contenido: {_currentEntries.Count} archivos · {ArchiveService.FormatSize(uncomp)}";
        TxtStatus.Text = "Archivo listo para extraer.";
        var fi = new FileInfo(_currentFile);
        TxtExtractTitle.Text = fi.Name;
        TxtExtractFile.Text = $"{ArchiveService.DetectType(_currentFile)} — tamaño descomprimido {uncomp:N0} bytes";
        _browsePrefix = "";
        _arcFilter = ""; TxtArcFilter.Text = "";
        RefreshBrowser();
        ShowPage("Extraer");
    }

    private sealed class BrowserRow
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public string Glyph { get; set; } = "";
        public string SizeText { get; set; } = "";
        public string PackedText { get; set; } = "";
        public string TypeText { get; set; } = "";
        public string CrcText { get; set; } = "";
        public string ModifiedText { get; set; } = "";
        public bool IsDir { get; set; }
    }
    private string _browsePrefix = "";
    private string _arcFilter = "";

    private static string KindName(string name)
    {
        string ext = Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".txt" or ".md" or ".log" or ".ini" or ".csv" => "Documento de texto",
            ".json" or ".xml" or ".yml" or ".yaml" or ".toml" => "Datos estructurados",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg" or ".ico" => "Imagen",
            ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a" => "Audio",
            ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" => "Vídeo",
            ".pdf" => "Documento PDF",
            ".exe" or ".msi" => "Aplicación",
            ".dll" => "Biblioteca",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".iso" => "Archivo comprimido",
            "" => "Archivo",
            _ => "Archivo " + ext.TrimStart('.').ToUpperInvariant()
        };
    }

    private void ReloadEntries()
    {
        string keep = _browsePrefix;
        LoadEntries();
        _browsePrefix = keep;
        RefreshBrowser();
    }

    private async void BtnArcAdd_Click(object s, RoutedEventArgs e)
    {
        if (_currentFile == null) { ShowError("Primero selecciona un archivo comprimido."); return; }
        if (!ArchiveService.CanModify(_currentFile)) { ShowError(ArchiveService.ModifyBlockReason(_currentFile)); return; }
        var dlg = new OpenFileDialog { Multiselect = true, Filter = "Todos los archivos|*.*" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _cts = new CancellationTokenSource();
            ShowSplash("Añadiendo archivos...");
            var pwd = string.IsNullOrEmpty(PwdBox.Password) ? _currentPassword : PwdBox.Password;
            var prog = new Progress<double>(v => { MainProgress.Value = v; TxtProgressPct.Text = $"{v:0}%"; });
            await ArchiveService.AddEntriesAsync(_currentFile, dlg.FileNames, pwd, prog, _cts.Token);
            ReloadEntries();
            TxtStatus.Text = "Archivos añadidos correctamente.";
        }
        catch (OperationCanceledException) { TxtStatus.Text = "Operación cancelada."; }
        catch (Exception ex) { ShowError("No se pudo añadir: " + ex.Message); }
        finally { HideSplash(); }
    }

    private async void BtnArcDel_Click(object s, RoutedEventArgs e)
    {
        if (_currentFile == null) { ShowError("Primero selecciona un archivo comprimido."); return; }
        if (!ArchiveService.CanModify(_currentFile)) { ShowError(ArchiveService.ModifyBlockReason(_currentFile)); return; }
        var sel = ExtractList.SelectedItems.OfType<BrowserRow>().Select(r => r.FullPath).ToList();
        if (sel.Count == 0) { ShowError("Selecciona archivos o carpetas para eliminar."); return; }
        var r = MessageBox.Show($"Eliminar {sel.Count} elemento(s) del archivo?\nEsta acción no se puede deshacer.",
            "ExtractX — Eliminar", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;
        try
        {
            _cts = new CancellationTokenSource();
            ShowSplash("Eliminando...");
            var pwd = string.IsNullOrEmpty(PwdBox.Password) ? _currentPassword : PwdBox.Password;
            var prog = new Progress<double>(v => { MainProgress.Value = v; TxtProgressPct.Text = $"{v:0}%"; });
            await ArchiveService.DeleteEntriesAsync(_currentFile, sel, pwd, prog, _cts.Token);
            ReloadEntries();
            TxtStatus.Text = "Selección eliminada.";
        }
        catch (OperationCanceledException) { TxtStatus.Text = "Operación cancelada."; }
        catch (Exception ex) { ShowError("No se pudo eliminar: " + ex.Message); }
        finally { HideSplash(); }
    }

    private void BtnArcFind_Click(object s, RoutedEventArgs e)
    {
        ShowPage("Extraer");
        TxtArcFilter.Focus();
    }

    private void BtnArcWiz_Click(object s, RoutedEventArgs e)
    {
        if (_currentFile == null) { ShowError("Primero selecciona un archivo comprimido."); return; }
        WizOverlay.Visibility = Visibility.Visible;
    }

    private void BtnWizClose_Click(object s, RoutedEventArgs e) => WizOverlay.Visibility = Visibility.Collapsed;

    private async void BtnWizGo_Click(object s, RoutedEventArgs e)
    {
        WizOverlay.Visibility = Visibility.Collapsed;
        if (_currentFile == null) return;
        if (WizExtractAll.IsChecked == true) { BtnExtractHere_Click(s, e); return; }
        if (WizExtractTo.IsChecked == true) { BtnExtractTo_Click(s, e); return; }
        if (WizCheck.IsChecked == true) { ShowPage("Extraer"); BtnVerify_Click(s, e); return; }
        var dlg = new SaveFileDialog { Filter = "ZIP|*.zip", FileName = Path.GetFileNameWithoutExtension(_currentFile) + ".zip" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _cts = new CancellationTokenSource();
            ShowSplash("Convirtiendo a ZIP...");
            var pwd = string.IsNullOrEmpty(PwdBox.Password) ? _currentPassword : PwdBox.Password;
            var prog = new Progress<(double pct, string current)>(t =>
            {
                MainProgress.Value = t.pct;
                TxtProgressPct.Text = $"{t.pct:0}%";
                TxtStatus.Text = string.IsNullOrEmpty(t.current) ? "Convirtiendo..." : t.current;
            });
            await ArchiveService.ConvertToZipAsync(_currentFile, dlg.FileName, pwd, prog, _cts.Token);
            TxtStatus.Text = "Conversión completada.";
            TxtDoneTitle.Text = "Convertido a ZIP";
            TxtDoneDetail.Text = dlg.FileName;
            DoneOverlay.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) { TxtStatus.Text = "Conversión cancelada."; }
        catch (Exception ex) { ShowError("No se pudo convertir: " + ex.Message); }
        finally { HideSplash(); }
    }

    private void BtnArcInfo_Click(object s, RoutedEventArgs e)
    {
        if (_currentFile == null) { ShowError("Primero selecciona un archivo comprimido."); return; }
        var fi = new FileInfo(_currentFile);
        long uncomp = _currentEntries.Sum(x => x.Size);
        double ratio = uncomp > 0 ? (1 - fi.Length / (double)uncomp) * 100 : 0;
        bool enc = false;
        try { enc = ArchiveService.NeedsPassword(_currentFile); } catch { }
        var dirs = _currentEntries.Select(x => x.Name.Replace('\\', '/'))
            .Where(n => n.Contains('/')).Select(n => n[..n.IndexOf('/')])
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();
        TxtInfoBody.Text =
            $"Archivo:      {fi.Name}\n" +
            $"Ruta:         {_currentFile}\n" +
            $"Tipo:         {ArchiveService.DetectType(_currentFile)}\n" +
            $"Tamaño:       {ArchiveService.FormatSize(fi.Length)} ({fi.Length:N0} bytes)\n" +
            $"Sin comprimir:{ArchiveService.FormatSize(uncomp)} ({uncomp:N0} bytes)\n" +
            $"Ratio:        {ratio:0.#} %\n" +
            $"Contenido:    {_currentEntries.Count} archivo(s) en {dirs} carpeta(s)\n" +
            $"Protegido:   {(enc ? "sí (pide contraseña)" : "no")}\n" +
            $"Modificado:   {fi.LastWriteTime:dd/MM/yyyy HH:mm}";
        InfoOverlay.Visibility = Visibility.Visible;
    }

    private void BtnInfoClose_Click(object s, RoutedEventArgs e) => InfoOverlay.Visibility = Visibility.Collapsed;

    // ---------- Menú estilo WinRAR ----------
    private void MenuOpen_Click(object s, RoutedEventArgs e) => BtnSelect_Click(s, e);

    private void MenuClose_Click(object s, RoutedEventArgs e)
    {
        _currentFile = null;
        _currentEntries.Clear();
        _currentPassword = null;
        try { PwdBox.Password = ""; } catch { }
        TxtExtractTitle.Text = "Gestor de archivos";
        TxtExtractFile.Text = "Ningún archivo cargado. Usa Inicio para seleccionar uno.";
        TxtExtractCount.Text = "0"; TxtCompSize.Text = "—"; TxtUncompSize.Text = "—";
        TxtArcStatus.Text = "—";
        RefreshBrowser();
        ShowPage("Inicio");
    }

    private void MenuRepairZip_Click(object s, RoutedEventArgs e)
    {
        if (_currentFile != null) TxtToolInput.Text = _currentFile;
        ShowPage("Tools");
    }

    private void MenuBench_Click(object s, RoutedEventArgs e)
    {
        ShowPage("Tools");
        ToolBench_Click(s, e);
    }

    private void MenuConfig_Click(object s, RoutedEventArgs e) => ShowPage("Config");
    private void MenuAbout_Click(object s, RoutedEventArgs e) => ShowPage("Acerca");

    private void RefreshExtractList(string filter)
    {
        _arcFilter = filter?.Trim() ?? "";
        RefreshBrowser();
    }

    private void RefreshBrowser()
    {
        ExtractList.Items.Clear();
        string filt = _arcFilter;
        if (!string.IsNullOrEmpty(filt))
        {
            // Búsqueda plana en todo el archivo
            foreach (var e in _currentEntries
                         .Where(x => x.Name.Contains(filt, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(x => x.Name))
            {
                string full = e.Name.Replace('\\', '/');
                ExtractList.Items.Add(new BrowserRow
                {
                    Name = full, FullPath = full, Glyph = "•", IsDir = false,
                    SizeText = e.SizeText,
                    PackedText = ArchiveService.FormatSize(e.CompressedSize),
                    TypeText = KindName(full),
                    CrcText = e.CrcText,
                    ModifiedText = e.Modified?.ToString("dd/MM/yyyy HH:mm") ?? ""
                });
            }
            TxtCrumb.Text = $"Búsqueda: {filt} ({ExtractList.Items.Count})";
            BtnUp.IsEnabled = _browsePrefix.Length > 0;
            UpdateArcStatus();
            return;
        }
        var dirs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<EntryInfo>();
        foreach (var e in _currentEntries)
        {
            string n = e.Name.Replace('\\', '/');
            if (!n.StartsWith(_browsePrefix, StringComparison.OrdinalIgnoreCase)) continue;
            string rest = n[_browsePrefix.Length..];
            if (string.IsNullOrEmpty(rest)) continue;
            int slash = rest.IndexOf('/');
            if (slash >= 0) dirs.Add(rest[..slash]);
            else files.Add(e);
        }
        foreach (var d in dirs)
            ExtractList.Items.Add(new BrowserRow
            {
                Name = d, FullPath = _browsePrefix + d + "/", Glyph = "▸",
                IsDir = true, SizeText = "—", PackedText = "—",
                TypeText = "Carpeta de archivos", CrcText = "—", ModifiedText = ""
            });
        foreach (var e in files.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            string full = e.Name.Replace('\\', '/');
            ExtractList.Items.Add(new BrowserRow
            {
                Name = full[_browsePrefix.Length..], FullPath = full, Glyph = "•",
                IsDir = false, SizeText = e.SizeText,
                PackedText = ArchiveService.FormatSize(e.CompressedSize),
                TypeText = KindName(full),
                CrcText = e.CrcText,
                ModifiedText = e.Modified?.ToString("dd/MM/yyyy HH:mm") ?? ""
            });
        }
        if (_browsePrefix.Length > 0)
            ExtractList.Items.Insert(0, new BrowserRow
            {
                Name = "..", FullPath = "..UP..", Glyph = "↑",
                IsDir = true, SizeText = "—", PackedText = "—",
                TypeText = "Carpeta de archivos", CrcText = "—", ModifiedText = ""
            });
        TxtCrumb.Text = "/" + _browsePrefix.TrimEnd('/');
        BtnUp.IsEnabled = _browsePrefix.Length > 0;
        UpdateArcStatus();
    }

    private void UpdateArcStatus()
    {
        int ndirs = ExtractList.Items.OfType<BrowserRow>().Count(r => r.IsDir);
        var files = ExtractList.Items.OfType<BrowserRow>().Where(r => !r.IsDir).ToList();
        long total = _currentEntries.Sum(e => e.Size);
        TxtArcStatus.Text = $"Total {ndirs} carpeta(s), {files.Count} archivo(s) · " +
            $"Descomprimido: {ArchiveService.FormatSize(total)}";
    }

    private void ExtractList_DoubleClick(object s, MouseButtonEventArgs e)
    {
        if (ExtractList.SelectedItem is not BrowserRow row) return;
        if (row.FullPath == "..UP..") { BtnUp_Click(s, new RoutedEventArgs()); return; }
        if (row.IsDir)
        {
            _browsePrefix = row.FullPath;
            _arcFilter = ""; TxtArcFilter.Text = "";
            RefreshBrowser();
        }
        else OpenEntryTemp(row.FullPath);
    }

    private void BtnUp_Click(object s, RoutedEventArgs e)
    {
        string t = _browsePrefix.TrimEnd('/');
        int i = t.LastIndexOf('/');
        _browsePrefix = i < 0 ? "" : t[..(i + 1)];
        RefreshBrowser();
    }

    private void TxtArcFilter_Changed(object sender, TextChangedEventArgs e)
    {
        _arcFilter = TxtArcFilter.Text.Trim();
        if (_currentEntries.Count > 0) RefreshBrowser();
    }

    private void BtnOpenSel_Click(object s, RoutedEventArgs e)
    {
        var row = ExtractList.SelectedItems.OfType<BrowserRow>().FirstOrDefault(r => !r.IsDir);
        if (row == null) { ShowError("Selecciona un archivo para abrirlo."); return; }
        OpenEntryTemp(row.FullPath);
    }

    private async void OpenEntryTemp(string fullPath)
    {
        if (_currentFile == null) return;
        try
        {
            ShowSplash($"Abriendo {Path.GetFileName(fullPath.Replace('/', Path.DirectorySeparatorChar))}...");
            string tmp = Path.Combine(Path.GetTempPath(), "ExtractX_open", Path.GetFileName(_currentFile));
            Directory.CreateDirectory(tmp);
            var pwd = string.IsNullOrEmpty(PwdBox.Password) ? _currentPassword : PwdBox.Password;
            await ArchiveService.ExtractEntriesAsync(_currentFile, tmp, new[] { fullPath }, pwd, null, CancellationToken.None);
            string disk = Path.Combine(tmp, fullPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(disk))
            {
                var all = Directory.GetFiles(tmp, "*", SearchOption.AllDirectories);
                disk = all.FirstOrDefault(f => f.EndsWith(Path.GetFileName(fullPath), StringComparison.OrdinalIgnoreCase))
                    ?? all.FirstOrDefault() ?? throw new FileNotFoundException("Extracción vacía.");
            }
            Process.Start(new ProcessStartInfo(disk) { UseShellExecute = true });
        }
        catch (Exception ex) { ShowError("No se pudo abrir: " + ex.Message); }
        finally { HideSplash(); }
    }

    private async void BtnExtractSel_Click(object s, RoutedEventArgs e)
    {
        if (_currentFile == null) { ShowError("Primero selecciona un archivo comprimido."); return; }
        var sel = ExtractList.SelectedItems.OfType<BrowserRow>().Select(r => r.FullPath).ToList();
        if (sel.Count == 0) { ShowError("Selecciona archivos o carpetas de la lista."); return; }
        var files = ArchiveService.ExpandSelection(_currentEntries, sel);
        if (files.Count == 0) { ShowError("Nada que extraer en la selección."); return; }
        var dlg = new OpenFolderDialog { Title = "Carpeta destino (selección)" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _cts = new CancellationTokenSource();
            ShowSplash($"Extrayendo {files.Count} elemento(s)...");
            var pwd = string.IsNullOrEmpty(PwdBox.Password) ? _currentPassword : PwdBox.Password;
            var prog = new Progress<(double pct, string current)>(t =>
            {
                MainProgress.Value = t.pct;
                TxtProgressPct.Text = $"{t.pct:0}%";
                TxtStatus.Text = string.IsNullOrEmpty(t.current) ? "Extrayendo..." : $"Extrayendo: {t.current}";
            });
            await ArchiveService.ExtractEntriesAsync(_currentFile, dlg.FolderName, files, pwd, prog, _cts.Token);
            _store.History.Insert(0, new HistoryEntry
            {
                FileName = Path.GetFileName(_currentFile) + $" ({files.Count} sel.)",
                SourcePath = _currentFile, Destination = dlg.FolderName,
                SizeBytes = 0, SizeText = $"{files.Count} archivos",
                Date = DateTime.Now, Status = "Extraído", Success = true
            });
            _store.Save(); RefreshAll();
            TxtDoneTitle.Text = "Selección extraída";
            TxtDoneDetail.Text = $"{files.Count} elemento(s) → {dlg.FolderName}";
            DoneOverlay.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) { TxtStatus.Text = "Extracción cancelada."; }
        catch (Exception ex) { ShowError("Error al extraer: " + ex.Message); }
        finally { HideSplash(); }
    }

    // ---------- Extracción ----------
    private void BtnExtractHere_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFile == null) { ShowError("Primero selecciona un archivo comprimido."); return; }
        StartExtract(Path.GetDirectoryName(_currentFile)!);
    }
    private void BtnExtractTo_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFile == null) { ShowError("Primero selecciona un archivo comprimido."); return; }
        var dlg = new OpenFolderDialog { Title = "Carpeta destino" };
        if (dlg.ShowDialog() == true) StartExtract(dlg.FolderName);
    }

    private void StartExtract(string dest)
    {
        if (_currentFile == null) return;
        string pwd = !string.IsNullOrEmpty(PwdBox.Password) ? PwdBox.Password : (_currentPassword ?? "");
        if (ArchiveService.NeedsPassword(_currentFile) && string.IsNullOrEmpty(pwd))
        {
            _pendingDest = dest;
            TxtPwdFile.Text = Path.GetFileName(_currentFile);
            PwdDialogBox.Password = "";
            PasswordOverlay.Visibility = Visibility.Visible;
            return;
        }
        _ = RunExtractAsync(_currentFile, dest, string.IsNullOrEmpty(pwd) ? null : pwd);
    }

    private async Task RunExtractAsync(string file, string dest, string? pwd)
    {
        _cts = new CancellationTokenSource();
        BtnCancelOp.Visibility = Visibility.Visible;
        LogoFx.Spin(HeroLogo);
        var fileName = Path.GetFileName(file);
        var fileLen = new FileInfo(file).Length;
        ShowPage("Inicio");
        ShowSplash($"Extrayendo {fileName}...");
        try
        {
            var progress = new Progress<(double pct, string current)>(t =>
            {
                MainProgress.Value = t.pct;
                TxtProgressPct.Text = $"{t.pct:0}%";
                TxtStatus.Text = string.IsNullOrEmpty(t.current) ? "Extrayendo..." : $"Extrayendo: {t.current}";
            });
            await ArchiveService.ExtractAsync(file, dest, pwd, progress, _cts.Token);
            _lastDest = dest;
            _store.History.Insert(0, new HistoryEntry
            {
                FileName = fileName, SourcePath = file, Destination = dest,
                SizeBytes = fileLen, SizeText = ArchiveService.FormatSize(fileLen),
                Date = DateTime.Now, Status = "Extraído", Success = true
            });
            _store.Save();
            RefreshAll();
            TxtStatus.Text = "Extracción completada correctamente.";
            TxtDoneDetail.Text = $"{fileName} → {dest}";
            DoneOverlay.Visibility = Visibility.Visible;
            if (_store.Settings.AutoOpen)
                try { Process.Start("explorer.exe", dest); } catch { }
        }
        catch (OperationCanceledException) { TxtStatus.Text = "Extracción cancelada."; }
        catch (UnauthorizedAccessException ex) { ShowError(ex.Message); }
        catch (Exception ex) { ShowError("Error al extraer: " + ex.Message); }
        finally { BtnCancelOp.Visibility = Visibility.Collapsed; LogoFx.Stop(HeroLogo); HideSplash(); }
    }

    private void BtnCancelOp_Click(object s, RoutedEventArgs e) => _cts?.Cancel();

    // ---------- Password overlay ----------
    private void BtnPwdCancel_Click(object s, RoutedEventArgs e) { PasswordOverlay.Visibility = Visibility.Collapsed; _pendingDest = null; }
    private void BtnPwdOk_Click(object s, RoutedEventArgs e)
    {
        PasswordOverlay.Visibility = Visibility.Collapsed;
        var pwd = PwdDialogBox.Password;
        if (_currentFile == null) return;
        PwdBox.Password = pwd;
        try { LoadEntries(); }
        catch (Exception ex) { ShowError("Contraseña incorrecta o archivo ilegible: " + ex.Message); return; }
        if (_pendingDest != null) { var d = _pendingDest; _pendingDest = null; _ = RunExtractAsync(_currentFile, d, pwd); }
    }

    // ---------- Overlays ----------
    private void ShowError(string msg) { TxtErrorMsg.Text = msg; ErrorOverlay.Visibility = Visibility.Visible; }
    private void BtnErrorClose_Click(object s, RoutedEventArgs e) => ErrorOverlay.Visibility = Visibility.Collapsed;
    private void BtnDoneClose_Click(object s, RoutedEventArgs e) => DoneOverlay.Visibility = Visibility.Collapsed;
    private void BtnDoneOpen_Click(object s, RoutedEventArgs e)
    {
        DoneOverlay.Visibility = Visibility.Collapsed;
        if (Directory.Exists(_lastDest)) try { Process.Start("explorer.exe", _lastDest); } catch { }
    }

    // ---------- Listas ----------
    private void RefreshAll()
    {
        RecentList.Items.Clear();
        foreach (var h in _store.History.Take(8))
            RecentList.Items.Add($"{h.Date:dd/MM/yyyy HH:mm}   {h.FileName}   {h.SizeText}   {(h.Success ? "Extraído" : "Error")}");
        HistoryList.Items.Clear();
        foreach (var h in _store.History)
            HistoryList.Items.Add($"{h.Date:dd/MM/yyyy HH:mm}  │  {h.FileName}  │  {h.SizeText}  │  {h.Destination}  │  {h.Status}");
        FavList.Items.Clear();
        foreach (var f in _store.Favorites) FavList.Items.Add($"{f.Name}  →  {f.Path}");
        PassList.Items.Clear();
        foreach (var p in _store.Passwords) PassList.Items.Add($"{p.Label}  ·  {new string('•', Math.Min(8, p.Password.Length))}");
        MasivaList.Items.Clear();
        foreach (var f in _masivaFiles) MasivaList.Items.Add(f);
        TxtCompressInfo.Text = _compressFiles.Count == 0 ? "Sin archivos añadidos." : $"{_compressFiles.Count} archivos listos para comprimir.";
    }

    private void RecentList_DoubleClick(object s, MouseButtonEventArgs e)
    {
        var h = _store.History.FirstOrDefault(x => RecentList.SelectedItem?.ToString()?.Contains(x.FileName) == true);
        if (h != null && File.Exists(h.SourcePath)) SetCurrentFile(h.SourcePath);
    }
    private void HistoryList_DoubleClick(object s, MouseButtonEventArgs e)
    {
        var h = _store.History.FirstOrDefault(x => HistoryList.SelectedItem?.ToString()?.Contains(x.FileName) == true);
        if (h != null && Directory.Exists(h.Destination)) try { Process.Start("explorer.exe", h.Destination); } catch { }
    }
    private void BtnClearHistory_Click(object s, RoutedEventArgs e) { _store.History.Clear(); _store.Save(); RefreshAll(); }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var q = TxtSearch.Text.Trim();
        RefreshExtractList(q);
        if (string.IsNullOrEmpty(q)) { RefreshAll(); return; }
        RecentList.Items.Clear();
        foreach (var h in _store.History.Where(x => x.FileName.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(20))
            RecentList.Items.Add($"{h.Date:dd/MM/yyyy HH:mm}   {h.FileName}   {h.SizeText}");
    }

    // ---------- Verificar / Preview ----------
    private void BtnVerify_Click(object s, RoutedEventArgs e)
    {
        if (_currentFile == null) { TxtVerifyResult.Text = "Sin archivo."; return; }
        var pwd = string.IsNullOrEmpty(PwdBox.Password) ? null : PwdBox.Password;
        bool ok = ArchiveService.Verify(_currentFile, pwd);
        TxtVerifyResult.Text = ok ? "✔ Integridad correcta. CRC válido." : "✘ Archivo dañado o contraseña incorrecta.";
        TxtVerifyResult.Foreground = ok ? new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)) : new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
    }

    private async void BtnPreview_Click(object s, RoutedEventArgs e)
    {
        if (_currentFile == null) { TxtPreview.Text = "Sin archivo."; return; }
        var row = ExtractList.SelectedItem as BrowserRow;
        if (row == null || row.IsDir) { TxtPreview.Text = "Selecciona un archivo de la lista."; return; }
        var name = row.FullPath;
        var ext = Path.GetExtension(name).ToLowerInvariant();
        if (!new[] { ".txt", ".md", ".json", ".xml", ".csv", ".log", ".ini" }.Contains(ext))
        { TxtPreview.Text = $"Vista previa no disponible para {ext}. Solo texto plano (doble clic para abrirlo)."; return; }
        try
        {
            var pwd = string.IsNullOrEmpty(PwdBox.Password) ? _currentPassword : PwdBox.Password;
            var tmp = Path.Combine(Path.GetTempPath(), "ExtractX_preview");
            Directory.CreateDirectory(tmp);
            foreach (var f in Directory.GetFiles(tmp)) try { File.Delete(f); } catch { }
            await ArchiveService.ExtractEntriesAsync(_currentFile, tmp, new[] { name }, pwd, null, CancellationToken.None);
            var disk = Path.Combine(tmp, name.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(disk))
                disk = Directory.GetFiles(tmp, "*", SearchOption.AllDirectories).FirstOrDefault()
                    ?? throw new FileNotFoundException("Extracción vacía.");
            var text = File.ReadAllText(disk);
            TxtPreview.Text = text.Length > 4000 ? text[..4000] + "\n…(truncado)" : text;
        }
        catch (Exception ex) { TxtPreview.Text = "Error: " + ex.Message; }
    }

    // ---------- Comprimir ----------
    private void BtnCompressSelect_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Multiselect = true, Filter = "Todos|*.*" };
        if (dlg.ShowDialog() == true) { _compressFiles.AddRange(dlg.FileNames); RefreshAll(); }
    }
    private async void BtnCompressGo_Click(object s, RoutedEventArgs e)
    {
        if (_compressFiles.Count == 0) { ShowError("Añade archivos para comprimir primero."); return; }
        string format = (CmbCompressFormat.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "ZIP";
        var level = ArchiveService.ParseLevel((CmbCompressLevel.SelectedItem as ComboBoxItem)?.Content?.ToString());
        bool solid = ChkSolid.IsChecked == true;
        string? pwd = string.IsNullOrEmpty(PwdCompress.Password) ? null : PwdCompress.Password;
        string ext = ArchiveService.DefaultExtension(format);
        var dlg = new SaveFileDialog { Filter = $"{format}|*{ext}", FileName = "archivo" + ext };
        if (dlg.ShowDialog() != true) return;
        LogoFx.Spin(HeroLogo);
        ShowSplash($"Comprimiendo a {format}...");
        try
        {
            var p = new Progress<double>(v => { MainProgress.Value = v; TxtProgressPct.Text = $"{v:0}%"; TxtStatus.Text = $"Comprimiendo a {format}..."; });
            await ArchiveService.CompressAsync(_compressFiles, dlg.FileName, format, pwd, p, CancellationToken.None, level, solid);
            _compressFiles.Clear(); PwdCompress.Password = ""; RefreshAll();
            TxtStatus.Text = "Compresión completada correctamente.";
            TxtDoneDetail.Text = $"Comprimido → {dlg.FileName}";
            TxtDoneTitle.Text = "Compresión completada";
            DoneOverlay.Visibility = Visibility.Visible;
        }
        catch (Exception ex) { ShowError("Error al comprimir: " + ex.Message); }
        finally { LogoFx.Stop(HeroLogo); HideSplash(); }
    }

    // ---------- Masiva ----------
    private void BtnMasivaAdd_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = ArchiveService.OpenFilter, Multiselect = true };
        if (dlg.ShowDialog() == true) { _masivaFiles.AddRange(dlg.FileNames); RefreshAll(); }
    }
    private async void BtnMasivaGo_Click(object s, RoutedEventArgs e)
    {
        if (_masivaFiles.Count == 0) { ShowError("Añade archivos primero."); return; }
        var dlg = new OpenFolderDialog { Title = "Carpeta destino masiva" };
        if (dlg.ShowDialog() != true) return;
        ShowSplash("Extracción masiva en curso...");
        int i = 0;
        foreach (var f in _masivaFiles.ToList())
        {
            try
            {
                await ArchiveService.ExtractAsync(f, dlg.FolderName, null,
                    new Progress<(double, string)>(t => MasivaProgress.Value = (i * 100.0 + t.Item1) / _masivaFiles.Count),
                    CancellationToken.None);
                _store.History.Insert(0, new HistoryEntry
                {
                    FileName = Path.GetFileName(f), SourcePath = f, Destination = dlg.FolderName,
                    SizeBytes = new FileInfo(f).Length, SizeText = ArchiveService.FormatSize(new FileInfo(f).Length),
                    Date = DateTime.Now, Status = "Extraído", Success = true
                });
            }
            catch { _store.History.Insert(0, new HistoryEntry { FileName = Path.GetFileName(f), SourcePath = f, Destination = dlg.FolderName, Date = DateTime.Now, Status = "Error", Success = false }); }
            i++;
        }
        _store.Save(); RefreshAll();
        MasivaProgress.Value = 100;
        HideSplash();
        TxtDoneTitle.Text = "Extracción masiva completada";
        TxtDoneDetail.Text = $"{i} archivos → {dlg.FolderName}";
        DoneOverlay.Visibility = Visibility.Visible;
    }

    // ---------- Favoritos ----------
    private void BtnFavBrowse_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() == true) TxtFavPath.Text = dlg.FolderName;
    }
    private void BtnFavAdd_Click(object s, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtFavPath.Text)) return;
        _store.Favorites.Add(new FavoriteFolder { Name = string.IsNullOrWhiteSpace(TxtFavName.Text) ? Path.GetFileName(TxtFavPath.Text.TrimEnd('\\')) : TxtFavName.Text, Path = TxtFavPath.Text });
        _store.Save(); RefreshAll(); TxtFavName.Text = ""; TxtFavPath.Text = "";
    }
    private void FavList_DoubleClick(object s, MouseButtonEventArgs e)
    {
        var f = _store.Favorites.FirstOrDefault(x => FavList.SelectedItem?.ToString()?.Contains(x.Name) == true);
        if (f != null && Directory.Exists(f.Path)) try { Process.Start("explorer.exe", f.Path); } catch { }
    }

    // ---------- Contraseñas ----------
    private void BtnPassAdd_Click(object s, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtPassValue.Text)) return;
        _store.Passwords.Add(new SavedPassword { Label = string.IsNullOrWhiteSpace(TxtPassLabel.Text) ? "Archivo" : TxtPassLabel.Text, Password = TxtPassValue.Text });
        _store.Save(); RefreshAll(); TxtPassLabel.Text = ""; TxtPassValue.Text = "";
    }
    private void BtnPassDel_Click(object s, RoutedEventArgs e)
    {
        if (PassList.SelectedIndex >= 0 && PassList.SelectedIndex < _store.Passwords.Count)
        { _store.Passwords.RemoveAt(PassList.SelectedIndex); _store.Save(); RefreshAll(); }
    }

    // ---------- Herramientas ----------
    private void BtnToolBrowse_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = ArchiveService.OpenFilter };
        if (dlg.ShowDialog() == true) TxtToolInput.Text = dlg.FileName;
    }
    private void BtnToolRunVerify_Click(object s, RoutedEventArgs e)
    {
        if (!File.Exists(TxtToolInput.Text)) { TxtToolOutput.Text = "Selecciona un archivo válido."; return; }
        var sw = Stopwatch.StartNew();
        bool ok = ArchiveService.Verify(TxtToolInput.Text, null);
        sw.Stop();
        TxtToolOutput.Text = ok
            ? $"VERIFICACIÓN CORRECTA\nArchivo: {TxtToolInput.Text}\nTiempo: {sw.ElapsedMilliseconds} ms\nCRC: válido en todas las entradas."
            : $"VERIFICACIÓN FALLIDA\nEl archivo está dañado, incompleto o cifrado.";
    }
    private void ToolRepair_Click(object sender, RoutedEventArgs e)
    {
        var kind = (sender as Button)?.Tag?.ToString() ?? "zip";
        var dlg = new OpenFileDialog { Filter = kind == "zip" ? "ZIP|*.zip" : "RAR|*.rar" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var outPath = Path.Combine(Path.GetDirectoryName(dlg.FileName)!, Path.GetFileNameWithoutExtension(dlg.FileName) + "_reparado.zip");
            using var zin = System.IO.Compression.ZipFile.OpenRead(dlg.FileName);
            using var zout = System.IO.Compression.ZipFile.Open(outPath, ZipArchiveMode.Create);
            int okCount = 0, bad = 0;
            foreach (var entry in zin.Entries)
            {
                try
                {
                    using var s = entry.Open();
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    var ne = zout.CreateEntry(entry.FullName);
                    using var ds = ne.Open();
                    ds.Write(ms.ToArray());
                    okCount++;
                }
                catch { bad++; }
            }
            TxtToolOutput.Text = $"REPARACIÓN {kind.ToUpper()}\nEntradas recuperadas: {okCount}\nEntradas dañadas: {bad}\nSalida: {outPath}";
            ShowPage("Tools");
        }
        catch (Exception ex) { ShowError("No se pudo reparar: " + ex.Message); }
    }
    private void ToolBench_Click(object? s, RoutedEventArgs? e)
    {
        ShowPage("Tools");
        var sw = Stopwatch.StartNew();
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var rnd = new Random(42);
            var buf = new byte[1024 * 1024];
            rnd.NextBytes(buf);
            for (int i = 0; i < 8; i++)
            {
                var en = zip.CreateEntry($"bench_{i}.bin");
                using var st = en.Open(); st.Write(buf, 0, buf.Length);
            }
        }
        long compLen = ms.Length;
        sw.Stop();
        TxtToolOutput.Text = $"BENCHMARK ExtractX 1.0\nCompresión 8 MB → {ArchiveService.FormatSize(compLen)} en {sw.ElapsedMilliseconds} ms\nMotor: SharpCompress + Deflate\nCPU: {Environment.ProcessorCount} núcleos · {Environment.OSVersion}";
    }

    // ---------- Config ----------
    private void LoadConfigToUi()
    {
        var c = _store.Settings;
        CmbTheme.SelectedIndex = c.Theme == "Claro" ? 1 : 0;
        CmbLang.SelectedIndex = c.Language switch { "English" => 1, "Português" => 2, _ => 0 };
        ChkAutoOpen.IsChecked = c.AutoOpen;
        ChkUpdates.IsChecked = c.CheckUpdates;
        ChkZip.IsChecked = c.AssociateZip; ChkRar.IsChecked = c.AssociateRar; Chk7z.IsChecked = c.Associate7z;
        SelectCombo(CmbDefFormat, c.DefaultCompressFormat);
        SelectCombo(CmbDefLevel, c.DefaultCompressLevel);
        TxtRepo.Text = c.UpdateRepo;
        TxtFavName.Text = "Nueva carpeta";
    }
    private void CmbTheme_Changed(object s, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (CmbTheme.SelectedItem is ComboBoxItem it && Application.Current != null)
        {
            bool light = it.Content?.ToString() == "Claro";
            void Set(string key, byte r, byte g, byte b)
            {
                var nb = new SolidColorBrush(Color.FromRgb(r, g, b));
                Application.Current.Resources[key] = nb;
                Resources[key] = new SolidColorBrush(Color.FromRgb(r, g, b));
            }
            if (light) { Set("BgBrush", 0xF1, 0xF5, 0xF9); Set("PanelBrush", 0xFF, 0xFF, 0xFF); Set("CardBrush", 0xFF, 0xFF, 0xFF); }
            else { Set("BgBrush", 0x0B, 0x11, 0x17); Set("PanelBrush", 0x12, 0x1A, 0x24); Set("CardBrush", 0x16, 0x1E, 0x2A); }
        }
    }
    private void BtnSaveConfig_Click(object s, RoutedEventArgs e)
    {
        _store.Settings.Theme = (CmbTheme.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Oscuro";
        _store.Settings.Language = (CmbLang.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Español";
        _store.Settings.AutoOpen = ChkAutoOpen.IsChecked == true;
        _store.Settings.CheckUpdates = ChkUpdates.IsChecked == true;
        _store.Settings.AssociateZip = ChkZip.IsChecked == true;
        _store.Settings.AssociateRar = ChkRar.IsChecked == true;
        _store.Settings.Associate7z = Chk7z.IsChecked == true;
        _store.Settings.DefaultCompressFormat = (CmbDefFormat.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "ZIP";
        _store.Settings.DefaultCompressLevel = (CmbDefLevel.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Normal";
        _store.Settings.UpdateRepo = TxtRepo.Text.Trim();
        if (!string.IsNullOrEmpty(PwdToken.Password)) UpdateService.SaveToken(PwdToken.Password);
        _store.Save();
        ApplyCompressDefaults();
        ApplyAssociations();
        TxtConfigMsg.Text = "✔ Configuración guardada. Doble clic y menú ya abren ExtractX.";
    }

    private void ApplyAssociations()
    {
        var map = new Dictionary<string, bool>
        {
            [".zip"] = _store.Settings.AssociateZip,
            [".rar"] = _store.Settings.AssociateRar,
            [".7z"] = _store.Settings.Associate7z,
        };
        foreach (var kv in map)
        {
            if (kv.Value) FileAssoc.Associate(kv.Key);
            else if (FileAssoc.IsAssociated(kv.Key)) FileAssoc.Unassociate(kv.Key);
        }
    }

    /// <summary>Abre la mini ventana para el archivo actual (modo compacto).</summary>
    private void BtnCompact_Click(object s, RoutedEventArgs e)
    {
        if (_currentFile != null && File.Exists(_currentFile))
        {
            new MiniWindow(_currentFile).Show();
            WindowState = WindowState.Minimized;
        }
        else
        {
            ShowError("Primero selecciona un archivo para verlo en modo compacto.");
        }
    }

    /// <summary>Permite a la mini ventana devolver un archivo al modo completo.</summary>
    public void LoadExternalFile(string path)
    {
        WelcomeOverlay.Visibility = Visibility.Collapsed;
        SetCurrentFile(path);
    }
    private async void BtnCheckUpdates_Click(object s, RoutedEventArgs e)
    {
        TxtConfigMsg.Text = "Buscando actualizaciones...";
        var info = await UpdateService.CheckAsync(_store.Settings.UpdateRepo, UpdateService.LoadToken());
        if (info == null) { TxtConfigMsg.Text = $"✔ Tienes la última versión (v{UpdateService.CurrentVersion})."; return; }
        TxtConfigMsg.Text = $"Nueva versión: v{info.Tag}.";
        ShowUpdateDialog(info);
    }

    private void AboutLink_Click(object s, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(((Button)s).Tag?.ToString() ?? "https://example.com") { UseShellExecute = true }); } catch { }
    }
    private void AboutLicense_Click(object s, RoutedEventArgs e)
        => ShowError("Licencia MIT — © 2026 ExtractX. Uso libre personal y comercial.");
}
