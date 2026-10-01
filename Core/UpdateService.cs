using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace ExtractX.Core;

/// <summary>
/// Auto-actualización vía GitHub Releases:
/// check → descarga instalador → instala silencioso encima → listo.
/// Plan: canal "latest", 1 chequeo al arrancar (si está activado) + botón manual.
/// </summary>
public static class UpdateService
{
    public const string DefaultOwner = "bddjf00-cell";
    public const string DefaultRepo = "ExtractX";
    public const string CurrentVersion = "1.1.0";

    public sealed record UpdateInfo(string Tag, string Notes, string DownloadUrl, string FileName, bool Incremental);

    public static string ResolveRepo(string? configured) =>
        string.IsNullOrWhiteSpace(configured) ? $"{DefaultOwner}/{DefaultRepo}" : configured.Trim().Trim('/');

    // El token NUNCA va en el código: vive en %AppData%\ExtractX\github.token
    // o en la variable EXTRACTX_GITHUB_TOKEN. Sin token también funciona en repos públicos.
    private static string TokenPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ExtractX", "github.token");

    public static string? LoadToken()
    {
        try
        {
            var env = Environment.GetEnvironmentVariable("EXTRACTX_GITHUB_TOKEN");
            if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
            if (!File.Exists(TokenPath)) return null;
            string b64 = File.ReadAllText(TokenPath).Trim();
            return string.IsNullOrEmpty(b64) ? null
                : System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64)).Trim();
        }
        catch { return null; }
    }

    public static void SaveToken(string? token)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TokenPath)!);
            if (string.IsNullOrWhiteSpace(token)) { if (File.Exists(TokenPath)) File.Delete(TokenPath); return; }
            File.WriteAllText(TokenPath, Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(token.Trim())));
        }
        catch { }
    }

    public static async Task<UpdateInfo?> CheckAsync(string? repo = null, string? token = null, CancellationToken ct = default)
    {
        try
        {
            string full = ResolveRepo(repo);
            if (!full.Contains('/')) return null;
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ExtractX-Updater/1.0");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            if (!string.IsNullOrWhiteSpace(token))
                http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Trim());
            http.Timeout = TimeSpan.FromSeconds(20);
            using var resp = await http.GetAsync($"https://api.github.com/repos/{full}/releases/latest", ct);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = (root.GetProperty("tag_name").GetString() ?? "").TrimStart('v', 'V');
            if (string.IsNullOrEmpty(tag) || !IsNewer(tag, CurrentVersion)) return null;
            var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            string url = "", name = "";
            bool incremental = false;
            if (root.TryGetProperty("assets", out var assets))
            {
                var list = assets.EnumerateArray().Select(a => (
                    Name: a.GetProperty("name").GetString() ?? "",
                    Url: a.GetProperty("browser_download_url").GetString() ?? "")).ToList();
                (name, url) = PickAsset(list);
                incremental = name.Contains("patch", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("incremental", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("delta", StringComparison.OrdinalIgnoreCase);
            }
            if (string.IsNullOrEmpty(url)) return null;
            return new UpdateInfo(tag, notes, url, name, incremental);
        }
        catch { return null; }
    }

    private static (string Name, string Url) PickAsset(List<(string Name, string Url)> list)
    {
        (string Name, string Url)? Find(Func<(string Name, string Url), bool> pred)
        {
            foreach (var a in list) if (pred(a)) return a;
            return null;
        }
        bool Has(string s, (string Name, string Url) a) => a.Name.Contains(s, StringComparison.OrdinalIgnoreCase);
        return Find(a => Has("patch", a)) ?? Find(a => Has("incremental", a)) ?? Find(a => Has("delta", a))
            ?? Find(a => a.Name.EndsWith("Setup.exe", StringComparison.OrdinalIgnoreCase))
            ?? Find(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            ?? Find(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault();
    }

    public static bool IsNewer(string remote, string local)
    {
        static int[] Parts(string v) => v.Trim().TrimStart('v', 'V').Split('.')
            .Select(p => int.TryParse(new string(p.TakeWhile(char.IsDigit).ToArray()), out int n) ? n : 0)
            .ToArray();
        var r = Parts(remote); var l = Parts(local);
        for (int i = 0; i < Math.Max(r.Length, l.Length); i++)
        {
            int a = i < r.Length ? r[i] : 0, b = i < l.Length ? l[i] : 0;
            if (a != b) return a > b;
        }
        return false;
    }

    /// <summary>Descarga el setup nuevo y lo lanza en silencioso encima de la instalación actual.</summary>
    public static async Task ApplyAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct)
    {
        var tmp = Path.Combine(Path.GetTempPath(), info.FileName);
        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) })
        using (var resp = await http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? 0;
            using var net = await resp.Content.ReadAsStreamAsync(ct);
            using var file = File.Create(tmp);
            var buf = new byte[1024 * 128];
            long done = 0; int read;
            while ((read = await net.ReadAsync(buf, ct)) > 0)
            {
                await file.WriteAsync(buf.AsMemory(0, read), ct);
                done += read;
                if (total > 0) progress?.Report(done * 100.0 / total);
            }
        }
        // Actualiza encima: mismo directorio y ámbito que la instalación actual
        var (dir, allUsers) = FindInstall();
        string args = "--silent --formats=zip,rar,7z";
        if (dir != null) args += $" --dir=\"{dir}\"";
        if (allUsers) args += " --all-users";
        Process.Start(new ProcessStartInfo(tmp, args) { UseShellExecute = true });
    }

    private static (string? dir, bool allUsers) FindInstall()
    {
        foreach (var (hive, all) in new[] { (Microsoft.Win32.Registry.LocalMachine, true), (Microsoft.Win32.Registry.CurrentUser, false) })
        {
            try
            {
                using var k = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ExtractX");
                var d = k?.GetValue("InstallLocation")?.ToString();
                if (!string.IsNullOrEmpty(d) && Directory.Exists(d)) return (d, all);
            }
            catch { }
        }
        return (null, false);
    }
}
