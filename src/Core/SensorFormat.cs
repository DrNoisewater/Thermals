using System.Globalization;
using LibreHardwareMonitor.Hardware;

namespace Thermals.Core;

/// <summary>Unit-aware formatting. Values are shown with sensible precision for each sensor type.</summary>
public static class SensorFormat
{
    /// <summary>Set by the host from settings; read from the UI and logging threads.</summary>
    public static volatile bool Fahrenheit;

    public const string Missing = "—";

    public static double ToDisplay(SensorType type, float value) =>
        type == SensorType.Temperature && Fahrenheit ? value * 9.0 / 5.0 + 32.0 : value;

    public static string Format(SensorType type, float? value)
    {
        if (value is not float v || float.IsNaN(v) || float.IsInfinity(v)) return Missing;
        double x = ToDisplay(type, v);
        var c = CultureInfo.CurrentCulture;

        switch (type)
        {
            case SensorType.Temperature: return x.ToString("0.0", c) + (Fahrenheit ? " °F" : " °C");
            case SensorType.Load:
            case SensorType.Control:
            case SensorType.Level: return x.ToString("0.0", c) + " %";
            case SensorType.Clock: return x.ToString("0", c) + " MHz";
            case SensorType.Power: return x.ToString(x >= 100 ? "0.0" : "0.00", c) + " W";
            case SensorType.Voltage: return x.ToString("0.000", c) + " V";
            case SensorType.Current: return x.ToString("0.000", c) + " A";
            case SensorType.Fan: return x.ToString("0", c) + " RPM";
            case SensorType.Flow: return x.ToString("0.0", c) + " L/h";
            case SensorType.Frequency: return x.ToString("0.0", c) + " Hz";
            case SensorType.Factor: return x.ToString("0.000", c);
            case SensorType.Data: return x.ToString("0.0", c) + " GB";
            case SensorType.SmallData: return x.ToString("0", c) + " MB";
            case SensorType.Throughput: return Throughput(x, c);
        }

        // Types that only exist in some library versions are matched by name so the build never
        // depends on a specific enum layout.
        switch (type.ToString())
        {
            case "TimeSpan":
                return x < 0 ? Missing : TimeSpan.FromSeconds(x).ToString(x >= 86400 ? @"d\.hh\:mm\:ss" : @"hh\:mm\:ss", c);
            case "Energy": return x.ToString("0", c) + " mWh";
            case "Noise": return x.ToString("0.0", c) + " dBA";
            case "Humidity": return x.ToString("0.0", c) + " %";
            case "Timing": return x.ToString("0.0", c) + " ns";
            default: return x.ToString("0.###", c);
        }
    }

    /// <summary>Compact value for the overlay and tray (fewer decimals).</summary>
    public static string FormatCompact(SensorType type, float? value)
    {
        if (value is not float v || float.IsNaN(v) || float.IsInfinity(v)) return Missing;
        double x = ToDisplay(type, v);
        var c = CultureInfo.CurrentCulture;
        switch (type)
        {
            case SensorType.Temperature: return x.ToString("0", c) + (Fahrenheit ? "°F" : "°C");
            case SensorType.Load:
            case SensorType.Control:
            case SensorType.Level: return x.ToString("0", c) + "%";
            case SensorType.Power: return x.ToString(x >= 10 ? "0" : "0.0", c) + " W";
            case SensorType.Voltage: return x.ToString("0.000", c) + " V";
            case SensorType.Clock: return x >= 1000 ? (x / 1000).ToString("0.00", c) + " GHz" : x.ToString("0", c) + " MHz";
            case SensorType.Data: return x.ToString("0.0", c) + " GB";
            case SensorType.SmallData: return x >= 1024 ? (x / 1024).ToString("0.0", c) + " GB" : x.ToString("0", c) + " MB";
            default: return Format(type, value);
        }
    }

    /// <summary>Number only, invariant culture (for CSV).</summary>
    public static string Raw(SensorType type, float? value)
    {
        if (value is not float v || float.IsNaN(v) || float.IsInfinity(v)) return "";
        return ToDisplay(type, v).ToString("0.###", CultureInfo.InvariantCulture);
    }

    public static string Unit(SensorType type) => type switch
    {
        SensorType.Temperature => Fahrenheit ? "°F" : "°C",
        SensorType.Load or SensorType.Control or SensorType.Level => "%",
        SensorType.Clock => "MHz",
        SensorType.Power => "W",
        SensorType.Voltage => "V",
        SensorType.Current => "A",
        SensorType.Fan => "RPM",
        SensorType.Flow => "L/h",
        SensorType.Frequency => "Hz",
        SensorType.Data => "GB",
        SensorType.SmallData => "MB",
        SensorType.Throughput => "B/s",
        _ => type.ToString() switch { "TimeSpan" => "s", "Energy" => "mWh", "Noise" => "dBA", "Humidity" => "%", "Timing" => "ns", _ => "" },
    };

    /// <summary>
    /// Fixed thresholds a device reports about itself (e.g. an NVMe drive's warning and critical
    /// temperatures). They are limits, not readings, so they are never coloured as "hot".
    /// </summary>
    public static bool IsLimit(SensorReading r) =>
        r.Type == SensorType.Temperature &&
        (r.Name.Contains("Warning Temperature", StringComparison.OrdinalIgnoreCase) ||
         r.Name.Contains("Critical Temperature", StringComparison.OrdinalIgnoreCase));

    public static string TypeName(SensorType type) => type switch
    {
        SensorType.Load => "Usage",
        SensorType.Control => "Fan control",
        SensorType.SmallData => "Data",
        SensorType.Throughput => "Throughput",
        _ => type.ToString(),
    };

    /// <summary>Display order for sensor types inside a hardware group (most useful first).</summary>
    public static int Order(SensorType type) => type switch
    {
        SensorType.Temperature => 0,
        SensorType.Load => 1,
        SensorType.Clock => 2,
        SensorType.Power => 3,
        SensorType.Voltage => 4,
        SensorType.Current => 5,
        SensorType.Fan => 6,
        SensorType.Control => 7,
        SensorType.Flow => 8,
        SensorType.Frequency => 9,
        SensorType.Data => 10,
        SensorType.SmallData => 11,
        SensorType.Throughput => 12,
        SensorType.Level => 13,
        SensorType.Factor => 14,
        _ => 20,
    };

    private static string Throughput(double bytesPerSec, CultureInfo c)
    {
        if (bytesPerSec < 1024) return bytesPerSec.ToString("0", c) + " B/s";
        if (bytesPerSec < 1024 * 1024) return (bytesPerSec / 1024).ToString("0.0", c) + " KB/s";
        if (bytesPerSec < 1024d * 1024 * 1024) return (bytesPerSec / (1024 * 1024)).ToString("0.00", c) + " MB/s";
        return (bytesPerSec / (1024d * 1024 * 1024)).ToString("0.00", c) + " GB/s";
    }

    public static string Bytes(double bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
        int i = 0;
        while (bytes >= 1024 && i < units.Length - 1) { bytes /= 1024; i++; }
        return bytes.ToString(i == 0 ? "0" : "0.##", CultureInfo.CurrentCulture) + " " + units[i];
    }
}
