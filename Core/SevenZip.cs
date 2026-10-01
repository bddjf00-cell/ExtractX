using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ExtractX.Core;

/// <summary>
/// Segundo motor de extracción/compresión:
/// - 7za.exe (redist/, instantáneo): ZIP, 7Z, TAR, GZ, BZ2, XZ, CAB.
/// - 7z.exe+7z.dll completos (descarga MSI oficial una vez): además RAR, ISO, WIM...
/// Sin 7za no hay compresión con contraseña; sin el completo no hay RAR/ISO.
/// 7-Zip © Igor Pavlov (LGPL). Se invoca como proceso externo.
/// </summary>
public static class SevenZip
{
    private static readonly string[] ZaReadable =
        { ".zip", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".tbz", ".xz", ".txz", ".cab", ".001", ".lzma" };

    public static string BinDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExtractX", "bin");

    // ---------------- localización ----------------

    public static string? FindBundled()
    {
        var exeDir = AppContext.BaseDirectory;
        foreach (var c in new[]
        {
            Path.Combine(exeDir, "redist", "7za.exe"),
            Path.Combine(exeDir, "7za.exe"),
            Path.Combine(BinDir, "7za.exe"),
        })
            if (File.Exists(c)) return c;
        try
        {
            var psi = new ProcessStartInfo("where", "7za.exe")
                { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi)!;
            string first = p.StandardOutput.ReadLine() ?? "";
            p.WaitForExit(4000);
            if (File.Exists(first.Trim())) return first.Trim();
        }
        catch { }
        return null;
    }

    public static bool Available => FindBundled() != null;

    public static string? FindFull()
    {
        foreach (var c in new[]
        {
            Path.Combine(BinDir, "7z.exe"),
            Path.Combine(AppContext.BaseDirectory, "7z.exe"),
            Path.Combine(AppContext.BaseDirectory, "redist", "7z.exe"),
        })
            if (File.Exists(c) && File.Exists(Path.Combine(Path.GetDirectoryName(c)!, "7z.dll"))) return c;
        try
        {
            foreach (var sub in new[] { @"SOFTWARE\7-Zip", @"SOFTWARE\WOW6432Node\7-Zip" })
            {
                using var k = Registry.LocalMachine.OpenSubKey(sub);
                var p = k?.GetValue("Path")?.ToString();
                if (!string.IsNullOrEmpty(p) && File.Exists(Path.Combine(p, "7z.exe"))) return Path.Combine(p, "7z.exe");
            }
        }
        catch { }
        try
        {
            var psi = new ProcessStartInfo("where", "7z.exe")
                { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi)!;
            string first = p.StandardOutput.ReadLine() ?? "";
            p.WaitForExit(4000);
            if (File.Exists(first.Trim())) return first.Trim();
        }
        catch { }
        return null;
    }

    public static bool FullAvailable => FindFull() != null;

