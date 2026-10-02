using System.IO;
using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Writers;

namespace ExtractX.Core;

public enum CompressFormat { Zip, Tar, TarGz, Gz }
public enum CompressLevel5 { Stored, Fast, Normal, Maximum }

/// <summary>
/// Compresión multiformato con niveles y contraseña (ZIP).
/// Nota honesta: 7Z/RAR de salida no existen en el motor (solo lectura);
/// se ofrecen ZIP, TAR, TAR.GZ y GZ.
/// </summary>
public static class CompressService
{
    public static string ExtensionFor(CompressFormat f) => f switch
    {
        CompressFormat.Zip => ".zip",
        CompressFormat.Tar => ".tar",
        CompressFormat.TarGz => ".tar.gz",
        CompressFormat.Gz => ".gz",
        _ => ".zip"
    };

    public static string LevelName(CompressLevel5 l) => l switch
    {
        CompressLevel5.Stored => "Sin compresión",
        CompressLevel5.Fast => "Rápida",
        CompressLevel5.Normal => "Normal",
        CompressLevel5.Maximum => "Máxima",
        _ => "Normal"
    };

    public static CompressionLevel ToSystemLevel(CompressLevel5 l) => l switch
    {
        CompressLevel5.Stored => CompressionLevel.NoCompression,
        CompressLevel5.Fast => CompressionLevel.Fastest,
        CompressLevel5.Maximum => CompressionLevel.SmallestSize,
        _ => CompressionLevel.Optimal
    };

    public static async Task CompressAsync(IEnumerable<string> sources, string outputPath,
        CompressFormat format, CompressLevel5 level, string? password,
        IProgress<double>? progress, CancellationToken ct)
    {
        var src = sources.Where(s => File.Exists(s) || Directory.Exists(s)).ToList();
        if (src.Count == 0) throw new FileNotFoundException("No hay archivos para comprimir.");
        if (!string.IsNullOrEmpty(password) && format != CompressFormat.Zip)
            throw new InvalidOperationException("La contraseña solo está disponible para formato ZIP.");

        if (!outputPath.EndsWith(ExtensionFor(format), StringComparison.OrdinalIgnoreCase))
            outputPath += ExtensionFor(format);

        switch (format)
        {
            case CompressFormat.Zip when !string.IsNullOrEmpty(password):
                await CompressZipEncryptedAsync(src, outputPath, ToSystemLevel(level), password, progress, ct);
                break;
            case CompressFormat.Zip:
                await CompressZipPlainAsync(src, outputPath, ToSystemLevel(level), progress, ct);
                break;
            case CompressFormat.Tar:
                await Task.Run(() => WriteTar(src, outputPath, progress, ct), ct);
                break;
            case CompressFormat.TarGz:
                await CompressTarGzAsync(src, outputPath, progress, ct);
                break;
            case CompressFormat.Gz:
                await CompressGzAsync(src, outputPath, level, progress, ct);
                break;
        }
    }

    // ---------- ZIP sin contraseña ----------

