using System.Collections.Concurrent;
using System.Diagnostics;
using LibreHardwareMonitor.Hardware;

namespace Thermals.Core;

public sealed class SensorReading
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required SensorType Type { get; init; }
    public required int Index { get; init; }
    public required HardwareNode Hardware { get; init; }
    public float? Value { get; init; }
    public float? Min { get; init; }
    public float? Max { get; init; }
    public float? Avg { get; init; }
}

public sealed class HardwareNode
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required HardwareType Type { get; init; }
    /// <summary>Top-level hardware this node belongs to (itself if top-level).</summary>
    public required string TopId { get; init; }
    public string? ParentName { get; init; }
    /// <summary>Time the last Update() of this device took (top-level devices only, includes sub-devices).</summary>
    public double UpdateMs { get; init; }
    /// <summary>Hard drive that is spun down; its readings are left alone so it isn't woken.</summary>
    public bool Asleep { get; init; }
    /// <summary>When this device was last actually read (drives are read less often than other hardware).</summary>
    public DateTime? LastRead { get; init; }
    public List<SensorReading> Sensors { get; } = new();

    public string DisplayName => ParentName == null ? Name : $"{ParentName}  ›  {Name}";
}

public sealed class SensorSnapshot
{
    public required IReadOnlyList<HardwareNode> Hardware { get; init; }
    public required IReadOnlyList<SensorReading> All { get; init; }
    public required IReadOnlyDictionary<string, SensorReading> ById { get; init; }
    /// <summary>Changes whenever the set of sensors changes (hardware added/removed, groups toggled).</summary>
    public required int StructureVersion { get; init; }
    public required double PollMs { get; init; }
    public required bool WasFullUpdate { get; init; }
    public required DateTime Time { get; init; }
}

/// <summary>
/// Owns LibreHardwareMonitor and polls it on one dedicated background thread.
/// LHM is not thread-safe, so every call into it happens on that thread; consumers receive
/// immutable snapshots. When only the overlay is visible, only the hardware that feeds
/// the overlay is refreshed, which keeps background cost minimal.
/// </summary>
public sealed class SensorHub : IDisposable
{
    private sealed class Stats
    {
        public float Min = float.MaxValue, Max = float.MinValue;
        public double Sum;
        public long Count;

        public void Add(float v)
        {
            if (v < Min) Min = v;
            if (v > Max) Max = v;
            Sum += v;
            Count++;
        }

        public void Reset() { Min = float.MaxValue; Max = float.MinValue; Sum = 0; Count = 0; }
    }

    private readonly Computer _computer = new();
    private readonly ConcurrentQueue<Action<Computer>> _pending = new();
    private readonly Dictionary<string, Stats> _stats = new();
    private readonly Dictionary<string, string> _sensorToTop = new();
    private readonly Dictionary<string, double> _updateMs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _lastUpdate = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _lastRead = new(StringComparer.Ordinal);
    private readonly HashSet<string> _asleep = new(StringComparer.Ordinal);
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _thread;

    private volatile bool _running;
    private volatile bool _resetStats;
    private volatile HashSet<string> _focusSensors = new();
    private int _structureHash;
    private int _structureVersion;

    public event Action<SensorSnapshot>? Updated;

    public volatile int IntervalMs = 1000;
    /// <summary>True: refresh all hardware. False: refresh only hardware feeding focus sensors.</summary>
    public volatile bool FullUpdate = true;
    /// <summary>
    /// Read drives less often: SSD/NVMe every 5 s, hard drives every 60 s.
    /// A spun-down hard drive is never read (reading SMART data would wake it), whatever this is set to.
    /// </summary>
    public volatile bool ThrottleDrives = true;
    private const long SsdIntervalMs = 5_000;
    private const long HddIntervalMs = 60_000;

    public SensorSnapshot? Latest { get; private set; }
    public string? OpenError { get; private set; }
    public bool IsOpen { get; private set; }