    /// <summary>Versión del motor (FileMajorPart de 7z.exe). 0 si ilegible.</summary>
    public static int EngineMajor(string exe)
    {
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(exe);
            if (vi.FileMajorPart > 0) return vi.FileMajorPart;
        }
        catch { }
        return 0;
    }

    /// <summary>¿Lo abre el 7za ligero? RAR/ISO/WIM necesitan el motor completo.</summary>
    public static bool CanRead(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (path.ToLowerInvariant().EndsWith(".tar.gz")) return true;
        return ZaReadable.Contains(ext);
    }

    // ---------------- descarga del completo ----------------

    public static async Task<string> EnsureFullAsync(IProgress<(int pct, string msg)>? progress = null, CancellationToken ct = default)
    {
        var found = FindFull();
        if (found != null)
        {
            if (EngineMajor(found) >= 22) return found;
            // Motor obsoleto (p. ej. 9.20: no abre RAR5): se expulsa y se descarga el actual.
            Report(progress, 0, "Motor 7-Zip obsoleto: descargando versión actual...");
            try
            {
                File.Delete(found);
                File.Delete(Path.Combine(Path.GetDirectoryName(found)!, "7z.dll"));
            }
            catch { }
        }
        Report(progress, 0, "Descargando motor 7-Zip completo (una sola vez, ~2 MB)...");
        var msi = await DownloadMsiAsync(progress, ct);
        Report(progress, 70, "Extrayendo 7z.exe...");
        var tmp = Path.Combine(Path.GetTempPath(), "xz_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            using var p = Process.Start(new ProcessStartInfo("msiexec", $"/a \"{msi}\" /qb TARGETDIR=\"{tmp}\"")
                { UseShellExecute = false, CreateNoWindow = true })!;
            await p.WaitForExitAsync(ct);
            var src = Path.Combine(tmp, "Files", "7-Zip");
            Directory.CreateDirectory(BinDir);
            foreach (var f in new[] { "7z.exe", "7z.dll" })
            {
                var s = Path.Combine(src, f);
                if (!File.Exists(s)) throw new FileNotFoundException($"El paquete 7-Zip no trajo {f}.");
                File.Copy(s, Path.Combine(BinDir, f), true);
            }
            if (EngineMajor(Path.Combine(BinDir, "7z.exe")) < 22)
                throw new Exception("El paquete 7-Zip descargado no es válido.");
        }
        finally { try { Directory.Delete(tmp, true); } catch { } try { File.Delete(msi); } catch { } }
        Report(progress, 100, "Motor 7-Zip listo.");
        return Path.Combine(BinDir, "7z.exe");
    }

    private static async Task<string> DownloadMsiAsync(IProgress<(int pct, string msg)>? progress, CancellationToken ct)
    {
        string url = "https://www.7-zip.org/a/7z2409-x64.msi"; // respaldo conocido
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ExtractX/1.1");
            var html = await http.GetStringAsync("https://www.7-zip.org/download.html", ct);
            var versions = Regex.Matches(html, @"a/7z(\d+)-x64\.msi")
                .Select(x => x.Groups[1].Value)
                .Select(v => long.TryParse(v, out long n) ? n : 0)
                .Where(n => n >= 2000) // 9.20 antiguo (920) queda excluido
                .Distinct().OrderBy(n => n).ToList();
            if (versions.Count > 0) url = $"https://www.7-zip.org/a/7z{versions[^1]}-x64.msi";
        }
        catch { }
        var dest = Path.Combine(Path.GetTempPath(), "7z-x64.msi");
        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
        using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? 0;
            using var net = await resp.Content.ReadAsStreamAsync(ct);
            using var file = File.Create(dest);
            var buf = new byte[128 * 1024];
            long done = 0; int read;
            while ((read = await net.ReadAsync(buf, ct)) > 0)
            {
                await file.WriteAsync(buf.AsMemory(0, read), ct);
                done += read;
                if (total > 0) Report(progress, (int)(done * 60 / total), $"Descargando 7-Zip... {done * 100 / total}%");
            }
        }
        return dest;
    }

    // ---------------- extracción ----------------

    public static async Task ExtractAsync(string archive, string destDir, string? password,
        IProgress<(double pct, string current)>? progress, CancellationToken ct, int totalFiles = 0)
    {
        Directory.CreateDirectory(destDir);
        string exe;
        if (CanRead(archive))
        {
            exe = FindBundled() ?? await EnsureFullAsync(null, ct);
        }
        else
        {
            progress?.Report((2, "RAR/ISO: activando motor 7-Zip completo..."));
            exe = await EnsureFullAsync(progress != null
                ? new Progress<(int p, string m)>(x => progress.Report((x.p, x.m))) : null, ct);
        }
        var args = $"x -o\"{destDir}\" -y -bsp1 -bb1" + Pwd(password) + $" -- \"{archive}\"";
        int code = await RunAsync(exe, args, progress, ct);
        if (code != 0 && code != 1)
            throw new Exception($"7-Zip no pudo extraer (código {code}). ¿Contraseña incorrecta o archivo dañado?");
        progress?.Report((100, "Completado"));
    }

    // ---------------- compresión ----------------

    public static async Task CompressAsync(IEnumerable<string> sources, string outputPath, string format,
        string? password, IProgress<double>? progress, CancellationToken ct,
        CompressLevel5 level = CompressLevel5.Normal, bool solid = false)
    {
        string f = (format ?? "ZIP").ToUpperInvariant();
        string exe = FindBundled() ?? await EnsureFullAsync(null, ct);
        var files = sources.Where(s => File.Exists(s) || Directory.Exists(s)).ToList();
        if (files.Count == 0) throw new FileNotFoundException("No hay archivos para comprimir.");

        if (f == "TAR.GZ")
        {
            var tmpTar = Path.Combine(Path.GetTempPath(), "xz_" + Guid.NewGuid().ToString("N") + ".tar");
            try
            {
                await CompressOneAsync(exe, files, tmpTar, "tar", null, Scale(progress, 0, 70), ct, level, solid);
                await CompressOneAsync(exe, new[] { tmpTar }, outputPath, "gzip", password, Scale(progress, 70, 100), ct, level, solid);
            }
            finally { try { File.Delete(tmpTar); } catch { } }
            return;
        }
        await CompressOneAsync(exe, files, outputPath, f.ToLowerInvariant() switch
        {
            "7z" => "7z", "tar" => "tar", "gz" or "gzip" => "gzip", _ => "zip"
        }, password, progress, ct, level, solid);
    }

    private static async Task CompressOneAsync(string exe, IEnumerable<string> files, string outFile,
        string type, string? password, IProgress<double>? progress, CancellationToken ct,
        CompressLevel5 level = CompressLevel5.Normal, bool solid = false)
    {
        // Sin @listfile: es frágil con codificaciones. Args directos por lotes (límite línea de comandos).
        var batches = Batch(files.ToList(), 15000);
        int done = 0;
        string mx = type == "tar" ? "" : level switch
        {
            CompressLevel5.Stored => " -mx=0",
            CompressLevel5.Fast => " -mx=1",
            CompressLevel5.Maximum => " -mx=9",
            _ => " -mx=5"
        };
        string ms = (solid && type == "7z") ? " -ms=on" : "";
        foreach (var batch in batches)
        {
            ct.ThrowIfCancellationRequested();
            string pwdArgs = string.IsNullOrEmpty(password) ? ""
                : $" -p\"{password}\"" + (type == "7z" ? " -mhe=on" : type == "zip" ? " -mem=AES256" : "");
            // Primer lote crea; los siguientes añaden (-y sobrescribe/añade sin preguntar).
            var args = $"a -t{type}{mx}{ms} -y -bsp1 -bb1{pwdArgs} -- \"{outFile}\" " +
                       string.Join(" ", batch.Select(f => $"\"{f}\""));
            var prog = progress == null ? null : new Progress<(double p, string c)>(x =>
                progress.Report((done + x.p / Math.Max(1, batches.Count)) ));
            int code = await RunAsync(exe, args, prog, ct);
            if (code != 0 && code != 1) throw new Exception($"7-Zip no pudo comprimir (código {code}).");
            done += 100 / Math.Max(1, batches.Count);
        }
        progress?.Report(100);
    }

    private static List<List<string>> Batch(List<string> files, int maxChars)
    {
        var batches = new List<List<string>>();
        var cur = new List<string>(); int len = 0;
        foreach (var f in files)
        {
            if (cur.Count > 0 && len + f.Length + 3 > maxChars) { batches.Add(cur); cur = new List<string>(); len = 0; }
            cur.Add(f); len += f.Length + 3;
        }
        if (cur.Count > 0) batches.Add(cur);
        return batches;
    }

    // ---------------- listado ----------------

    public static async Task<List<EntryInfo>> ListAsync(string archive, string? password,
        IProgress<(int pct, string msg)>? progress = null, CancellationToken ct = default)
    {
        string exe = (CanRead(archive) && Available) ? FindBundled()! : await EnsureFullAsync(progress, ct);
        var args = $"l -slt" + Pwd(password) + $" -- \"{archive}\"";
        var output = await RunCaptureAsync(exe, args, ct);
        var list = new List<EntryInfo>();
        string? name = null; long size = 0, packed = 0; DateTime? mod = null; bool isDir = false;
        bool inEntries = false; // el primer bloque es la cabecera del archivo, no una entrada
        void Flush()
        {
            if (inEntries && name != null && !isDir) list.Add(new EntryInfo { Name = name, Size = size, CompressedSize = packed, Modified = mod });
            name = null; size = packed = 0; mod = null; isDir = false;
        }
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Trim() == "----------") { Flush(); inEntries = true; continue; }
            if (string.IsNullOrWhiteSpace(line)) { Flush(); continue; }
            if (!inEntries) continue;
            int eq = line.IndexOf('=');
            if (eq < 0) continue;
            var k = line[..eq].Trim(); var v = line[(eq + 1)..].Trim();
            switch (k)
            {
                case "Path": if (name != null) Flush(); name = v; break;
                case "Size": long.TryParse(v, out size); break;
                case "Packed Size": long.TryParse(v, out packed); break;
                case "Folder": if (v == "+") isDir = true; break;
                case "Attributes": if (v.Contains('D')) isDir = true; break;
                case "Modified": if (DateTime.TryParse(v, out var dt)) mod = dt; break;
            }
        }
        Flush();
        return list.OrderBy(x => x.Name).ToList();
    }

    // ---------------- verificación ----------------

    /// <summary>Segunda opinión: `7z t` comprueba integridad sin extraer.</summary>
    public static async Task<bool> TestAsync(string archive, string? password, CancellationToken ct)
    {
        try
        {
            string? exe = (CanRead(archive) && Available) ? FindBundled() : (FullAvailable ? FindFull() : null);
            if (exe == null) return false;
            int code = await RunAsync(exe, $"t" + Pwd(password) + $" -- \"{archive}\"", null, ct);
            return code == 0;
        }
        catch { return false; }
    }

    // ---------------- proceso ----------------

    private static string Pwd(string? password) => string.IsNullOrEmpty(password) ? "" : $" -p\"{password}\"";

    private static IProgress<double>? Scale(IProgress<double>? inner, double from, double to)
    {
        if (inner == null) return null;
        return new Progress<double>(v => inner.Report(from + v * (to - from) / 100.0));
    }

    private static async Task<int> RunAsync(string exe, string args,
        IProgress<(double pct, string current)>? progress, CancellationToken ct)
    {
        using var p = new Process();
        p.StartInfo = new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        p.Start();
        var pctRe = new Regex(@"(\d{1,3})%");
        using var _ = ct.Register(() => { try { p.Kill(true); } catch { } });
        string? line;
        while ((line = await p.StandardOutput.ReadLineAsync(ct)) != null)
        {
            var m = pctRe.Match(line);
            if (m.Success && double.TryParse(m.Groups[1].Value, out double v))
                progress?.Report((Math.Min(99, v), ""));
        }
        string err = await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0 && err.Contains("Wrong password", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Contraseña incorrecta.");
        return p.ExitCode;
    }

    private static async Task<string> RunCaptureAsync(string exe, string args, CancellationToken ct)
    {
        using var p = new Process();
        p.StartInfo = new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        p.Start();
        string output = await p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0) throw new Exception("7-Zip no pudo leer el archivo.");
        return output;
    }

    private static void Report(IProgress<(int pct, string msg)>? p, int pct, string msg) => p?.Report((pct, msg));
}