    private static async Task CompressZipPlainAsync(List<string> src, string outZip,
        CompressionLevel level, IProgress<double>? progress, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            var files = ExpandFiles(src);
            using var zip = ZipFile.Open(outZip, ZipArchiveMode.Create);
            int i = 0;
            foreach (var (full, name) in files)
            {
                ct.ThrowIfCancellationRequested();
                zip.CreateEntryFromFile(full, name, level);
                progress?.Report(++i * 100.0 / Math.Max(1, files.Count));
            }
            // Carpetas vacías: entrada de directorio
            foreach (var d in src.Where(Directory.Exists).Cast<string>())
            {
                string rel = new DirectoryInfo(d).Name + "/";
                if (!Directory.EnumerateFileSystemEntries(d).Any())
                    zip.CreateEntry(rel);
            }
        }, ct);
    }

    // ---------- ZIP con contraseña ----------

    private static async Task CompressZipEncryptedAsync(List<string> src, string outZip,
        CompressionLevel level, string password, IProgress<double>? progress, CancellationToken ct)
    {
        var items = new List<(string, string, bool)>();
        foreach (var s in src)
        {
            if (File.Exists(s)) items.Add((Path.GetFileName(s), s, false));
            else if (Directory.Exists(s))
            {
                string root = Path.GetFullPath(s).TrimEnd(Path.DirectorySeparatorChar);
                string baseName = new DirectoryInfo(root).Name;
                bool any = false;
                foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    any = true;
                    items.Add((baseName + "/" + Path.GetRelativePath(root, f), f, false));
                }
                foreach (var d in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
                    items.Add((baseName + "/" + Path.GetRelativePath(root, d), d, true));
                if (!any) items.Add((baseName, s, true));
            }
        }
        if (items.Count == 0) throw new FileNotFoundException("No hay archivos para comprimir.");
        await ZipCryptoWriter.WriteAsync(items, outZip, level, password, progress, ct);
    }

    // ---------- TAR ----------

    private static void WriteTar(List<string> src, string outTar, IProgress<double>? progress, CancellationToken ct)
    {
        var files = ExpandFiles(src);
        using var fs = File.Create(outTar);
        using var writer = WriterFactory.Open(fs, ArchiveType.Tar, new WriterOptions(CompressionType.None));
        int i = 0;
        foreach (var (full, name) in files)
        {
            ct.ThrowIfCancellationRequested();
            using var f = File.OpenRead(full);
            writer.Write(name, f, File.GetLastWriteTime(full));
            progress?.Report(++i * 100.0 / Math.Max(1, files.Count));
        }
    }

    private static async Task CompressTarGzAsync(List<string> src, string outTgz,
        IProgress<double>? progress, CancellationToken ct)
    {
        string tmpTar = Path.Combine(Path.GetTempPath(), "ExtractX_" + Guid.NewGuid().ToString("N") + ".tar");
        try
        {
            await Task.Run(() => WriteTar(src, tmpTar,
                new Progress<double>(v => progress?.Report(v * 0.8)), ct), ct);
            using var fin = File.OpenRead(tmpTar);
            using var fout = File.Create(outTgz);
            using var gz = new GZipStream(fout, CompressionLevel.Optimal);
            var buf = new byte[1024 * 256];
            long total = fin.Length, done = 0;
            int read;
            while ((read = await fin.ReadAsync(buf, ct)) > 0)
            {
                await gz.WriteAsync(buf.AsMemory(0, read), ct);
                done += read;
                progress?.Report(80 + done * 20.0 / Math.Max(1, total));
            }
        }
        finally { try { File.Delete(tmpTar); } catch { } }
        progress?.Report(100);
    }

    private static async Task CompressGzAsync(List<string> src, string outGz,
        CompressLevel5 level, IProgress<double>? progress, CancellationToken ct)
    {
        var files = src.Where(File.Exists).ToList();
        if (files.Count == 0) throw new InvalidOperationException("GZ comprime un único archivo. Para varios usa TAR.GZ.");
        if (files.Count > 1) throw new InvalidOperationException("GZ comprime un único archivo. Para varios usa TAR.GZ.");
        if (src.Any(Directory.Exists)) throw new InvalidOperationException("GZ no comprime carpetas. Usa TAR.GZ o ZIP.");
        using var fin = File.OpenRead(files[0]);
        using var fout = File.Create(outGz);
        using var gz = new GZipStream(fout, ToSystemLevel(level));
        progress?.Report(50);
        await fin.CopyToAsync(gz, ct);
        progress?.Report(100);
    }

    // ---------- utilidades ----------

    private static List<(string Full, string Name)> ExpandFiles(List<string> src)
    {
        var files = new List<(string, string)>();
        foreach (var s in src)
        {
            if (File.Exists(s)) files.Add((s, Path.GetFileName(s)));
            else if (Directory.Exists(s))
            {
                string root = Path.GetFullPath(s).TrimEnd(Path.DirectorySeparatorChar);
                string baseName = new DirectoryInfo(root).Name;
                foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                    files.Add((f, baseName + "/" + Path.GetRelativePath(root, f)));
            }
        }
        return files;
    }
}
