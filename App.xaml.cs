using System.IO;
using System.Windows;
using ExtractX.Core;

namespace ExtractX;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var files = e.Args
            .Where(a => !a.StartsWith("-") && !a.StartsWith("/"))
            .Where(a => File.Exists(a) || Directory.Exists(a))
            .ToList();

        bool forceFull = e.Args.Any(a => a.Equals("--full", StringComparison.OrdinalIgnoreCase));
        bool forceCompress = e.Args.Any(a => a.Equals("--compress", StringComparison.OrdinalIgnoreCase));

        if (!forceFull && files.Count == 1 && File.Exists(files[0]) &&
            ArchiveService.SupportedExtensions.Contains(Path.GetExtension(files[0]).ToLowerInvariant()))
        {
            // Doble clic a un .zip/.rar/... -> mini ventana de extracción
            new MiniWindow(files[0]).Show();
        }
        else if (!forceFull && forceCompress && files.Count > 0)
        {
            // Enviar a / menú "Comprimir con ExtractX" -> mini de compresión
            new MiniWindow(sources: files).Show();
        }
        else if (!forceFull && files.Count > 1)
        {
            // Arrastrar varios archivos al exe -> mini de compresión
            new MiniWindow(sources: files).Show();
        }
        else
        {
            // Sin argumentos -> modo grande completo
            new MainWindow().Show();
        }
    }
}