    public SensorHub()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "Thermals sensor poll",
            Priority = ThreadPriority.BelowNormal,
        };
    }

    public void Start()
    {
        _running = true;
        _thread.Start();
    }

    /// <summary>Run an action against the LHM Computer on the poll thread before the next poll.</summary>
    public void Enqueue(Action<Computer> action)
    {
        _pending.Enqueue(action);
        _wake.Set();
    }

    public void ApplyHardwareGroups(AppSettings s) => Enqueue(c =>
    {
        c.IsCpuEnabled = s.CpuEnabled;
        c.IsGpuEnabled = s.GpuEnabled;
        c.IsMemoryEnabled = s.MemoryEnabled;
        c.IsMotherboardEnabled = s.MotherboardEnabled;
        c.IsStorageEnabled = s.StorageEnabled;
        c.IsNetworkEnabled = s.NetworkEnabled;
        c.IsBatteryEnabled = s.BatteryEnabled;
        c.IsControllerEnabled = s.ControllerEnabled;
        c.IsPsuEnabled = s.PsuEnabled;
    });

    public void SetFocusSensors(IEnumerable<string> sensorIds)
    {
        _focusSensors = new HashSet<string>(sensorIds, StringComparer.Ordinal);
        _wake.Set();
    }

    public void ResetStats()
    {
        _resetStats = true;
        _wake.Set();
    }

    /// <summary>Poll now instead of waiting for the interval.</summary>
    public void PollNow() => _wake.Set();

    private void Run()
    {
        // Apply hardware-group settings before opening so disabled groups are never probed.
        while (_pending.TryDequeue(out var initial))
        {
            try { initial(_computer); } catch (Exception ex) { Log.Error("Hub action failed", ex); }
        }

        try
        {
            _computer.Open();
            IsOpen = true;
        }
        catch (Exception ex)
        {
            OpenError = ex.Message;
            Log.Error("Failed to open hardware monitor", ex);
        }

        while (_running)
        {
            long start = Stopwatch.GetTimestamp();
            try
            {
                Poll(start);
            }
            catch (Exception ex)
            {
                Log.Error("Poll failed", ex);
            }

            int elapsed = (int)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            int wait = Math.Max(IntervalMs - elapsed, 50);
            _wake.WaitOne(wait);
        }

        try { _computer.Close(); } catch (Exception ex) { Log.Error("Close failed", ex); }
    }

    private void Poll(long start)
    {
        bool structureMayChange = false;
        while (_pending.TryDequeue(out var action))
        {
            try { action(_computer); } catch (Exception ex) { Log.Error("Hub action failed", ex); }
            structureMayChange = true;
        }

        if (_resetStats)
        {
            _resetStats = false;
            foreach (var st in _stats.Values) st.Reset();
        }

        if (!IsOpen) return;

        bool full = FullUpdate || structureMayChange || Latest == null;
        HashSet<string>? focusTops = null;
        if (!full)
        {
            focusTops = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in _focusSensors)
                if (_sensorToTop.TryGetValue(id, out var top)) focusTops.Add(top);
        }

        var nodes = new List<HardwareNode>();
        var all = new List<SensorReading>(Latest?.All.Count ?? 256);
        var hash = new HashCode();

        foreach (var hw in _computer.Hardware)
        {
            string topId = hw.Identifier.ToString();
            bool update = full || focusTops!.Contains(topId);
            if (update && hw.HardwareType == HardwareType.Storage)
                update = ShouldReadDrive(topId);

            if (update)
            {
                long t0 = Stopwatch.GetTimestamp();
                SafeUpdate(hw);
                foreach (var sub in hw.SubHardware) SafeUpdate(sub);
                _updateMs[topId] = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                _lastRead[topId] = DateTime.Now;
            }

            _updateMs.TryGetValue(topId, out double ms);
            DateTime? lastRead = _lastRead.TryGetValue(topId, out var lr) ? lr : null;
            bool asleep = _asleep.Contains(topId);
            // A device never read yet has no real values (a drive asleep since start-up would show 0).
            bool hasData = lastRead != null || hw.HardwareType != HardwareType.Storage;
            Collect(hw, topId, null, update, hasData, ms, asleep, lastRead, nodes, all, ref hash);
            foreach (var sub in hw.SubHardware)
                Collect(sub, topId, hw.Name, update, hasData, 0, false, null, nodes, all, ref hash);
        }

        int structure = hash.ToHashCode() ^ all.Count;
        if (structure != _structureHash)
        {
            _structureHash = structure;
            _structureVersion++;
        }

        var byId = new Dictionary<string, SensorReading>(all.Count, StringComparer.Ordinal);
        foreach (var r in all) byId[r.Id] = r;

        var snapshot = new SensorSnapshot
        {
            Hardware = nodes,
            All = all,
            ById = byId,
            StructureVersion = _structureVersion,
            PollMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds,
            WasFullUpdate = full,
            Time = DateTime.Now,
        };
        Latest = snapshot;
        Updated?.Invoke(snapshot);
    }

    /// <summary>Decides whether a drive is read this poll. Never wakes a sleeping hard drive.</summary>
    private bool ShouldReadDrive(string topId)
    {
        // Identifiers look like /hdd/2, /ssd/0 or /nvme/1 (the number is the Windows disk number).
        var parts = topId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        bool known = parts.Length >= 2 && int.TryParse(parts[1], out _);
        bool isHdd = known && parts[0] == "hdd";
        int number = known ? int.Parse(parts[1]) : -1;

        long now = Environment.TickCount64;
        long interval = !ThrottleDrives ? 0 : isHdd || !known ? HddIntervalMs : SsdIntervalMs;
        if (_lastUpdate.TryGetValue(topId, out long last) && now - last < interval) return false;
        _lastUpdate[topId] = now;

        if (isHdd && Native.NativeMethods.IsDiskSpinning(number) == false)
        {
            _asleep.Add(topId);
            return false;
        }
        _asleep.Remove(topId);
        return true;
    }

    private static void SafeUpdate(IHardware hw)
    {
        try { hw.Update(); }
        catch (Exception ex) { Log.Error($"Update failed for {hw.Name}", ex); }
    }

    private void Collect(IHardware hw, string topId, string? parentName, bool updated, bool hasData, double updateMs,
        bool asleep, DateTime? lastRead, List<HardwareNode> nodes, List<SensorReading> all, ref HashCode hash)
    {
        var node = new HardwareNode
        {
            Id = hw.Identifier.ToString(),
            Name = hw.Name,
            Type = hw.HardwareType,
            TopId = topId,
            ParentName = parentName,
            UpdateMs = updateMs,
            Asleep = asleep,
            LastRead = lastRead,
        };

        foreach (var sensor in hw.Sensors)
        {
            string id = sensor.Identifier.ToString();
            hash.Add(id);
            _sensorToTop[id] = topId;

            float? value = hasData ? sensor.Value : null;
            if (!_stats.TryGetValue(id, out var st)) _stats[id] = st = new Stats();
            if (updated && value is float v && !float.IsNaN(v) && !float.IsInfinity(v)) st.Add(v);

            var reading = new SensorReading
            {
                Id = id,
                Name = sensor.Name,
                Type = sensor.SensorType,
                Index = sensor.Index,
                Hardware = node,
                Value = value,
                Min = st.Count > 0 ? st.Min : null,
                Max = st.Count > 0 ? st.Max : null,
                Avg = st.Count > 0 ? (float)(st.Sum / st.Count) : null,
            };
            node.Sensors.Add(reading);
            all.Add(reading);
        }

        node.Sensors.Sort((a, b) =>
        {
            int c = SensorFormat.Order(a.Type).CompareTo(SensorFormat.Order(b.Type));
            return c != 0 ? c : a.Index.CompareTo(b.Index);
        });

        if (node.Sensors.Count > 0) nodes.Add(node);
    }

    public void Dispose()
    {
        _running = false;
        _wake.Set();
        if (_thread.IsAlive) _thread.Join(3000);
        _wake.Dispose();
    }
}
