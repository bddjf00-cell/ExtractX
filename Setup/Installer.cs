using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using Microsoft.Win32;
using System.Net.Http;

namespace ExtractX.Setup;

public sealed record InstallOptions(
    string Directory,
    bool AllUsers,
    List<string> Formats,
    bool StartMenu,
    bool Desktop,
    bool LaunchAfter);

public static class Installer
{
    public const string AppName = "ExtractX";
    public const string ProgId = "ExtractX.archive";
    public const string Version = "1.2.1";
    public const string PayloadName = "ExtractX-v1.2.1.exe";
    public const string AppExeName = "ExtractX.exe";

    public static readonly string[] AllFormats = { ".zip", ".rar", ".7z", ".tar", ".gz", ".iso" };

    public static bool IsAdmin()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public static void RelaunchElevated(string args)
    {
        var exe = Environment.ProcessPath!;
        Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true, Verb = "runas" });
    }

    public static string DefaultDir(bool allUsers) => allUsers
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);

    private static RegistryKey Hive(bool allUsers) => allUsers ? Registry.LocalMachine : Registry.CurrentUser;

    /// <summary>
    /// Busca el payload junto al setup (dist/) o en la raíz del proyecto.
    /// Tolerante a versiones: acepta ExtractX-v*.exe si el nombre exacto no está.
    /// </summary>
    public static string? FindPayload()
    {
        var exe = Environment.ProcessPath!;
        var dir = Path.GetDirectoryName(exe)!;
        foreach (var c in new[]
        {
            Path.Combine(dir, PayloadName),
            Path.Combine(dir, AppExeName),
            Path.Combine(dir, "..", PayloadName),
        })
            if (File.Exists(c)) return Path.GetFullPath(c);
        foreach (var d in new[] { dir, Path.Combine(dir, "..") })
        {
            try
            {
                var best = Directory.GetFiles(d, "ExtractX-v*.exe")
                    .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (best != null) return Path.GetFullPath(best);
            }
            catch { }
        }
        return null;
    }

    public static string? FindInstalledDir()
    {
        foreach (var h in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var k = h.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName);
                var d = k?.GetValue("InstallLocation")?.ToString();
                if (!string.IsNullOrEmpty(d) && Directory.Exists(d)) return d;
            }
            catch { }
        }
        // Detección por defecto si quedó resto sin registro
        var def = DefaultDir(false);
        if (File.Exists(Path.Combine(def, AppExeName))) return def;
        return null;
    }

    public static async Task InstallAsync(InstallOptions o, IProgress<(int pct, string msg)>? progress, CancellationToken ct)
    {
        await EnsureRuntimeAsync(progress, ct);

        var payload = await EnsurePayloadAsync(progress, ct);
        Report(progress, 4, "Creando carpetas...");
        Directory.CreateDirectory(o.Directory);

        var destExe = Path.Combine(o.Directory, AppExeName);
        await CopyWithProgressAsync(payload, destExe, 6, 58, progress, ct);

        Report(progress, 60, "Copiando motor 7-Zip...");
        CopyDir(Path.Combine(Path.GetDirectoryName(payload)!, "redist"), Path.Combine(o.Directory, "redist"));

        Report(progress, 72, "Registrando desinstalador...");
        try { File.Copy(Environment.ProcessPath!, Path.Combine(o.Directory, "Uninstall.exe"), true); } catch { }
        WriteUninstallEntry(o);

        if (o.StartMenu || o.Desktop)
        {
            Report(progress, 78, "Creando accesos directos...");
            if (o.StartMenu)
            {
                var sm = Path.Combine(Environment.GetFolderPath(o.AllUsers
                    ? Environment.SpecialFolder.CommonStartMenu : Environment.SpecialFolder.StartMenu), "Programs", AppName);
                Directory.CreateDirectory(sm);
                CreateShortcut(Path.Combine(sm, AppName + ".lnk"), destExe, "", "ExtractX — extrae cualquier archivo en segundos");
            }
            if (o.Desktop)
            {
                var dk = Environment.GetFolderPath(o.AllUsers
                    ? Environment.SpecialFolder.CommonDesktopDirectory : Environment.SpecialFolder.DesktopDirectory);
                CreateShortcut(Path.Combine(dk, AppName + ".lnk"), destExe, "", "ExtractX");
            }
        }

        Report(progress, 86, "Asociando formatos y menús...");
        ApplyRegistry(o);

        Report(progress, 100, "Completado.");
    }

    public static async Task UninstallAsync(IProgress<(int pct, string msg)>? progress, CancellationToken ct)
    {
        var dir = FindInstalledDir();
        bool allUsers = false;
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName);
            allUsers = k != null;
        }
        catch { }
        var root = Hive(allUsers);

        Report(progress, 10, "Quitando accesos directos...");
        foreach (var baseDir in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        })
        {
            TryDelete(Path.Combine(baseDir, AppName, AppName + ".lnk"));
            TryDelete(Path.Combine(baseDir, AppName + ".lnk"));
            TryDeleteDir(Path.Combine(baseDir, AppName));
        }

        Report(progress, 35, "Restaurando asociaciones...");
        try
        {
            var installed = AllFormats.ToList();
            try
            {
                var f = root.OpenSubKey(@"Software\ExtractX\Installed")?.GetValue("Formats")?.ToString();
                if (!string.IsNullOrWhiteSpace(f))
                    installed = f.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }
            catch { }
            foreach (var ext in installed)
            {
                try
                {
                    using var backup = root.OpenSubKey(@"Software\ExtractX\Backup");
                    var prev = backup?.GetValue(ext)?.ToString();
                    using var k = root.OpenSubKey(@"Software\Classes\" + ext, true);
                    if (k?.GetValue("")?.ToString() == ProgId)
                    {
                        if (!string.IsNullOrEmpty(prev)) k.SetValue("", prev);
                        else k.DeleteValue("", false);
                    }
                }
                catch { }
                foreach (var sub in new[] { $@"Software\Classes\SystemFileAssociations\{ext}\shell\ExtractX.Extract", $@"Software\Classes\SystemFileAssociations\{ext}\shell\ExtractX.Open" })
                    TryDeleteKey(root, sub);
            }
            TryDeleteKey(root, @"Software\Classes\*\shell\ExtractX.Compress");
            TryDeleteKey(root, @"Software\Classes\Directory\shell\ExtractX.Compress");
            TryDeleteKey(root, @"Software\Classes\" + ProgId);
            TryDeleteKey(root, @"Software\ExtractX");
            try { root.OpenSubKey(@"Software\RegisteredApplications", true)?.DeleteValue(AppName, false); } catch { }
            TryDeleteKey(root, @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName);
            NotifyShell();
        }
        catch { }

        Report(progress, 70, "Cerrando ExtractX...");
        try
        {
            foreach (var p in Process.GetProcessesByName("ExtractX")) { try { p.Kill(); } catch { } }
        }
        catch { }

        if (dir != null)
        {
            Report(progress, 85, "Borrando archivos...");
            var self = Environment.ProcessPath;
            bool selfInside = !string.IsNullOrEmpty(self) &&
                dir.Equals(Path.GetDirectoryName(self), StringComparison.OrdinalIgnoreCase);
            // Borra todo menos el ejecutable en curso
            foreach (var f in SafeEnumFiles(dir))
            {
                if (selfInside && f.Equals(self, StringComparison.OrdinalIgnoreCase)) continue;
                for (int i = 0; i < 4; i++)
                {
                    try { File.Delete(f); break; }
                    catch { await Task.Delay(400, ct); }
                }
            }
            foreach (var d in SafeEnumDirs(dir))
            {
                try { Directory.Delete(d, true); } catch { }
            }
            if (selfInside)
            {
                // El desinstalador no puede borrarse a sí mismo: lo hace un cmd tras salir
                try
                {
                    Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{dir}\"")
                    { CreateNoWindow = true, UseShellExecute = false });
                }
                catch { }
            }
            else
            {
                for (int i = 0; i < 6 && Directory.Exists(dir); i++)
                {
                    try { Directory.Delete(dir, true); break; }
                    catch { await Task.Delay(600, ct); }
                }
            }
        }
        Report(progress, 100, "Desinstalación completa.");
    }

    // ---------------- registro ----------------

    private static void ApplyRegistry(InstallOptions o)
    {
        var root = Hive(o.AllUsers);
        var exe = Path.Combine(o.Directory, AppExeName);

        using (var k = root.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            k.SetValue("", "Archivo comprimido ExtractX");
            k.SetValue("FriendlyAppName", AppName);
        }
        using (var k = root.CreateSubKey($@"Software\Classes\{ProgId}\DefaultIcon"))
            k.SetValue("", $"\"{exe}\",0");
        using (var k = root.CreateSubKey($@"Software\Classes\{ProgId}\shell\open\command"))
            k.SetValue("", $"\"{exe}\" \"%1\"");

        // Capabilities para que salga en "Aplicaciones predeterminadas" de Windows
        using (var cap = root.CreateSubKey(@"Software\ExtractX\Capabilities"))
        {
            cap.SetValue("ApplicationName", AppName);
            cap.SetValue("ApplicationDescription", "Descompresor moderno: ZIP, RAR, 7Z, TAR, GZ, ISO");
            using var fa = cap.CreateSubKey("FileAssociations");
            foreach (var ext in AllFormats) fa.SetValue(ext, ProgId);
        }
        using (var ra = root.CreateSubKey(@"Software\RegisteredApplications"))
            ra.SetValue(AppName, @"Software\ExtractX\Capabilities");

        // Toma de formatos (guarda el dueño anterior para restaurar al desinstalar)
        try
        {
            using var inst = root.CreateSubKey(@"Software\ExtractX\Installed");
            inst.SetValue("Formats", string.Join(",", o.Formats));
            inst.SetValue("Dir", o.Directory);
        }
        catch { }
        foreach (var ext in o.Formats)
        {
            try
            {
                using var k = root.CreateSubKey(@"Software\Classes\" + ext);
                var prev = k.GetValue("")?.ToString();
                if (!string.IsNullOrEmpty(prev) && prev != ProgId)
                    root.CreateSubKey(@"Software\ExtractX\Backup")?.SetValue(ext, prev);
                k.SetValue("", ProgId);

                using var m = root.CreateSubKey($@"Software\Classes\SystemFileAssociations\{ext}\shell\ExtractX.Extract");
                m.SetValue("", "Extraer con ExtractX");
                m.SetValue("Icon", $"\"{exe}\",0");
                using var c = root.CreateSubKey($@"Software\Classes\SystemFileAssociations\{ext}\shell\ExtractX.Extract\command");
                c.SetValue("", $"\"{exe}\" --extract \"%1\"");
                using var m2 = root.CreateSubKey($@"Software\Classes\SystemFileAssociations\{ext}\shell\ExtractX.Open");
                m2.SetValue("", "Abrir con ExtractX");
                m2.SetValue("Icon", $"\"{exe}\",0");
                using var c2 = root.CreateSubKey($@"Software\Classes\SystemFileAssociations\{ext}\shell\ExtractX.Open\command");
                c2.SetValue("", $"\"{exe}\" \"%1\"");
            }
            catch { }
        }

        if (o.Formats.Count > 0)
        {
            foreach (var cls in new[] { @"Software\Classes\*\shell\ExtractX.Compress", @"Software\Classes\Directory\shell\ExtractX.Compress" })
            {
                try
                {
                    using var m = root.CreateSubKey(cls);
                    m.SetValue("", "Comprimir con ExtractX");
                    m.SetValue("Icon", $"\"{exe}\",0");
                    using var c = root.CreateSubKey(cls + @"\command");
                    c.SetValue("", $"\"{exe}\" --compress \"%1\"");
                }
                catch { }
            }
        }
        NotifyShell();
    }

    private static void WriteUninstallEntry(InstallOptions o)
    {
        try
        {
            var root = Hive(o.AllUsers);
            using var k = root.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName);
            var uninst = Path.Combine(o.Directory, "Uninstall.exe");
            if (!File.Exists(uninst)) uninst = Environment.ProcessPath!;
            k.SetValue("DisplayName", AppName);
            k.SetValue("DisplayVersion", Version);
            k.SetValue("Publisher", AppName);
            k.SetValue("InstallLocation", o.Directory);
            k.SetValue("DisplayIcon", Path.Combine(o.Directory, AppExeName));
            k.SetValue("UninstallString", $"\"{uninst}\" --uninstall");
            k.SetValue("NoModify", 1);
            k.SetValue("NoRepair", 1);
        }
        catch { }
    }

    // ---------------- utilidades ----------------

    /// <summary>
    /// Payload garantizado: si no está junto al setup (p. ej. el auto-updater solo
    /// descarga el setup a TEMP), se baja de GitHub Releases con progreso.
    /// </summary>
    public static async Task<string> EnsurePayloadAsync(IProgress<(int pct, string msg)>? progress, CancellationToken ct)
    {
        var local = FindPayload();
        if (local != null) return local;
        Report(progress, 1, "Setup solo: descargando programa completo (~130 MB)...");
        string url = await FindPayloadUrlAsync(ct)
            ?? throw new FileNotFoundException(
                "No se encontró el programa junto al instalador ni se pudo descargar. " +
                "Revisa tu conexión o descarga la carpeta dist/ completa.");
        string dir = Path.Combine(Path.GetTempPath(), "ExtractX_payload");
        Directory.CreateDirectory(dir);
        string dest = Path.Combine(dir, PayloadName);
        if (!File.Exists(dest) || new FileInfo(dest).Length < 50_000_000)
            await DownloadFileAsync(url, dest,
                progress == null ? null : new Progress<(int pct, string msg)>(t =>
                    progress.Report((1 + t.pct * 4 / 10, $"Descargando programa... {t.pct:0}%"))), ct);
        if (!File.Exists(dest)) throw new FileNotFoundException("Descarga incompleta del programa.");
        return dest;
    }

    private static async Task<string?> FindPayloadUrlAsync(CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ExtractX-Setup/1.0");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var resp = await http.GetAsync(
                "https://api.github.com/repos/bddjf00-cell/ExtractX/releases/latest", ct);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(
                await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("assets", out var assets)) return null;
            foreach (var a in assets.EnumerateArray())
            {
                string name = a.GetProperty("name").GetString() ?? "";
                if (name.StartsWith("ExtractX-v", StringComparison.OrdinalIgnoreCase)
                    && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                    return a.GetProperty("browser_download_url").GetString();
            }
            return null;
        }
        catch { return null; }
    }

    private static async Task DownloadFileAsync(string url, string dest,
        IProgress<(int pct, string msg)>? progress, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ExtractX-Setup/1.0");
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? 0;
        using var net = await resp.Content.ReadAsStreamAsync(ct);
        using var file = File.Create(dest);
        var buf = new byte[1024 * 256];
        long done = 0;
        int read;
        while ((read = await net.ReadAsync(buf, ct)) > 0)
        {
            await file.WriteAsync(buf.AsMemory(0, read), ct);
            done += read;
            if (total > 0) progress?.Report(((int)(done * 100 / total), "descargando"));
        }
    }

    private static void CopyDir(string src, string dst)
    {
        try
        {
            if (!Directory.Exists(src)) return;
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        }
        catch { }
    }

    private static async Task CopyWithProgressAsync(string src, string dst, int p0, int p1, IProgress<(int, string)>? progress, CancellationToken ct)
    {
        const int buf = 1024 * 1024;
        using var fin = File.OpenRead(src);
        using var fout = File.Create(dst);
        var buffer = new byte[buf];
        long total = fin.Length, done = 0;
        int read;
        while ((read = await fin.ReadAsync(buffer, ct)) > 0)
        {
            await fout.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            Report(progress, p0 + (int)((p1 - p0) * done / total), $"Copiando archivos... {done * 100 / total}%");
        }
    }

    private static void CreateShortcut(string lnk, string target, string args, string desc)
    {
        try
        {
            var t = Type.GetTypeFromProgID("WScript.Shell")!;
            var shell = Activator.CreateInstance(t)!;
            var sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk })!;
            var st = sc.GetType();
            st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
            st.InvokeMember("Arguments", BindingFlags.SetProperty, null, sc, new object[] { args });
            st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { desc });
            st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(target)! });
            st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
        }
        catch { }
    }

    private static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }
    private static IEnumerable<string> SafeEnumFiles(string d)
    { try { return Directory.GetFiles(d); } catch { return Enumerable.Empty<string>(); } }
    private static IEnumerable<string> SafeEnumDirs(string d)
    { try { return Directory.GetDirectories(d); } catch { return Enumerable.Empty<string>(); } }
    private static void TryDeleteDir(string d) { try { if (Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch { } }
    private static void TryDeleteKey(RegistryKey root, string sub) { try { root.DeleteSubKeyTree(sub, false); } catch { } }

    private static void Report(IProgress<(int pct, string msg)>? p, int pct, string msg) => p?.Report((pct, msg));

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);
    private static void NotifyShell()
    {
        try { SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero); } catch { }
    }

    public static void OpenDefaultApps()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true }); } catch { }
    }

    // ---------------- requisito .NET ----------------

    /// <summary>¿Hay .NET Desktop Runtime 9+ (x64 o x86)? El setup es ligero y lo necesita.</summary>
    public static bool HasDesktopRuntime(int major = 9)
    {
        try
        {
            foreach (var pf in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            })
            {
                var baseDir = Path.Combine(pf, "dotnet", "shared", "Microsoft.WindowsDesktop.App");
                if (!Directory.Exists(baseDir)) continue;
                foreach (var d in Directory.GetDirectories(baseDir))
                {
                    var first = Path.GetFileName(d).Split('.')[0];
                    if (int.TryParse(first, out int mj) && mj >= major) return true;
                }
            }
        }
        catch { }
        return false;
    }

    /// <summary>Runtime junto al instalador (dist/) para equipos sin internet.</summary>
    public static string? FindRuntimeInstaller()
    {
        try
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath!)!;
            foreach (var f in Directory.GetFiles(dir, "windowsdesktop-runtime-*.exe"))
                return f;
        }
        catch { }
        return null;
    }

    public static async Task EnsureRuntimeAsync(IProgress<(int pct, string msg)>? progress, CancellationToken ct)
    {
        if (HasDesktopRuntime()) return;
        Report(progress, 1, "Falta .NET Desktop Runtime: instalando requisito...");
        string installer = FindRuntimeInstaller() ?? await DownloadRuntimeAsync(progress, ct);
        Report(progress, 2, "Instalando .NET Desktop Runtime (puede pedir permiso)...");
        using var p = Process.Start(new ProcessStartInfo(installer, "/install /quiet /norestart")
            { UseShellExecute = false, CreateNoWindow = true })
            ?? throw new Exception("No se pudo lanzar el instalador de .NET.");
        await p.WaitForExitAsync(ct);
        if (!HasDesktopRuntime()) throw new Exception("No se pudo instalar .NET Desktop Runtime 9.");
        Report(progress, 3, "Requisito .NET instalado.");
    }

    private static async Task<string> DownloadRuntimeAsync(IProgress<(int pct, string msg)>? progress, CancellationToken ct)
    {
        const string url = "https://aka.ms/dotnet/9.0/windowsdesktop-runtime-win-x64.exe";
        var dest = Path.Combine(Path.GetTempPath(), "windowsdesktop-runtime-9.0-win-x64.exe");
        if (File.Exists(dest) && new FileInfo(dest).Length > 10_000_000) return dest;
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? 0;
        using var net = await resp.Content.ReadAsStreamAsync(ct);
        using var file = File.Create(dest);
        var buf = new byte[1024 * 128];
        long done = 0;
        int read;
        while ((read = await net.ReadAsync(buf, ct)) > 0)
        {
            await file.WriteAsync(buf.AsMemory(0, read), ct);
            done += read;
            if (total > 0) Report(progress, 1, $"Descargando .NET Runtime... {done * 100 / total}%");
        }
        return dest;
    }
}
