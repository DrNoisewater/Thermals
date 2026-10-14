using System.Globalization;
using System.Text.RegularExpressions;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;

namespace Thermals.Core;

public sealed class InfoSection
{
    public InfoSection(string title) => Title = title;
    public string Title { get; }
    public List<KeyValuePair<string, string>> Rows { get; } = new();
    /// <summary>Keys of rows worth the user's attention (shown highlighted).</summary>
    public HashSet<string> Warnings { get; } = new();

    public void Add(string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) Rows.Add(new(key, value.Trim()));
    }

    public void Insert(int index, string key, string value, bool warning = false)
    {
        Rows.Insert(Math.Min(index, Rows.Count), new(key, value));
        if (warning) Warnings.Add(key);
    }
}

/// <summary>Memory running well below the speed it is rated for.</summary>
public sealed record MemorySpeedIssue(int RunningMTs, int RatedMTs, string Profile)
{
    public string Summary =>
        $"Your memory runs at {RunningMTs} MT/s but is rated for {RatedMTs} MT/s. {Profile} is probably off in the BIOS.";

    public string Details =>
        $"Running at {RunningMTs} MT/s, but the modules are rated for {RatedMTs} MT/s. This usually means {Profile} " +
        "is turned off in the BIOS; turning it on gives you the full speed. (Rating read from the part number.)";
}

/// <summary>
/// Static system inventory (WMI + registry). Collected once on a background thread.
/// Where WMI is known to be wrong (e.g. VRAM capped at 4 GB in Win32_VideoController,
/// Windows 11 reported as 10 in the registry) the more reliable source is used.
/// </summary>
public static class SystemInfo
{
    public static List<InfoSection> Collect()
    {
        var sections = new List<InfoSection>();
        Try(sections, "Operating system", OperatingSystem);
        Try(sections, "Computer", Computer);
        Try(sections, "Processor", Processor);
        Try(sections, "Memory", Memory);
        Try(sections, "Graphics", Graphics);
        Try(sections, "Storage", Storage);
        Try(sections, "Volumes", Volumes);
        Try(sections, "Network", Network);
        Try(sections, "Displays", Displays);
        return sections;
    }

    private static void Try(List<InfoSection> sections, string title, Action<InfoSection> fill)
    {
        var s = new InfoSection(title);
        try { fill(s); }
        catch (Exception ex)
        {
            Log.Error($"System info '{title}' failed", ex);
            s.Add("Error", ex.Message);
        }
        if (s.Rows.Count > 0) sections.Add(s);
    }

    private static IEnumerable<ManagementObject> Query(string wql, string scope = @"root\cimv2")
    {
        using var searcher = new ManagementObjectSearcher(scope, wql);
        using var results = searcher.Get();
        foreach (ManagementObject mo in results)
            yield return mo;
    }

    private static string? Str(ManagementObject mo, string prop)
    {
        try { return mo[prop]?.ToString()?.Trim(); } catch { return null; }
    }

    private static ulong U64(ManagementObject mo, string prop)
    {
        try { return mo[prop] == null ? 0 : Convert.ToUInt64(mo[prop], CultureInfo.InvariantCulture); }
        catch { return 0; }
    }

    // ---------------------------------------------------------------------------------------

    private static void OperatingSystem(InfoSection s)
    {
        foreach (var mo in Query("SELECT Caption, OSArchitecture, LastBootUpTime, InstallDate FROM Win32_OperatingSystem"))
        {
            s.Add("Edition", Str(mo, "Caption"));
            s.Add("Architecture", Str(mo, "OSArchitecture"));
            var boot = Str(mo, "LastBootUpTime");
            if (boot != null)
            {
                var t = ManagementDateTimeConverter.ToDateTime(boot);
                var up = DateTime.Now - t;
                s.Add("Last boot", $"{t:g}  (up {(int)up.TotalDays}d {up.Hours}h {up.Minutes}m)");
            }
            var installed = Str(mo, "InstallDate");
            if (installed != null) s.Add("Installed", ManagementDateTimeConverter.ToDateTime(installed).ToString("d"));
        }

        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        if (key != null)
        {
            s.Add("Version", key.GetValue("DisplayVersion")?.ToString());
            var build = key.GetValue("CurrentBuildNumber")?.ToString();
            var ubr = key.GetValue("UBR")?.ToString();
            s.Add("Build", ubr != null ? $"{build}.{ubr}" : build);
        }
        s.Add("Machine name", Environment.MachineName);
    }

