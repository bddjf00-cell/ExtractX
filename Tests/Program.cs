using System.Diagnostics;
using System.IO;
using ExtractX.Core;

namespace ExtractX.Tests;

// Batería real: crea RARs con WinRAR de verdad (incluido sólido y cifrado,
// el caso que fallaba) y verifica roundtrips ZIP/7Z/TAR/GZ + updates.
static class Program
{
    static int _fail;
    static readonly string Work = Path.Combine(Path.GetTempPath(), "ExtractX_Tests_" + Guid.NewGuid().ToString("N")[..8]);
    static readonly string Data = Path.Combine(Work, "data");
    static string RarExe = @"C:\Program Files\WinRAR\Rar.exe";

    static async Task<int> Main()
    {
        Console.WriteLine("Work: " + Work);
        Directory.CreateDirectory(Data);
        File.WriteAllText(Path.Combine(Data, "hola.txt"), "Hola ExtractX " + new string('x', 5000));
        File.WriteAllText(Path.Combine(Data, "notas.md"), "# título\n- a\n- b\n");
        Directory.CreateDirectory(Path.Combine(Data, "sub"));
        var rnd = new Random(7);
        var bin = new byte[512 * 1024]; rnd.NextBytes(bin);
        File.WriteAllBytes(Path.Combine(Data, "sub", "bin.dat"), bin);
        // Fichero muy compresible para comparar niveles
        File.WriteAllText(Path.Combine(Data, "repetido.txt"), string.Concat(Enumerable.Repeat("abcdefgh", 20000)));

        Stage("WinRAR disponible", () => File.Exists(RarExe));

        // ---- RARs reales ----
        string rar5 = Rar("test_r5.rar", "-ma5");
        string rarSolid = Rar("test_solid.rar", "-ma5 -s");
        string rarDef = Rar("test_def.rar", "");
        string rarEnc = Rar("test_enc.rar", "-ma5 -pSecr3t");

        await StageAsync("Listar RAR5", async () =>
            (await Task.Run(() => ArchiveService.ListEntries(rar5))).Count >= 4);
        await StageAsync("Detectar contraseña RAR", async () =>
            await Task.Run(() => ArchiveService.NeedsPassword(rarEnc) && !ArchiveService.NeedsPassword(rar5)));
        await StageAsync("Extraer RAR5", () => RoundtripExtract(rar5, null));
        await StageAsync("Extraer RAR sólido", () => RoundtripExtract(rarSolid, null));
        await StageAsync("Extraer RAR formato defecto", () => RoundtripExtract(rarDef, null));
        await StageAsync("Extraer RAR cifrado (pwd ok)", () => RoundtripExtract(rarEnc, "Secr3t"));
        await StageAsync("Extraer RAR cifrado (pwd mal) falla", async () =>
        {
            try { await RoundtripExtract(rarEnc, "mala"); return false; }
            catch { return true; }
        });
        await StageAsync("DEBUG claves RAR", async () =>
        {
            var arch = await Task.Run(() => ArchiveService.ListEntries(rar5));
            Console.WriteLine("  ARCH: " + string.Join(" | ", arch.Select(e => e.Name)));
            using var s = File.OpenRead(rar5);
            using var r = SharpCompress.Readers.ReaderFactory.Open(s);
            var keys = new List<string>();
            while (r.MoveToNextEntry()) keys.Add((r.Entry.Key ?? "<null>") + " [" + r.Entry.Size + "]");
            Console.WriteLine("  READER: " + string.Join(" | ", keys));
            string dest = FreshDir();
            await ArchiveService.ExtractAsync(rar5, dest, null, null, CancellationToken.None);
            Console.WriteLine("  DEST: " + string.Join(" | ",
                Directory.GetFiles(dest, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(dest, f))));
            return true;
        });
        Stage("Verify RAR5 ok", () => ArchiveService.Verify(rar5));

        string rarOut = Path.Combine(Work, "creado.rar");
        await ArchiveService.CompressAsync(new[] { Data }, rarOut, "RAR", null, null, CancellationToken.None, CompressLevel5.Normal, solid: true);
        await StageAsync("RAR creado (sólido) roundtrip", () => RoundtripExtract(rarOut, null));
        string rarOutEnc = Path.Combine(Work, "creado_enc.rar");
        await ArchiveService.CompressAsync(new[] { Data }, rarOutEnc, "RAR", "Clave123", null, CancellationToken.None);
        await StageAsync("RAR creado +pwd roundtrip", () => RoundtripExtract(rarOutEnc, "Clave123"));
        Stage("RAR sin WinRAR avisa", () => RarEngine.Available);

        // ---- ZIP nativo ----
        string zip = Path.Combine(Work, "t.zip");
        await ArchiveService.CompressAsync(new[] { Data }, zip, "ZIP", null, null, CancellationToken.None, CompressLevel5.Normal);
        await StageAsync("ZIP roundtrip", () => RoundtripExtract(zip, null));

        string zipEnc = Path.Combine(Work, "t_enc.zip");
        await ArchiveService.CompressAsync(new[] { Data }, zipEnc, "ZIP", "Clave123", null, CancellationToken.None, CompressLevel5.Normal);
        await StageAsync("ZIP+pwd roundtrip", () => RoundtripExtract(zipEnc, "Clave123"));

        // ---- TAR / TGZ / GZ ----
        string tar = Path.Combine(Work, "t.tar");
        await ArchiveService.CompressAsync(new[] { Data }, tar, "TAR", null, null, CancellationToken.None);
        await StageAsync("TAR roundtrip", () => RoundtripExtract(tar, null));

        string tgz = Path.Combine(Work, "t.tar.gz");
        await ArchiveService.CompressAsync(new[] { Data }, tgz, "TAR.GZ", null, null, CancellationToken.None);
        await StageAsync("TAR.GZ roundtrip", () => RoundtripExtract(tgz, null));
        Stage("Detecta TAR.GZ", () => ArchiveService.DetectType(tgz) == "Archivo TAR.GZ");

        string gz = Path.Combine(Work, "solo.txt.gz");
        await ArchiveService.CompressAsync(new[] { Path.Combine(Data, "hola.txt") }, gz, "GZ", null, null, CancellationToken.None);
        await StageAsync("GZ roundtrip", async () =>
        {
            string dest = FreshDir();
            await ArchiveService.ExtractAsync(gz, dest, null, null, CancellationToken.None);
            string inner = Path.Combine(dest, "solo.txt");
            return File.Exists(inner) && File.ReadAllText(inner) == File.ReadAllText(Path.Combine(Data, "hola.txt"));
        });

        // ---- Niveles ----
        string zStored = Path.Combine(Work, "s.zip"), zMax = Path.Combine(Work, "m.zip");
        var rep = new[] { Path.Combine(Data, "repetido.txt") };
        await ArchiveService.CompressAsync(rep, zStored, "ZIP", null, null, CancellationToken.None, CompressLevel5.Stored);
        await ArchiveService.CompressAsync(rep, zMax, "ZIP", null, null, CancellationToken.None, CompressLevel5.Maximum);
        Stage("Nivel Stored > Maximum", () =>
            new FileInfo(zStored).Length > new FileInfo(zMax).Length * 2);

        // ---- Mapeos ----
        Stage("DefaultExtension", () =>
            ArchiveService.DefaultExtension("7Z") == ".7z" &&
            ArchiveService.DefaultExtension("TAR.GZ") == ".tar.gz" &&
            ArchiveService.DefaultExtension("GZ") == ".gz" &&
            ArchiveService.DefaultExtension("raro") == ".zip");
        Stage("ParseLevel", () =>
            ArchiveService.ParseLevel("Máxima") == CompressLevel5.Maximum &&
            ArchiveService.ParseLevel("Sin compresión") == CompressLevel5.Stored &&
            ArchiveService.ParseLevel(null) == CompressLevel5.Normal);

        // ---- Motor 7-Zip ----
        EnsureZaLocal();
        Stage("7za localizable", () => SevenZip.Available);
        string seven = Path.Combine(Work, "t.7z");
        await ArchiveService.CompressAsync(new[] { Data }, seven, "7Z", null, null, CancellationToken.None, CompressLevel5.Normal);
        await StageAsync("7Z roundtrip (motor 7za)", () => RoundtripExtract(seven, null));
        string sevenEnc = Path.Combine(Work, "t_enc.7z");
        await ArchiveService.CompressAsync(new[] { Data }, sevenEnc, "7Z", "Clave123", null, CancellationToken.None);
        await StageAsync("7Z+pwd roundtrip", () => RoundtripExtract(sevenEnc, "Clave123"));
        string zipAes = Path.Combine(Work, "t_aes.zip");
        await ArchiveService.CompressAsync(new[] { Data }, zipAes, "ZIP", "Clave123", null, CancellationToken.None);
        await StageAsync("ZIP+pwd vía 7za roundtrip", () => RoundtripExtract(zipAes, "Clave123"));

        // ---- Updates (sin red obligatoria) ----
        Stage("IsNewer", () =>
            UpdateService.IsNewer("1.2.0", "1.1.0") && !UpdateService.IsNewer("1.1.0", "1.1.0")
            && !UpdateService.IsNewer("1.0.9", "1.1.0") && UpdateService.IsNewer("v2.0", "1.1.0"));
        await StageAsync("CheckAsync no revienta sin repo", async () =>
            await UpdateService.CheckAsync("usuario-que-no-existe-xyz/repo-xyz", null) == null);

        // ---- Motor 7z completo (rescate RAR/ISO): descarga MSI oficial una vez ----
        await StageAsync("Motor 7z completo (descarga)", async () =>
        {
            string exe = await SevenZip.EnsureFullAsync(null, CancellationToken.None);
            return File.Exists(exe) && File.Exists(Path.Combine(Path.GetDirectoryName(exe)!, "7z.dll"));
        });
        await StageAsync("Extraer RAR vía 7z completo", async () =>
        {
            string dest = FreshDir();
            await SevenZip.ExtractAsync(rarSolid, dest, null, null, CancellationToken.None);
            return Directory.GetFiles(dest, "*", SearchOption.AllDirectories).Length >= 4;
        });

        Console.WriteLine(_fail == 0 ? "\nTODO OK" : $"\nFALLOS: {_fail}");
        try { Directory.Delete(Work, true); } catch { }
        return _fail;
    }

