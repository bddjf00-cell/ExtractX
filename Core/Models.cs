using System.Text.Json.Serialization;

namespace ExtractX.Core;

public sealed class HistoryEntry
{
    public string FileName { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string Destination { get; set; } = "";
    public long SizeBytes { get; set; } = 0;
    public string SizeText { get; set; } = "";
    public DateTime Date { get; set; } = DateTime.Now;
    public string Status { get; set; } = "Extraído";
    public bool Success { get; set; } = true;
}

public sealed class FavoriteFolder
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
}

public sealed class SavedPassword
{
    public string Label { get; set; } = "";
    public string Password { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.Now;
}

public sealed class AppSettings
{
    public string Theme { get; set; } = "Oscuro";
    public string Language { get; set; } = "Español";
    public bool AutoOpen { get; set; } = true;
    public bool CheckUpdates { get; set; } = true;
    public bool AssociateZip { get; set; } = true;
    public bool AssociateRar { get; set; } = false;
    public bool Associate7z { get; set; } = false;
    public string DefaultCompressFormat { get; set; } = "ZIP";
    public string DefaultCompressLevel { get; set; } = "Normal";
    public string UpdateRepo { get; set; } = "";
    public string SkippedVersion { get; set; } = "";
}

public sealed class EntryInfo
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public long CompressedSize { get; set; }
    public DateTime? Modified { get; set; }
    public bool IsDirectory { get; set; }
    public string SizeText => ArchiveService.FormatSize(Size);
}
