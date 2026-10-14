using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thermals.Core;

public enum OverlayCorner { TopLeft, TopRight, BottomLeft, BottomRight }

public sealed class OverlayItem
{
    public string SensorId { get; set; } = "";
    public string Label { get; set; } = "";
}

public sealed class OverlaySettings
{
    public bool Enabled { get; set; } = true;
    public bool ItemsInitialized { get; set; }
    public List<OverlayItem> Items { get; set; } = new();

    public OverlayCorner Corner { get; set; } = OverlayCorner.TopRight;
    /// <summary>Screen.DeviceName, or null for the primary monitor.</summary>
    public string? MonitorDevice { get; set; }
    public int MarginPx { get; set; } = 12;

    public string FontFamily { get; set; } = "Segoe UI";
    public float FontSizePt { get; set; } = 10f;
    public int TextColorArgb { get; set; } = unchecked((int)0xFFF2F2F2);
    public int BackgroundAlpha { get; set; } = 150;   // 0 = text only, 255 = solid

    public bool ColorThresholds { get; set; } = true;
    public int WarnTempC { get; set; } = 80;
    public int HotTempC { get; set; } = 90;
}

public sealed class AppSettings
{
    public int PollIntervalMs { get; set; } = 1000;
    public bool Fahrenheit { get; set; } = true;

    // Hardware groups. Disabling ones you don't care about lowers the polling cost.
    public bool CpuEnabled { get; set; } = true;
    public bool GpuEnabled { get; set; } = true;
    public bool MemoryEnabled { get; set; } = true;
    public bool MotherboardEnabled { get; set; } = true;
    public bool StorageEnabled { get; set; } = true;
    public bool NetworkEnabled { get; set; } = true;
    public bool BatteryEnabled { get; set; } = true;
    public bool ControllerEnabled { get; set; }   // AIO / fan controllers (USB HID)
    public bool PsuEnabled { get; set; }          // Corsair-style digital PSUs
    public bool ThrottleDrives { get; set; } = true;  // SSDs every 5 s, hard drives every 60 s (sleeping drives are never woken)

    public bool MinimizeToTray { get; set; } = true;
    public bool StartMinimized { get; set; }
    public bool TrayShowsValue { get; set; } = true;

    public string? LogDirectory { get; set; }

    public int WindowX { get; set; }
    public int WindowY { get; set; }
    public int WindowW { get; set; }
    public int WindowH { get; set; }
    /// <summary>Bumped when the default window size changes, so an old remembered size is replaced once.</summary>
    public int WindowLayoutVersion { get; set; }
    /// <summary>Notices the user dismissed (e.g. the memory-speed suggestion), so they don't come back.</summary>
    public List<string> DismissedNotices { get; set; } = new();

    public OverlaySettings Overlay { get; set; } = new();

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), Json);
                if (s != null) return s.Sanitized();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Settings file unreadable; using defaults", ex);
            try { File.Copy(AppPaths.SettingsFile, AppPaths.SettingsFile + ".bad", true); } catch { }
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            var tmp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, AppPaths.SettingsFile, overwrite: true);   // atomic replace
        }
        catch (Exception ex)
        {
            Log.Error("Could not save settings", ex);
        }
    }

    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, Json), Json)!;

    private AppSettings Sanitized()
    {
        PollIntervalMs = Math.Clamp(PollIntervalMs, 250, 10_000);
        Overlay ??= new OverlaySettings();
        Overlay.Items ??= new List<OverlayItem>();
        Overlay.Items.RemoveAll(i => string.IsNullOrWhiteSpace(i.SensorId));
        Overlay.FontSizePt = Math.Clamp(Overlay.FontSizePt, 6f, 48f);
        Overlay.BackgroundAlpha = Math.Clamp(Overlay.BackgroundAlpha, 0, 255);
        Overlay.MarginPx = Math.Clamp(Overlay.MarginPx, 0, 500);
        if (string.IsNullOrWhiteSpace(Overlay.FontFamily)) Overlay.FontFamily = "Segoe UI";
        return this;
    }
}