    private static void Computer(InfoSection s)
    {
        foreach (var mo in Query("SELECT Manufacturer, Model FROM Win32_ComputerSystem"))
            s.Add("System", Join(Str(mo, "Manufacturer"), Str(mo, "Model")));
        foreach (var mo in Query("SELECT Manufacturer, Product, Version FROM Win32_BaseBoard"))
            s.Add("Motherboard", Join(Str(mo, "Manufacturer"), Str(mo, "Product"), Clean(Str(mo, "Version"))));
        foreach (var mo in Query("SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS"))
        {
            var date = Str(mo, "ReleaseDate");
            string? d = date != null ? ManagementDateTimeConverter.ToDateTime(date).ToString("d") : null;
            s.Add("BIOS", Join(Str(mo, "Manufacturer"), Str(mo, "SMBIOSBIOSVersion")) + (d != null ? $"  ({d})" : ""));
        }
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            if (key?.GetValue("UEFISecureBootEnabled") is int sb) s.Add("Secure Boot", sb == 1 ? "On" : "Off");
        }
        catch { }
    }

    private static void Processor(InfoSection s)
    {
        int sockets = 0, cores = 0, threads = 0;
        foreach (var mo in Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, L2CacheSize, L3CacheSize, SocketDesignation, Description FROM Win32_Processor"))
        {
            sockets++;
            cores += (int)U64(mo, "NumberOfCores");
            threads += (int)U64(mo, "NumberOfLogicalProcessors");
            if (sockets == 1)
            {
                s.Add("Name", Str(mo, "Name"));
                s.Add("Identifier", Str(mo, "Description"));
                s.Add("Socket", Str(mo, "SocketDesignation"));
                s.Add("Base clock", $"{U64(mo, "MaxClockSpeed")} MHz");
                ulong l2 = U64(mo, "L2CacheSize"), l3 = U64(mo, "L3CacheSize");
                if (l2 > 0) s.Add("L2 cache", SensorFormat.Bytes(l2 * 1024));
                if (l3 > 0) s.Add("L3 cache", SensorFormat.Bytes(l3 * 1024));
            }
        }
        if (sockets > 1) s.Add("Sockets", sockets.ToString());
        s.Add("Cores / threads", $"{cores} / {threads}");
    }

    private sealed record Module(string Slot, ulong Bytes, string Type, int RunningMTs, int? RatedMTs, string? Maker, string? Part);

    private static List<Module> ReadModules()
    {
        var list = new List<Module>();
        int n = 0;
        foreach (var mo in Query("SELECT BankLabel, DeviceLocator, Capacity, Manufacturer, PartNumber, Speed, ConfiguredClockSpeed, SMBIOSMemoryType FROM Win32_PhysicalMemory"))
        {
            n++;
            string slot = Str(mo, "DeviceLocator") ?? Str(mo, "BankLabel") ?? $"Slot {n}";
            int running = (int)U64(mo, "ConfiguredClockSpeed");
            if (running == 0) running = (int)U64(mo, "Speed");
            string? part = Clean(Str(mo, "PartNumber"));
            list.Add(new Module(slot, U64(mo, "Capacity"), MemoryType((int)U64(mo, "SMBIOSMemoryType")),
                running, RatedSpeedFromPartNumber(part), MakerName(Clean(Str(mo, "Manufacturer"))), part));
        }
        return list;
    }

    private static void Memory(InfoSection s)
    {
        var modules = ReadModules();
        ulong total = 0;
        foreach (var m in modules)
        {
            total += m.Bytes;
            string rated = m.RatedMTs is int r && r != m.RunningMTs ? $"  ·  rated {r} MT/s" : "";
            s.Add(m.Slot, $"{SensorFormat.Bytes(m.Bytes)} {m.Type}  ·  running {m.RunningMTs} MT/s{rated}  ·  {m.Maker} {m.Part}".Trim(' ', '·'));
        }
        s.Insert(0, "Installed", $"{SensorFormat.Bytes(total)} in {modules.Count} module{(modules.Count == 1 ? "" : "s")}");
        if (FindMemorySpeedIssue(modules) is { } issue) s.Insert(1, "Speed", issue.Details, warning: true);
    }

    /// <summary>
    /// A suggestion when memory runs well below the speed its part number says it's rated for
    /// (usually XMP/EXPO switched off in the BIOS). Null when it's fine or the rating is unknown.
    /// </summary>
    public static MemorySpeedIssue? CheckMemorySpeed()
    {
        try { return FindMemorySpeedIssue(ReadModules()); }
        catch (Exception ex) { Log.Error("Memory speed check failed", ex); return null; }
    }

    private static MemorySpeedIssue? FindMemorySpeedIssue(List<Module> modules)
    {
        var rated = modules.Where(m => m.RatedMTs.HasValue && m.RunningMTs > 0).ToList();
        if (rated.Count == 0) return null;
        int running = rated.Max(m => m.RunningMTs);
        int ratedFor = rated.Min(m => m.RatedMTs!.Value);   // a mixed kit only runs as fast as its slowest stick
        if (ratedFor - running < 200) return null;
        return new MemorySpeedIssue(running, ratedFor, rated.Any(m => m.Type == "DDR5") ? "XMP or EXPO" : "XMP");
    }

    /// <summary>
    /// Rated (XMP/EXPO) speed encoded in common enthusiast part numbers. Windows only reports the
    /// running and JEDEC speeds, so this is the only rating available without reading the SPD chip.
    /// </summary>
    internal static int? RatedSpeedFromPartNumber(string? part)
    {
        if (string.IsNullOrWhiteSpace(part)) return null;
        part = part.Trim().ToUpperInvariant();
        int? speed = null;

        // G.Skill: F4-3600C19-8GVRB -> 3600, F5-6000J3038F16G -> 6000
        var m = Regex.Match(part, @"^F[345]-(\d{4})");
        if (m.Success) speed = int.Parse(m.Groups[1].Value);

        // Kingston FURY: KF436C17BBK2/16 -> 3600, KF560C40… -> 6000
        if (speed == null && (m = Regex.Match(part, @"^KF[45](\d{2})C\d{2}")).Success)
            speed = int.Parse(m.Groups[1].Value) * 100;

        // Crucial Ballistix / Pro: BL2K16G36C16U4B -> 3600, CP2K16G56C46U5 -> 5600
        if (speed == null && (m = Regex.Match(part, @"^(BL|CP)\w*?G(\d{2})C\d{2}")).Success)
            speed = int.Parse(m.Groups[2].Value) * 100;

        // G.Skill F4-3600C19…, Corsair …M2B3200C16, HyperX KHX3200C16…, TeamGroup …3600HC18…
        if (speed == null && (m = Regex.Match(part, @"(?<!\d)(\d{4})H?C\d{2}")).Success)
            speed = int.Parse(m.Groups[1].Value);

        return speed is >= 1600 and <= 10000 ? speed : null;
    }

    /// <summary>JEDEC manufacturer codes as Windows reports them, for the common brands.</summary>
    private static string? MakerName(string? code) => code?.ToUpperInvariant() switch
    {
        null => null,
        "04CD" => "G.Skill",
        "029E" => "Corsair",
        "0198" => "Kingston",
        "859B" => "Crucial",
        "80CE" or "CE00" => "Samsung",
        "80AD" or "AD00" => "SK hynix",
        "802C" or "2C00" => "Micron",
        "04CB" => "ADATA",
        "04EF" => "TeamGroup",
        _ => code,
    };

    private static string MemoryType(int smbios) => smbios switch
    {
        20 => "DDR",
        21 => "DDR2",
        24 => "DDR3",
        26 => "DDR4",
        27 => "LPDDR",
        28 => "LPDDR2",
        29 => "LPDDR3",
        30 => "LPDDR4",
        34 => "DDR5",
        35 => "LPDDR5",
        _ => "",
    };

    private static void Graphics(InfoSection s)
    {
        var vram = ReadVramFromRegistry();
        int i = 0;
        foreach (var mo in Query("SELECT Name, DriverVersion, DriverDate, AdapterRAM, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController"))
        {
            i++;
            string name = Str(mo, "Name") ?? $"Adapter {i}";
            string prefix = i == 1 ? "" : $"#{i} ";
            s.Add(prefix + "Adapter", name);

            ulong mem = vram.TryGetValue(name, out var regMem) ? regMem : U64(mo, "AdapterRAM");
            if (mem > 0) s.Add(prefix + "Memory", SensorFormat.Bytes(mem));

            var dd = Str(mo, "DriverDate");
            string? date = dd != null ? ManagementDateTimeConverter.ToDateTime(dd).ToString("d") : null;
            s.Add(prefix + "Driver", Str(mo, "DriverVersion") + (date != null ? $"  ({date})" : ""));

            ulong w = U64(mo, "CurrentHorizontalResolution"), h = U64(mo, "CurrentVerticalResolution"), hz = U64(mo, "CurrentRefreshRate");
            if (w > 0) s.Add(prefix + "Mode", $"{w} × {h} @ {hz} Hz");
        }
    }

    /// <summary>Accurate dedicated VRAM per adapter (64-bit value written by the display driver).</summary>
    private static Dictionary<string, ulong> ReadVramFromRegistry()
    {
        var result = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cls == null) return result;
            foreach (var sub in cls.GetSubKeyNames())
            {
                if (sub.Length != 4 || !char.IsDigit(sub[0])) continue;
                using var k = cls.OpenSubKey(sub);
                var desc = k?.GetValue("DriverDesc")?.ToString();
                if (k == null || desc == null) continue;

                ulong bytes = k.GetValue("HardwareInformation.qwMemorySize") switch
                {
                    long l => (ulong)l,
                    byte[] b when b.Length >= 8 => BitConverter.ToUInt64(b, 0),
                    _ => 0,
                };
                if (bytes == 0)
                {
                    bytes = k.GetValue("HardwareInformation.MemorySize") switch
                    {
                        int n => (uint)n,
                        byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0),
                        _ => 0,
                    };
                }
                if (bytes > 0 && (!result.TryGetValue(desc, out var existing) || bytes > existing))
                    result[desc] = bytes;
            }
        }
        catch (Exception ex)
        {
            Log.Error("VRAM registry read failed", ex);
        }
        return result;
    }

    private static void Storage(InfoSection s)
    {
        int i = 0;
        foreach (var mo in Query("SELECT FriendlyName, MediaType, BusType, Size FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage"))
        {
            i++;
            string media = U64(mo, "MediaType") switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "" };
            string bus = U64(mo, "BusType") switch
            {
                1 => "SCSI", 3 => "ATA", 6 => "Fibre Channel", 7 => "USB", 8 => "RAID", 10 => "SAS",
                11 => "SATA", 12 => "SD", 13 => "MMC", 15 => "File-backed", 16 => "Storage Spaces", 17 => "NVMe",
                _ => "",
            };
            s.Add($"Disk {i}", $"{Str(mo, "FriendlyName")}  {SensorFormat.Bytes(U64(mo, "Size"))}  {bus} {media}".Trim());
        }
    }

    private static void Volumes(InfoSection s)
    {
        foreach (var d in DriveInfo.GetDrives())
        {
            if (!d.IsReady || d.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
            double usedPct = d.TotalSize > 0 ? 100.0 * (d.TotalSize - d.TotalFreeSpace) / d.TotalSize : 0;
            string label = string.IsNullOrEmpty(d.VolumeLabel) ? "" : $"{d.VolumeLabel}, ";
            s.Add(d.Name.TrimEnd('\\'),
                $"{label}{d.DriveFormat}  {SensorFormat.Bytes(d.TotalFreeSpace)} free of {SensorFormat.Bytes(d.TotalSize)}  ({usedPct:0}% used)");
        }
    }

    private static void Network(InfoSection s)
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            string ip = "";
            try
            {
                ip = string.Join(", ", nic.GetIPProperties().UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString()));
            }
            catch { }

            string speed = nic.Speed > 0
                ? nic.Speed >= 1_000_000_000 ? $"{nic.Speed / 1e9:0.#} Gb/s" : $"{nic.Speed / 1e6:0} Mb/s"
                : "";
            s.Add(nic.Name, $"{nic.Description}  {speed}  {ip}".Trim());
        }
    }

    private static void Displays(InfoSection s)
    {
        int i = 0;
        foreach (var sc in Screen.AllScreens)
        {
            i++;
            s.Add($"Display {i}", $"{sc.Bounds.Width} × {sc.Bounds.Height}  at ({sc.Bounds.X}, {sc.Bounds.Y})" +
                                 $"  {Native.NativeMethods.GetScreenDpi(sc) / 96f * 100:0}% scale{(sc.Primary ? "  · primary" : "")}");
        }
    }

    private static string Join(params string?[] parts) =>
        string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));

    /// <summary>OEMs fill unused SMBIOS strings with placeholders; hide those.</summary>
    private static string? Clean(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        string t = v.Trim();
        string[] junk = { "To Be Filled By O.E.M.", "Default string", "Not Applicable", "None", "Unknown", "x.x", "Undefined" };
        return junk.Any(j => t.Equals(j, StringComparison.OrdinalIgnoreCase)) || t.All(c => c == '0') ? null : t;
    }
}
