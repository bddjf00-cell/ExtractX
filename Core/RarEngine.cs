using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace ExtractX.Core;

/// <summary>
/// Creación de RAR vía Rar.exe oficial (única forma legal: RAR es propietario de RARLAB).
/// RAR5, sólido opcional, AES-256 (-hp cifra también los nombres), registro de recuperación.
/// Si WinRAR no está instalado, se lanza error amable en vez de fallar raro.
/// </summary>
public static class RarEngine
{
    public static string? FindRar()
    {
        foreach (var c in new[]
        {
            @"C:\Program Files\WinRAR\Rar.exe",
            @"C:\Program Files (x86)\WinRAR\Rar.exe",
        })
            if (File.Exists(c)) return c;
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\WinRAR.exe");
            var p = k?.GetValue("")?.ToString();
            if (!string.IsNullOrEmpty(p))
            {
                string rar = Path.Combine(Path.GetDirectoryName(p)!, "Rar.exe");
                if (File.Exists(rar)) return rar;
            }
        }
        catch { }
        try
        {
            var psi = new ProcessStartInfo("where", "rar.exe")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var proc = Process.Start(psi)!;
            string first = proc.StandardOutput.ReadLine() ?? "";
            proc.WaitForExit(4000);
            if (File.Exists(first.Trim())) return first.Trim();
        }
        catch { }
        return null;
    }

    public static bool Available => FindRar() != null;

    /// <summary>Elimina entradas de un RAR (`Rar.exe d`). Solo RAR puede borrarse a sí mismo.</summary>
    public static async Task DeleteAsync(string archive, IEnumerable<string> names, string? password, CancellationToken ct)
    {
        string rar = FindRar() ?? throw new InvalidOperationException(
            "Para modificar RAR necesitas WinRAR instalado.");
        var list = names.Select(n => (n ?? "").Replace('/', '\\')).Where(n => n.Length > 0).ToList();
        if (list.Count == 0) throw new ArgumentException("No hay entradas seleccionadas.");
        string pwd = string.IsNullOrEmpty(password) ? "" : $" -p\"{password}\"";
        var args = $"d -y{pwd} -- \"{archive}\" " + string.Join(" ", list.Select(n => $"\"{n}\""));
        int code = await RunAsync(rar, Path.GetDirectoryName(Path.GetFullPath(archive))!, args, null, ct);
        if (code != 0 && code != 1)
            throw new Exception($"Rar.exe no pudo eliminar (código {code}).");
    }

    public static async Task CompressAsync(IEnumerable<string> sources, string outputPath,
        string? password, IProgress<double>? progress, CancellationToken ct,
        CompressLevel5 level = CompressLevel5.Normal, bool solid = false)
    {
        string rar = FindRar() ?? throw new InvalidOperationException(
            "Para crear RAR necesitas WinRAR instalado (RAR es un formato propietario de RARLAB). " +
            "Descárgalo gratis en rarlab.com, instálalo y vuelve a intentarlo.");
        var files = sources.Where(s => File.Exists(s) || Directory.Exists(s)).ToList();
        if (files.Count == 0) throw new FileNotFoundException("No hay archivos para comprimir.");

        string m = level switch
        {
            CompressLevel5.Stored => "-m0",
            CompressLevel5.Fast => "-m1",
            CompressLevel5.Maximum => "-m5",
            _ => "-m3"
        };
        string pwd = string.IsNullOrEmpty(password) ? "" : $" -hp\"{password}\""; // -hp: cifra datos + nombres (AES-256)
        string sol = solid ? " -s" : "";
        // -rr5p: 5% de registro de recuperación · -y: sí a todo · -ma5: formato RAR5
        string opts = $"a -r -ma5 {m}{sol} -rr5p -y{pwd}";

        var batches = Batch(files, 15000);
        int done = 0;
        string workDir = CommonParent(files);
        foreach (var batch in batches)
        {
            ct.ThrowIfCancellationRequested();
            var rel = batch.Select(f => Path.GetRelativePath(workDir, f)).ToList();
            // Si hay unidades distintas, se usan rutas absolutas tal cual.
            if (rel.Any(r => Path.IsPathRooted(r) || r.StartsWith("..")))
                rel = batch;
            var args = $"{opts} \"{outputPath}\" " + string.Join(" ", rel.Select(f => $"\"{f}\""));
            var prog = progress == null ? null : new Progress<double>(v =>
                progress.Report(done + v / Math.Max(1, batches.Count)));
            int code = await RunAsync(rar, workDir, args, prog, ct);
            if (code != 0 && code != 1)
                throw new Exception($"Rar.exe no pudo comprimir (código {code}).");
            done += 100 / Math.Max(1, batches.Count);
        }
        progress?.Report(100);
    }

    private static string CommonParent(List<string> files)
    {
        try
        {
            var dirs = files.Select(f => Directory.Exists(f) ? Path.GetFullPath(f) : Path.GetDirectoryName(Path.GetFullPath(f))!).ToList();
            string common = dirs[0];
            foreach (var d in dirs.Skip(1))
            {
                while (!d.StartsWith(common + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && !d.Equals(common, StringComparison.OrdinalIgnoreCase))
                {
                    var parent = Path.GetDirectoryName(common.TrimEnd(Path.DirectorySeparatorChar));
                    if (string.IsNullOrEmpty(parent)) break;
                    common = parent;
                }
            }
            return Directory.Exists(common) ? common : Path.GetPathRoot(common) ?? ".";
        }
        catch { return "."; }
    }

    /// <summary>Elimina entradas (`rar d`). RAR5, con o sin contraseña.</summary>
    public static async Task DeleteAsync(string archive, IEnumerable<string> names, string? password,
        IProgress<double>? progress, CancellationToken ct)
    {
        string rar = FindRar() ?? throw new InvalidOperationException(
            "Para modificar RAR necesitas WinRAR instalado.");
        var raw = names.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        if (raw.Count == 0) throw new ArgumentException("Nada que eliminar.");
        // Expande carpetas a ficheros explícitos: `d dir` no coincide (código 10)
        // y `d dir\` borra de más. También impide vaciar el archivo.
        var list = ExpandToFiles(archive, raw, password)
            .Select(n => n.Replace('/', '\\').TrimEnd('\\')).ToList();
        if (list.Count == 0) throw new FileNotFoundException("Ninguna entrada coincidió en el RAR.");
        string pwd = string.IsNullOrEmpty(password) ? "" : $" -hp\"{password}\"";
        var args = $"d -y{pwd} \"{archive}\" " + string.Join(" ", list.Select(n => $"\"{n}\""));
        int code = await RunAsync(rar, Path.GetDirectoryName(Path.GetFullPath(archive))!, args,
            progress == null ? null : new Progress<double>(v => progress.Report(v)), ct);
        if (code != 0 && code != 1 && code != 10)
            throw new Exception($"Rar.exe no pudo eliminar (código {code}).");
        if (code == 10) throw new FileNotFoundException("Ninguna entrada coincidió en el RAR.");
        progress?.Report(100);
    }

    private static List<string> ExpandToFiles(string archive, List<string> names, string? password)
    {
        List<string> all;
        try
        {
            all = ArchiveService.ListEntries(archive, password)
                .Select(e => e.Name.Replace('\\', '/')).ToList();
        }
        catch { return names.Select(n => n.Replace('/', '\\')).ToList(); }
        var files = new HashSet<string>(all, StringComparer.OrdinalIgnoreCase);
        var out_ = new List<string>();
        foreach (var n in names)
        {
            string norm = n.Replace('\\', '/');
            if (files.Contains(norm)) { out_.Add(norm.Replace('/', '\\')); continue; }
            string prefix = norm.Trim('/') + "/";
            out_.AddRange(all.Where(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(a => a.Replace('/', '\\')));
        }
        var distinct = out_.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distinct.Count > 0 && distinct.Count >= all.Count)
            throw new InvalidOperationException(
                "Eso vaciaría el archivo por completo. Extrae lo que necesites y elimina el .rar a mano.");
        return distinct;
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

    private static async Task<int> RunAsync(string exe, string workDir, string args,
        IProgress<double>? progress, CancellationToken ct)
    {
        using var p = new Process();
        p.StartInfo = new ProcessStartInfo(exe, args)
        {
            WorkingDirectory = workDir,
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
                progress?.Report(Math.Min(99, v));
        }
        await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return p.ExitCode;
    }
}
