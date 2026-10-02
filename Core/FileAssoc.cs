using Microsoft.Win32;

namespace ExtractX.Core;

/// <summary>
/// Asociación de formatos (doble clic abre ExtractX) y menús contextuales.
/// Todo en HKCU: no requiere permisos de administrador.
/// </summary>
public static class FileAssoc
{
    private const string AppKey = "ExtractX.archive";

    private static string ExePath =>
        Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";

    public static bool IsAssociated(string ext)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + ext);
            return k?.GetValue("")?.ToString() == AppKey;
        }
        catch { return false; }
    }

    public static bool Associate(string ext)
    {
        try
        {
            var exe = ExePath;
            if (string.IsNullOrEmpty(exe)) return false;
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ext))
            {
                var prev = k.GetValue("")?.ToString();
                if (!string.IsNullOrEmpty(prev) && prev != AppKey)
                    Registry.CurrentUser.CreateSubKey(@"Software\ExtractX\Backup")?.SetValue(ext, prev);
                k.SetValue("", AppKey);
            }
            using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{AppKey}\shell\open\command"))
                k.SetValue("", $"\"{exe}\" \"%1\"");
            using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{AppKey}"))
                k.SetValue("FriendlyAppName", "ExtractX");
            RegisterContextMenus();
            NotifyShell();
            return true;
        }
        catch { return false; }
    }

    public static bool Unassociate(string ext)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + ext, true);
            if (k?.GetValue("")?.ToString() == AppKey)
            {
                var backup = Registry.CurrentUser.OpenSubKey(@"Software\ExtractX\Backup")?.GetValue(ext)?.ToString();
                if (!string.IsNullOrEmpty(backup)) k.SetValue("", backup);
                else k.DeleteValue("", false);
            }
            NotifyShell();
            return true;
        }
        catch { return false; }
    }

    /// <summary>Menú clic derecho: "Extraer con ExtractX" y "Comprimir con ExtractX".</summary>
    public static void RegisterContextMenus()
    {
        try
        {
            var exe = ExePath;
            if (string.IsNullOrEmpty(exe)) return;
            foreach (var ext in ArchiveService.SupportedExtensions)
            {
                using var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\SystemFileAssociations\{ext}\shell\ExtractX.Extract");
                k.SetValue("", "Extraer con ExtractX");
                k.SetValue("Icon", $"\"{exe}\",0");
                using var c = Registry.CurrentUser.CreateSubKey($@"Software\Classes\SystemFileAssociations\{ext}\shell\ExtractX.Extract\command");
                c.SetValue("", $"\"{exe}\" --extract \"%1\"");
                using var k2 = Registry.CurrentUser.CreateSubKey($@"Software\Classes\SystemFileAssociations\{ext}\shell\ExtractX.Open");
                k2.SetValue("", "Abrir con ExtractX");
                k2.SetValue("Icon", $"\"{exe}\",0");
                using var c2 = Registry.CurrentUser.CreateSubKey($@"Software\Classes\SystemFileAssociations\{ext}\shell\ExtractX.Open\command");
                c2.SetValue("", $"\"{exe}\" \"%1\"");
            }
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\*\shell\ExtractX.Compress"))
            {
                k.SetValue("", "Comprimir con ExtractX");
                k.SetValue("Icon", $"\"{exe}\",0");
                using var c = Registry.CurrentUser.CreateSubKey(@"Software\Classes\*\shell\ExtractX.Compress\command");
                c.SetValue("", $"\"{exe}\" --compress \"%1\"");
            }
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Directory\shell\ExtractX.Compress"))
            {
                k.SetValue("", "Comprimir con ExtractX");
                k.SetValue("Icon", $"\"{exe}\",0");
                using var c = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Directory\shell\ExtractX.Compress\command");
                c.SetValue("", $"\"{exe}\" --compress \"%1\"");
            }
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private static void NotifyShell()
    {
        try { SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero); } catch { }
    }
}
