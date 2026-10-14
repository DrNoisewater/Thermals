using System.Text;

namespace Thermals.Core;

internal static class AppPaths
{
    public const string Company = "Noisewater Labs";

    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Company, "Thermals");

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string ErrorLog => Path.Combine(DataDir, "error.log");
    public static string DefaultLogDir => Path.Combine(DataDir, "Logs");

    static AppPaths()
    {
        try
        {
            // Carry settings over from the pre-branding location (%AppData%\Thermals).
            string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Thermals");
            if (!Directory.Exists(DataDir) && Directory.Exists(legacy))
            {
                Directory.CreateDirectory(DataDir);
                foreach (var file in Directory.GetFiles(legacy))
                    File.Copy(file, Path.Combine(DataDir, Path.GetFileName(file)), overwrite: false);
            }
            Directory.CreateDirectory(DataDir);
        }
        catch
        {
            // Reported when a write fails.
        }
    }
}

/// <summary>Tiny append-only error log. Never throws.</summary>
internal static class Log
{
    private static readonly object Gate = new();

    public static void Error(string context, Exception? ex = null)
    {
        try
        {
            var sb = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                .Append("  ").Append(context);
            if (ex != null) sb.AppendLine().Append(ex);
            sb.AppendLine();

            lock (Gate)
            {
                var file = new FileInfo(AppPaths.ErrorLog);
                // Keep the log from growing without bound.
                if (file.Exists && file.Length > 1_000_000) file.Delete();
                File.AppendAllText(AppPaths.ErrorLog, sb.ToString());
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
