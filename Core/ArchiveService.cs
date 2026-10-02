using System.IO;
using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace ExtractX.Core;

public static class ArchiveService
{
    public static readonly string[] SupportedExtensions =
        { ".zip", ".rar", ".7z", ".tar", ".gz", ".tgz", ".tar.gz", ".iso", ".cab", ".bz2", ".xz", ".lzh", ".wim" };

    public static readonly string OpenFilter =
        "Archivos comprimidos|*.zip;*.rar;*.7z;*.tar;*.gz;*.tgz;*.tar.gz;*.iso;*.cab;*.bz2;*.xz;*.wim|Todos los archivos|*.*";

    public static string FormatSize(long bytes)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:0.#} {u[i]}";
    }

    public static string DetectType(string path)
    {
        var lower = path.ToLowerInvariant();
        if (lower.EndsWith(".tar.gz") || lower.EndsWith(".tgz")) return "Archivo TAR.GZ";
        return Path.GetExtension(lower) switch
        {
            ".zip" => "Archivo ZIP",
            ".rar" => "Archivo RAR",
            ".7z" => "Archivo 7-Zip",
            ".tar" => "Archivo TAR",
            ".gz" => "Archivo GZip",
            ".iso" => "Imagen ISO",
            ".cab" => "Archivo CAB",
            ".bz2" => "Archivo BZip2",
            ".xz" => "Archivo XZ",
            ".wim" => "Imagen WIM",
            _ => "Archivo comprimido"
        };
    }

    public static bool IsTarGz(string path)
    {
        var lower = path.ToLowerInvariant();
        return lower.EndsWith(".tar.gz") || lower.EndsWith(".tgz");
    }

    private static ulong ToUlong(object? v)
    {
        try { return Convert.ToUInt64(v); } catch { return 0; }
    }

    public static string FormatForExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".7z" => "7Z",
        ".rar" => "RAR",
        ".tar" => "TAR",
        ".gz" or ".tgz" or ".tar.gz" => "TAR.GZ",
        _ => "ZIP"
    };

    /// <summary>Añade ficheros a un archivo existente (como "Añadir" de WinRAR).</summary>
    public static async Task AppendFiles(string archivePath, IEnumerable<string> files, string? password,
        IProgress<double>? progress, CancellationToken ct)
    {
        var list = files.Where(s => File.Exists(s) || Directory.Exists(s)).ToList();
        if (list.Count == 0) throw new FileNotFoundException("No hay archivos para añadir.");
        string lower = archivePath.ToLowerInvariant();
        string fmt = lower.EndsWith(".tar.gz") || lower.EndsWith(".tgz") ? "TAR.GZ"
            : FormatForExtension(Path.GetExtension(lower));
        if (fmt is "TAR.GZ" or "GZ") throw new InvalidOperationException("A ese formato no se le puede añadir (crea uno nuevo).");
        if (fmt == "RAR")
        {
            await RarEngine.CompressAsync(list, archivePath, password, progress, ct, CompressLevel5.Normal, solid: false);
            return;
        }
        if (SevenZip.Available)
        {
            await SevenZip.CompressAsync(list, archivePath, fmt, password, progress, ct);
            return;
        }
        throw new InvalidOperationException("Añadir necesita el motor 7-Zip (se descarga solo al extraer un RAR/ISO).");
    }

    /// <summary>Elimina entradas de un archivo (como "Eliminar" de WinRAR).</summary>
    public static async Task DeleteEntries(string archivePath, IEnumerable<string> names, string? password,
        IProgress<double>? progress, CancellationToken ct)
    {
        var list = names.Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Replace('\\', '/').Trim('/')).ToList();
        if (list.Count == 0) throw new ArgumentException("No hay entradas seleccionadas.");
        string lower = archivePath.ToLowerInvariant();
        if (lower.EndsWith(".rar"))
        {
            await RarEngine.DeleteAsync(archivePath, list, password, progress, ct);
            return;
        }
        await SevenZip.DeleteAsync(archivePath, list, password, progress, ct);
    }

    // ---------------- listado ----------------

    public static List<EntryInfo> ListEntries(string path, string? password = null)
    {
        if (IsTarGz(path))
        {
            string tmp = GunzipToTemp(path);
            try { return ListEntries(tmp, password); }
            finally { TryDelete(tmp); }
        }
        if (Path.GetExtension(path).ToLowerInvariant() == ".gz")
            return ListGz(path, password);

        var list = new List<EntryInfo>();
        var opts = new ReaderOptions();
        if (!string.IsNullOrEmpty(password)) opts.Password = password;
        try
        {
            using var archive = ArchiveFactory.Open(path, opts);
            foreach (var e in archive.Entries)
            {
                if (e.IsDirectory) continue;
                list.Add(new EntryInfo
                {
                    Name = string.IsNullOrWhiteSpace(e.Key) ? "(sin nombre)" : e.Key!,
                    Size = Math.Max(0, e.Size),
                    CompressedSize = Math.Max(0, e.CompressedSize),
                    Modified = e.LastModifiedTime,
                    IsDirectory = false,
                    Crc = ToUlong(e.Crc)
                });
            }
        }
        catch (Exception ex) { throw FriendlyReadError(ex); }
        return list.OrderBy(x => x.Name).ToList();
    }

    private static List<EntryInfo> ListGz(string path, string? password)
    {
        // Un .gz puede ser un fichero suelto o un .tar mal nombrado: se detecta por contenido.
        string tmp = GunzipToTemp(path);
        try
        {
            if (IsTarFile(tmp)) return ListEntries(tmp, password);
            var fi = new FileInfo(path);
            string inner = Path.GetFileNameWithoutExtension(fi.Name);
            if (string.IsNullOrEmpty(inner)) inner = fi.Name + ".extraido";
            return new List<EntryInfo>
            {
                new() { Name = inner, Size = new FileInfo(tmp).Length, CompressedSize = fi.Length, Modified = fi.LastWriteTime }
            };
        }
        finally { TryDelete(tmp); }
    }

    public static bool NeedsPassword(string path)
    {
        try
        {
            if (IsTarGz(path) || Path.GetExtension(path).ToLowerInvariant() == ".gz")
            {
                string tmp = GunzipToTemp(path);
                try
                {
                    if (IsTarFile(tmp)) return NeedsPassword(tmp);
                    return false;
                }
                finally { TryDelete(tmp); }
            }
            using var archive = ArchiveFactory.Open(path);
            foreach (var e in archive.Entries)
            {
                if (!e.IsDirectory && e.IsEncrypted) return true;
            }
            return false;
        }
        catch (CryptographicException) { return true; }
        catch (InvalidOperationException ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase)) { return true; }
        catch { return false; }
    }

    /// <summary>
    /// Extrae solo las entradas indicadas (rutas con /) + abre ficheros en temporal.
    /// Las carpetas las expande quien llama con <see cref="ExpandSelection"/>.
    /// </summary>
    public static async Task ExtractEntriesAsync(string archivePath, string destDir, IEnumerable<string> entries,
        string? password, IProgress<(double pct, string current)>? progress, CancellationToken ct)
    {
        var wanted = new HashSet<string>(
            entries.Select(e => (e ?? "").Replace('\\', '/').Trim('/')),
            StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0) throw new ArgumentException("No hay entradas seleccionadas.");
        await Task.Run(() =>
        {
            Directory.CreateDirectory(destDir);
            string destFull = Path.GetFullPath(destDir);
            Exception? first = null;
            try { ExtractSelectedNative(archivePath, destFull, wanted, password, progress, ct); return; }
            catch (UnauthorizedAccessException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { first = ex; }
            try
            {
                progress?.Report((0, "Reintentando con motor 7-Zip..."));
                SevenZip.ExtractListAsync(archivePath, destFull, wanted, password, progress, ct)
                    .GetAwaiter().GetResult();
            }
            catch (UnauthorizedAccessException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch { throw FriendlyExtractError(first ?? new InvalidDataException("Selección ilegible.")); }
            progress?.Report((100, "Completado"));
        }, ct);
    }

    private static void ExtractSelectedNative(string path, string destFull, HashSet<string> wanted, string? password,
        IProgress<(double pct, string current)>? progress, CancellationToken ct)
    {
        if (IsTarGz(path))
        {
            string tmp = GunzipToTemp(path);
            string tar = tmp + ".tar";
            string work = MoveTemp(tmp, tar) ? tar : tmp;
            try { ExtractSelectedNative(work, destFull, wanted, password, progress, ct); return; }
            finally { TryDelete(tmp); TryDelete(tar); }
        }
        var opts = new ReaderOptions();
        if (!string.IsNullOrEmpty(password)) opts.Password = password;
        long total = wanted.Count, done = 0;
        using var stream = File.OpenRead(path);
        using var reader = ReaderFactory.Open(stream, opts);
        while (reader.MoveToNextEntry())
        {
            ct.ThrowIfCancellationRequested();
            var entry = reader.Entry;
            if (entry.IsDirectory) continue;
            string key = (entry.Key ?? "").Replace('\\', '/').Trim('/');
            if (!wanted.Contains(key)) continue;
            try
            {
                using var es = reader.OpenEntryStream();
                WriteStreamTo(es, destFull, key,
                    entry.Size > 0 ? entry.Size : Math.Max(entry.CompressedSize, 1),
                    entry.LastModifiedTime);
            }
            catch (CryptographicException ex)
            {
                throw new UnauthorizedAccessException("El archivo requiere contraseña o es incorrecta.", ex);
            }
            done++;
            progress?.Report((done * 100.0 / Math.Max(1, total), key));
        }
        if (done == 0) throw new FileNotFoundException("Ninguna entrada seleccionada se encontró en el archivo.");
    }

    /// <summary>Expande una selección (ficheros y/o carpetas con / final) a ficheros concretos.</summary>
    public static List<string> ExpandSelection(List<EntryInfo> all, IEnumerable<string> selected)
    {
        var names = all.Select(e => e.Name.Replace('\\', '/')).ToList();
        var out_ = new List<string>();
        foreach (var s in selected)
        {
            string norm = (s ?? "").Replace('\\', '/').Trim('/');
            if (names.Contains(norm, StringComparer.OrdinalIgnoreCase)) { out_.Add(norm); continue; }
            string prefix = norm.Trim('/') + "/";
            out_.AddRange(names.Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
        }
        return out_.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>¿Se puede modificar (añadir/eliminar)? GZ/ISO/CAB y demás son solo lectura.</summary>
    public static bool CanModify(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".zip" or ".7z" or ".tar" or ".rar" => true,
        _ => false
    };

    public static string ModifyBlockReason(string path) =>
        "Este formato es de solo lectura (p. ej. GZ, ISO, CAB). Extrae, modifica y vuelve a comprimir.";

    /// <summary>Añade ficheros/carpetas al archivo existente.</summary>
    public static async Task AddEntriesAsync(string archivePath, IEnumerable<string> sources, string? password,
        IProgress<double>? progress, CancellationToken ct,
        CompressLevel5 level = CompressLevel5.Normal)
    {
        string ext = Path.GetExtension(archivePath).ToLowerInvariant();
        var src = sources.Where(s => File.Exists(s) || Directory.Exists(s)).ToList();
        if (src.Count == 0) throw new FileNotFoundException("No hay archivos que añadir.");
        if (!CanModify(archivePath)) throw new InvalidOperationException(ModifyBlockReason(archivePath));

        if (ext == ".zip" && string.IsNullOrEmpty(password))
        {
            // Nativo y offline: abre en modo actualización
            await Task.Run(() =>
            {
                using var zip = ZipFile.Open(archivePath, ZipArchiveMode.Update);
                var existing = new HashSet<string>(zip.Entries.Select(e => e.FullName), StringComparer.OrdinalIgnoreCase);
                int i = 0, total = src.Count;
                foreach (var s in src)
                {
                    ct.ThrowIfCancellationRequested();
                    if (File.Exists(s))
                    {
                        string name = Path.GetFileName(s);
                        existing.Remove(name);
                        zip.GetEntry(name)?.Delete();
                        zip.CreateEntryFromFile(s, name, CompressionLevel.Optimal);
                    }
                    else
                    {
                        string root = Path.GetFullPath(s).TrimEnd(Path.DirectorySeparatorChar);
                        string baseName = new DirectoryInfo(root).Name;
                        foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                        {
                            string name = (baseName + "/" + Path.GetRelativePath(root, f)).Replace('\\', '/');
                            zip.GetEntry(name)?.Delete();
                            zip.CreateEntryFromFile(f, name, CompressionLevel.Optimal);
                        }
                        if (!Directory.EnumerateFileSystemEntries(root).Any())
                        {
                            string dn = baseName + "/";
                            if (!existing.Contains(dn)) zip.CreateEntry(dn);
                        }
                    }
                    progress?.Report(++i * 100.0 / Math.Max(1, total));
                }
            }, ct);
            progress?.Report(100);
            return;
        }
        if (ext == ".rar")
        {
            await RarEngine.CompressAsync(src, archivePath, password, progress, ct, level);
            return;
        }
        // ZIP con contraseña, 7Z y TAR: el motor 7-Zip añade sobre el existente
        await SevenZip.CompressAsync(src, archivePath,
            ext == ".7z" ? "7Z" : ext == ".tar" ? "TAR" : "ZIP",
            password, progress, ct, level);
    }

    /// <summary>Elimina la selección (ficheros y/o carpetas con / final); expande solo.</summary>
    public static async Task DeleteEntriesAsync(string archivePath, IEnumerable<string> selected, string? password,
        IProgress<double>? progress, CancellationToken ct)
    {
        var sel = selected.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        if (sel.Count == 0) throw new ArgumentException("Nada que eliminar.");
        if (!CanModify(archivePath)) throw new InvalidOperationException(ModifyBlockReason(archivePath));
        string ext = Path.GetExtension(archivePath).ToLowerInvariant();

        var all = await Task.Run(() => ListEntries(archivePath, password), ct);
        var files = ExpandSelection(all, sel);
        // Prefijos de carpeta seleccionados (lo que no sea un fichero concreto)
        var dirPrefixes = sel.Select(s => s.Replace('\\', '/').Trim('/'))
            .Where(s => !files.Contains(s, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (ext == ".zip" && string.IsNullOrEmpty(password))
        {
            var fileSet = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
            await Task.Run(() =>
            {
                using var zip = ZipFile.Open(archivePath, ZipArchiveMode.Update);
                foreach (var e in zip.Entries.ToList())
                {
                    ct.ThrowIfCancellationRequested();
                    string n = e.FullName.Replace('\\', '/').Trim('/');
                    if (fileSet.Contains(n) || dirPrefixes.Any(d => n == d || n.StartsWith(d + "/", StringComparison.OrdinalIgnoreCase)))
                        e.Delete();
                }
            }, ct);
            progress?.Report(100);
            return;
        }
        var engineTargets = files.Concat(dirPrefixes).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (engineTargets.Count == 0) throw new FileNotFoundException("Nada coincide en el archivo.");
        if (ext == ".rar")
        {
            await RarEngine.DeleteAsync(archivePath, engineTargets, password, progress, ct);
            return;
        }
        await SevenZip.DeleteAsync(archivePath, engineTargets, password, progress, ct);
    }

    /// <summary>Convierte cualquier archivo soportado a ZIP (para el Asistente).</summary>
    public static async Task ConvertToZipAsync(string archivePath, string outZip, string? password,
        IProgress<(double pct, string current)>? progress, CancellationToken ct)
    {
        string tmp = Path.Combine(Path.GetTempPath(), "ExtractX_conv_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            await ExtractAsync(archivePath, tmp, password,
                progress == null ? null : new Progress<(double pct, string current)>(t =>
                    progress.Report((t.pct / 2, t.current))), ct);
            var files = Directory.GetFileSystemEntries(tmp).ToList();
            if (files.Count == 0) throw new InvalidDataException("El archivo está vacío.");
            await CompressService.CompressAsync(files, outZip, CompressFormat.Zip,
                CompressLevel5.Normal, null,
                progress == null ? null : new Progress<double>(v => progress.Report((50 + v / 2, "Comprimiendo"))), ct);
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
        progress?.Report((100, "Completado"));
    }

    /// <summary>Tipo en español estilo WinRAR ("Carpeta de archivos", "Archivo RAR"...).</summary>
    public static string DescribeType(string name, bool isDir)
    {
        if (isDir) return "Carpeta de archivos";
        return Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".zip" => "Archivo ZIP",
            ".rar" => "Archivo RAR",
            ".7z" => "Archivo 7Z",
            ".tar" => "Archivo TAR",
            ".gz" or ".tgz" => "Archivo GZ",
            ".iso" => "Imagen ISO",
            ".txt" or ".md" or ".log" => "Documento de texto",
            ".pdf" => "Documento PDF",
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" => "Imagen",
            ".mp3" or ".wav" or ".flac" or ".ogg" => "Audio",
            ".mp4" or ".avi" or ".mkv" or ".mov" => "Vídeo",
            ".exe" => "Aplicación",
            ".dll" => "Biblioteca",
            ".csv" or ".xls" or ".xlsx" => "Hoja de cálculo",
            ".doc" or ".docx" => "Documento Word",
            ".json" or ".xml" or ".yml" or ".yaml" => "Datos",
            ".html" or ".htm" or ".css" or ".js" => "Archivo web",
            "" => "Archivo",
            var e => "Archivo " + e.TrimStart('.').ToUpperInvariant(),
        };
    }

    // ---------------- eliminar entradas ----------------

    // ---------------- verificación ----------------

    public static bool Verify(string path, string? password = null)
    {
        if (VerifyNative(path, password)) return true;
        // Segunda opinión con 7-Zip si está a mano (sin descargar nada).
        // Task.Run: evita interbloqueo al llamar código async desde la UI.
        try
        {
            if (SevenZip.Available || SevenZip.FullAvailable)
                return Task.Run(() => SevenZip.TestAsync(path, password, CancellationToken.None)).GetAwaiter().GetResult();
        }
        catch { }
        return false;
    }

    private static bool VerifyNative(string path, string? password = null)
    {
        try
        {
            if (IsTarGz(path))
            {
                string tmp = GunzipToTemp(path);
                try { return Verify(tmp, password); }
                finally { TryDelete(tmp); }
            }
            var opts = new ReaderOptions();
            if (!string.IsNullOrEmpty(password)) opts.Password = password;
            using var stream = File.OpenRead(path);
            using var reader = ReaderFactory.Open(stream, opts);
            while (reader.MoveToNextEntry())
            {
                if (reader.Entry.IsDirectory) continue;
                using var es = reader.OpenEntryStream();
                var buf = new byte[1024 * 256];
                while (es.Read(buf, 0, buf.Length) > 0) { /* fuerza CRC / descifrado */ }
            }
            return true;
        }
        catch { return false; }
    }

    // ---------------- extracción ----------------
    // Se usa la API secuencial (Reader): es la única que maneja RAR/7Z sólidos,
    // que es lo que rompía la extracción con "unpacked file size does not match header".

    public static async Task ExtractAsync(string archivePath, string destDir, string? password,
        IProgress<(double pct, string current)>? progress, CancellationToken ct)
    {
        string ext = Path.GetExtension(archivePath).ToLowerInvariant();
        bool rarLike = ext is ".rar" or ".iso" or ".wim" || IsTarGz(archivePath) == false && ext == ".001";

        // RAR/ISO: el motor 7-Zip completo los abre todos (sólidos, cifrados, RAR5).
        // Con contraseña: 7za descifra mejor (AES de 7-Zip) cuando está a mano.
        // Si ya está en disco se usa directo; si no, primero el nativo y 7-Zip como rescate.
        bool sevenFirst = (rarLike && SevenZip.FullAvailable)
            || (!string.IsNullOrEmpty(password) && SevenZip.Available);
        if (sevenFirst)
        {
            try { await SevenZip.ExtractAsync(archivePath, destDir, password, progress, ct); return; }
            catch (UnauthorizedAccessException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch { /* cae al motor nativo */ }
        }
        try
        {
            await ExtractInnerAsync(archivePath, destDir, password, progress, ct, depth: 0);
        }
        catch (UnauthorizedAccessException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception first)
        {
            // Rescate con 7-Zip (descarga el motor completo una sola vez si hace falta).
            try
            {
                progress?.Report((0, "Reintentando con motor 7-Zip..."));
                await SevenZip.ExtractAsync(archivePath, destDir, password, progress, ct);
            }
            catch (UnauthorizedAccessException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch { throw FriendlyExtractError(first); }
        }
    }

    private static async Task ExtractInnerAsync(string archivePath, string destDir, string? password,
        IProgress<(double pct, string current)>? progress, CancellationToken ct, int depth)
    {
        if (depth > 2) throw new InvalidDataException("Archivo anidado demasiado profundo.");
        await Task.Run(() =>
        {
            Directory.CreateDirectory(destDir);
            string destFull = Path.GetFullPath(destDir);

            if (IsTarGz(archivePath))
            {
                string tmp = GunzipToTemp(archivePath);
                string tar = tmp + ".tar";
                string work = MoveTemp(tmp, tar) ? tar : tmp;
                try
                {
                    progress?.Report((5, "Descomprimiendo capa GZip..."));
                    ExtractInnerAsync(work, destDir, password, progress, ct, depth + 1).GetAwaiter().GetResult();
                }
                finally { TryDelete(tmp); TryDelete(tar); }
                progress?.Report((100, "Completado"));
                return;
            }

            if (Path.GetExtension(archivePath).ToLowerInvariant() == ".gz")
            {
                ExtractSingleGz(archivePath, destDir, password, progress, ct);
                progress?.Report((100, "Completado"));
                return;
            }

            long total = 0;
            try { total = ListEntries(archivePath, password).Sum(e => e.Size > 0 ? e.Size : Math.Max(e.CompressedSize, 1)); } catch { }
            if (total <= 0) total = 1;

            Exception? firstError = null;
            try
            {
                long done = ExtractSequential(archivePath, destFull, password, progress, ct, total);
                if (done <= 0 && total > 1) throw new InvalidDataException("No se pudo leer ninguna entrada.");
            }
            catch (UnauthorizedAccessException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { firstError = ex; }

            if (firstError != null)
            {
                // Reintento con la API de archivo (acceso aleatorio): cubre ISO/CAB puntuales.
                try { ExtractRandomAccess(archivePath, destFull, password, progress, ct, total); }
                catch { throw FriendlyExtractError(firstError); }
            }
            progress?.Report((100, "Completado"));
        }, ct);
    }

    private static long ExtractSequential(string path, string destFull, string? password,
        IProgress<(double pct, string current)>? progress, CancellationToken ct, long total)
    {
        var opts = new ReaderOptions();
        if (!string.IsNullOrEmpty(password)) opts.Password = password;
        long done = 0;
        using var stream = File.OpenRead(path);
        using var reader = ReaderFactory.Open(stream, opts);
        while (reader.MoveToNextEntry())
        {
            ct.ThrowIfCancellationRequested();
            var entry = reader.Entry;
            if (entry.IsDirectory) continue;
            string key = entry.Key ?? "(sin nombre)";
            try
            {
                using var es = reader.OpenEntryStream();
                done += WriteStreamTo(es, destFull, key,
                    entry.Size > 0 ? entry.Size : Math.Max(entry.CompressedSize, 1),
                    entry.LastModifiedTime);
            }
            catch (CryptographicException ex)
            {
                throw new UnauthorizedAccessException("El archivo requiere contraseña o es incorrecta.", ex);
            }
            catch (Exception ex) when (ex is not UnauthorizedAccessException)
            {
                throw new InvalidDataException($"No se pudo extraer '{key}': {ex.Message}", ex);
            }
            progress?.Report((Math.Min(99, done * 100.0 / total), key));
        }
        return done;
    }

    private static System.IO.Stream OpenEntry(IEntry entry)
    {
        // IReader.Entry expone OpenEntryStream vía el reader; aquí llega IEntry:
        // se abre con reflexión segura sobre el tipo concreto del lector.
        var m = entry.GetType().GetMethod("OpenEntryStream", Type.EmptyTypes);
        if (m?.Invoke(entry, null) is System.IO.Stream s) return s;
        throw new InvalidDataException("El motor no pudo abrir la entrada.");
    }

    private static void ExtractRandomAccess(string path, string destFull, string? password,
        IProgress<(double pct, string current)>? progress, CancellationToken ct, long total)
    {
        var opts = new ReaderOptions();
        if (!string.IsNullOrEmpty(password)) opts.Password = password;
        using var archive = ArchiveFactory.Open(path, opts);
        var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
        long done = 0;
        foreach (var e in entries)
        {
            ct.ThrowIfCancellationRequested();
            string key = e.Key ?? "(sin nombre)";
            try
            {
                using var es = e.OpenEntryStream();
                done += WriteStreamTo(es, destFull, key,
                    e.Size > 0 ? e.Size : Math.Max(e.CompressedSize, 1), e.LastModifiedTime);
            }
            catch (CryptographicException ex)
            {
                throw new UnauthorizedAccessException("El archivo requiere contraseña o es incorrecta.", ex);
            }
            progress?.Report((Math.Min(99, done * 100.0 / total), key));
        }
    }

    private static void ExtractSingleGz(string path, string destDir, string? password,
        IProgress<(double pct, string current)>? progress, CancellationToken ct)
    {
        string tmp = GunzipToTemp(path);
        try
        {
            if (IsTarFile(tmp))
            {
                // Era un .tar comprimido con otro nombre: se extrae como tal.
                ExtractInnerAsync(tmp, destDir, password, progress, ct, 1).GetAwaiter().GetResult();
                return;
            }
            var fi = new FileInfo(path);
            string inner = Path.GetFileNameWithoutExtension(fi.Name);
            if (string.IsNullOrEmpty(inner)) inner = fi.Name + ".extraido";
            string dest = Path.Combine(destDir, inner);
            AssertSafePath(Path.GetFullPath(destDir), inner);
            progress?.Report((50, inner));
            File.Copy(tmp, dest, true);
        }
        finally { TryDelete(tmp); }
    }

    /// <summary>
    /// Escribe el flujo de una entrada en una ruta saneada dentro del destino:
    /// quita unidades (C:), barras iniciales y ".." (como WinRAR/7-Zip).
    /// Devuelve los bytes contabilizados para el progreso.
    /// </summary>
    private static long WriteStreamTo(System.IO.Stream source, string destFull, string key, long sizeUnits, DateTime? modified)
    {
        string safe = SanitizeKey(key);
        string full = Path.Combine(destFull, safe);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var fout = File.Create(full);
        source.CopyTo(fout);
        if (modified.HasValue)
        {
            try { File.SetLastWriteTime(full, modified.Value); } catch { }
        }
        return sizeUnits;
    }

    private static string SanitizeKey(string key)
    {
        string k = (key ?? "(sin nombre)").Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        // Quita unidad tipo "C:" y barras iniciales
        int colon = k.IndexOf(':');
        if (colon >= 0 && colon < 3) k = k[(colon + 1)..];
        k = k.TrimStart(Path.DirectorySeparatorChar);
        // Resuelve "." y ".." sin salir nunca del destino
        var parts = new List<string>();
        foreach (var p in k.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            if (p == ".") continue;
            if (p == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); continue; }
            parts.Add(p);
        }
        if (parts.Count == 0) throw new UnauthorizedAccessException($"Entrada insegura omitida: '{key}'.");
        return Path.Combine(parts.ToArray());
    }

    private static void AssertSafePath(string destFull, string key)
    {
        string full = Path.GetFullPath(Path.Combine(destFull, SanitizeKey(key)));
        if (!full.StartsWith(destFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"Entrada insegura omitida: '{key}'.");
    }

    private static Exception FriendlyReadError(Exception ex) =>
        new InvalidDataException("No se pudo leer el archivo. Puede estar dañado, incompleto o usar un formato no compatible. (" + Short(ex) + ")", ex);

    private static Exception FriendlyExtractError(Exception ex)
    {
        string m = ex.Message + " " + (ex.InnerException?.Message ?? "");
        if (m.Contains("unpacked file size", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("header", StringComparison.OrdinalIgnoreCase) && m.Contains("match", StringComparison.OrdinalIgnoreCase))
            return new InvalidDataException("El archivo parece dañado o usa un RAR sólido/cifrado que el motor no puede abrir. Prueba a re-descargarlo o ábrelo con WinRAR para este caso.", ex);
        return new InvalidDataException("Error al extraer: " + ex.Message, ex);
    }

    private static string Short(Exception ex)
    {
        string m = ex.Message;
        return m.Length > 120 ? m[..120] + "…" : m;
    }

    // ---------------- gzip / tar helpers ----------------

    public static string GunzipToTemp(string gzPath)
    {
        string tmp = Path.Combine(Path.GetTempPath(), "ExtractX_" + Guid.NewGuid().ToString("N"));
        using var fin = File.OpenRead(gzPath);
        using var gz = new GZipStream(fin, CompressionMode.Decompress);
        using var fout = File.Create(tmp);
        gz.CopyTo(fout);
        return tmp;
    }

    public static bool IsTarFile(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            if (fs.Length < 512 + 5) return false;
            fs.Seek(257, SeekOrigin.Begin);
            var buf = new byte[5];
            return fs.Read(buf, 0, 5) == 5 && buf[0] == 'u' && buf[1] == 's' && buf[2] == 't' && buf[3] == 'a' && buf[4] == 'r';
        }
        catch { return false; }
    }

    private static bool MoveTemp(string tmp, string newPath)
    {
        try { File.Move(tmp, newPath, true); return true; }
        catch { return false; }
    }

    private static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }

    // ---------------- compresión: fachada ----------------
    // 7Z siempre necesita el motor 7-Zip; si hay 7za local se usa (AES-256, niveles),
    // si no, se comprime con el motor nativo (ZIP/TAR/TGZ/GZ, sin dependencias).

    public static string DefaultExtension(string format) => (format ?? "ZIP").ToUpperInvariant() switch
    {
        "7Z" => ".7z",
        "RAR" => ".rar",
        "TAR" => ".tar",
        "TAR.GZ" or "TGZ" => ".tar.gz",
        "GZ" or "GZIP" => ".gz",
        _ => ".zip"
    };

    public static CompressLevel5 ParseLevel(string? name) => (name ?? "Normal") switch
    {
        "Sin compresión" => CompressLevel5.Stored,
        "Rápida" => CompressLevel5.Fast,
        "Máxima" => CompressLevel5.Maximum,
        _ => CompressLevel5.Normal
    };

    public static async Task CompressAsync(IEnumerable<string> sources, string outputPath, string format,
        string? password, IProgress<double>? progress, CancellationToken ct,
        CompressLevel5 level = CompressLevel5.Normal, bool solid = false)
    {
        string f = (format ?? "ZIP").ToUpperInvariant();
        if (f == "RAR")
        {
            await RarEngine.CompressAsync(sources, outputPath, password, progress, ct, level, solid);
            return;
        }
        if (f == "7Z" || SevenZip.Available)
        {
            await SevenZip.CompressAsync(sources, outputPath, f, password, progress, ct, level, solid);
            return;
        }
        var fmt = f switch
        {
            "TAR" => CompressFormat.Tar,
            "TAR.GZ" or "TGZ" => CompressFormat.TarGz,
            "GZ" or "GZIP" => CompressFormat.Gz,
            _ => CompressFormat.Zip
        };
        await CompressService.CompressAsync(sources, outputPath, fmt, level, password, progress, ct);
    }

    // ---------------- compat: compresión ZIP clásica ----------------

    public static async Task CompressZipAsync(IEnumerable<string> sources, string outputZip,
        IProgress<double>? progress, CancellationToken ct)
    {
        await CompressService.CompressAsync(sources, outputZip,
            CompressFormat.Zip, CompressLevel5.Normal, password: null, progress, ct);
    }
}
