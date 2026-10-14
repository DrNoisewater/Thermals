using LibreHardwareMonitor.Hardware;

namespace Thermals.Core;

/// <summary>Picks a sensible first-run overlay (CPU/GPU temp, load, power and RAM usage).</summary>
public static class DefaultOverlay
{
    public static List<OverlayItem> Pick(SensorSnapshot s)
    {
        var items = new List<OverlayItem>();
        var gpuTypes = new[] { HardwareType.GpuNvidia, HardwareType.GpuAmd, HardwareType.GpuIntel };

        // Prefer a discrete GPU: Intel iGPUs are listed last.
        var gpu = s.Hardware
            .Where(h => gpuTypes.Contains(h.Type))
            .OrderBy(h => h.Type == HardwareType.GpuIntel ? 1 : 0)
            .FirstOrDefault();

        Add(items, "CPU", Find(s, n => n.Type == HardwareType.Cpu, SensorType.Temperature,
            "CPU Package", "Core (Tctl/Tdie)", "Core (Tctl)", "Core (Tdie)", "Core Max", "Core Average"));
        Add(items, "CPU", Find(s, n => n.Type == HardwareType.Cpu, SensorType.Load, "CPU Total"));
        Add(items, "CPU", Find(s, n => n.Type == HardwareType.Cpu, SensorType.Power, "CPU Package", "Package"));

        if (gpu != null)
        {
            Add(items, "GPU", Find(s, n => n.Id == gpu.Id, SensorType.Temperature, "GPU Core", "GPU Hot Spot"));
            Add(items, "GPU", Find(s, n => n.Id == gpu.Id, SensorType.Load, "GPU Core", "D3D 3D"));
            Add(items, "GPU", Find(s, n => n.Id == gpu.Id, SensorType.Power, "GPU Package", "GPU Core", "GPU Power"));
        }

        Add(items, "RAM", Find(s, n => n.Type == HardwareType.Memory && !n.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase),
            SensorType.Load, "Memory"));
        return items;
    }

    private static void Add(List<OverlayItem> items, string label, SensorReading? r)
    {
        if (r != null && items.All(i => i.SensorId != r.Id))
            items.Add(new OverlayItem { SensorId = r.Id, Label = label });
    }

    private static SensorReading? Find(SensorSnapshot s, Func<HardwareNode, bool> hw, SensorType type, params string[] names)
    {
        var candidates = s.Hardware.Where(hw).SelectMany(n => n.Sensors).Where(r => r.Type == type).ToList();
        foreach (var name in names)
        {
            var exact = candidates.FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
        }
        foreach (var name in names)
        {
            var partial = candidates.FirstOrDefault(r => r.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (partial != null) return partial;
        }
        return null;
    }

    /// <summary>A short label for a sensor the user pins from the sensor list.</summary>
    public static string AutoLabel(SensorReading r)
    {
        string prefix = r.Hardware.Type switch
        {
            HardwareType.Cpu => "CPU",
            HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => "GPU",
            HardwareType.Memory => "RAM",
            _ => "",
        };
        string name = r.Name;
        if (prefix.Length > 0 && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return name;
        if (prefix.Length > 0) return $"{prefix} {name}";

        string hw = r.Hardware.Name.Length > 14 ? r.Hardware.Name[..14].TrimEnd() + "…" : r.Hardware.Name;
        return $"{hw} {name}";
    }
}