    static string Rar(string name, string switches)
    {
        string outRar = Path.Combine(Work, name);
        // Rutas relativas (WorkingDirectory=Work): como los crea un usuario normal.
        var psi = new ProcessStartInfo(RarExe, $"a -r {switches} \"{name}\" \"data\"")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            WorkingDirectory = Work
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit(120000);
        if (!File.Exists(outRar)) throw new Exception("Rar.exe no creó " + name);
        Console.WriteLine($"  rar: {name} ({new FileInfo(outRar).Length / 1024} KB)");
        return outRar;
    }

    static async Task<bool> RoundtripExtract(string archive, string? pwd)
    {
        string dest = FreshDir();
        await ArchiveService.ExtractAsync(archive, dest, pwd, null, CancellationToken.None);
        foreach (var src in Directory.GetFiles(Data, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(Data, src);
            // Rar.exe con "data\*" guarda rutas relativas a data\
            var cands = new[] { Path.Combine(dest, rel), Path.Combine(dest, "data", rel) };
            string? hit = cands.FirstOrDefault(File.Exists);
            if (hit == null) { Console.WriteLine("  falta: " + rel); return false; }
            if (!File.ReadAllBytes(src).SequenceEqual(File.ReadAllBytes(hit))) { Console.WriteLine("  difiere: " + rel); return false; }
        }
        return true;
    }

    static string FreshDir()
    {
        string d = Path.Combine(Work, "out_" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(d);
        return d;
    }

    static void EnsureZaLocal()
    {
        try
        {
            string repo = RepoRoot();
            string src = Path.Combine(repo, "redist", "7za.exe");
            string dstDir = AppContext.BaseDirectory;
            if (File.Exists(src) && !File.Exists(Path.Combine(dstDir, "7za.exe")))
                File.Copy(src, Path.Combine(dstDir, "7za.exe"));
        }
        catch (Exception ex) { Console.WriteLine("  aviso 7za: " + ex.Message); }
    }

    static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && d != null; i++, d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "ExtractX.csproj"))) return d.FullName;
        return AppContext.BaseDirectory;
    }

    static void Stage(string name, Func<bool> fn)
    {
        try
        {
            bool ok = fn();
            Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
            if (!ok) _fail++;
        }
        catch (Exception ex) { Console.WriteLine("FAIL " + name + " :: " + ex.Message); _fail++; }
    }

    static async Task StageAsync(string name, Func<Task<bool>> fn)
    {
        try
        {
            bool ok = await fn();
            Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
            if (!ok) _fail++;
        }
        catch (Exception ex) { Console.WriteLine("FAIL " + name + " :: " + ex.GetBaseException().Message); _fail++; }
    }
}
