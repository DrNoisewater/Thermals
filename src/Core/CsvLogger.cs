using System.Text;

namespace Thermals.Core;

/// <summary>
/// Writes every poll to a CSV file (one column per sensor). Called on the poll thread.
/// A new file is started automatically if the set of sensors changes.
/// </summary>
public sealed class CsvLogger : IDisposable
{
    private readonly object _gate = new();
    private StreamWriter? _writer;
    private List<string>? _columns;
    private int _headerVersion = -1;
    private string _directory = AppPaths.DefaultLogDir;
    private int _unflushed;
    private volatile bool _running;

    public bool IsRunning => _running;
    public string? CurrentFile { get; private set; }

    public event Action<string>? Failed;

    public void Start(string directory)
    {
        lock (_gate)
        {
            CloseWriter();
            _directory = directory;
            _headerVersion = -1;
            _running = true;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _running = false;
            CloseWriter();
        }
    }

    public void Write(SensorSnapshot s)
    {
        if (!_running) return;
        lock (_gate)
        {
            if (!_running) return;
            try
            {
                if (_writer == null || s.StructureVersion != _headerVersion)
                    OpenNewFile(s);

                var sb = new StringBuilder(_columns!.Count * 8);
                sb.Append(s.Time.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                foreach (var id in _columns)
                {
                    sb.Append(',');
                    if (s.ById.TryGetValue(id, out var r)) sb.Append(SensorFormat.Raw(r.Type, r.Value));
                }
                _writer!.WriteLine(sb.ToString());

                if (++_unflushed >= 10)
                {
                    _writer.Flush();
                    _unflushed = 0;
                }
            }
            catch (Exception ex)
            {
                Log.Error("CSV logging failed", ex);
                _running = false;
                CloseWriter();
                Failed?.Invoke(ex.Message);
            }
        }
    }

    private void OpenNewFile(SensorSnapshot s)
    {
        CloseWriter();
        Directory.CreateDirectory(_directory);
        CurrentFile = Path.Combine(_directory, $"Thermals_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        _writer = new StreamWriter(CurrentFile, false, new UTF8Encoding(true), 64 * 1024);
        _columns = new List<string>(s.All.Count);

        var header = new StringBuilder("Time");
        foreach (var node in s.Hardware)
        {
            foreach (var r in node.Sensors)
            {
                _columns.Add(r.Id);
                string unit = SensorFormat.Unit(r.Type);
                header.Append(',').Append(Quote($"{node.DisplayName} / {r.Name}" + (unit.Length > 0 ? $" [{unit}]" : "")));
            }
        }
        _writer.WriteLine(header.ToString());
        _headerVersion = s.StructureVersion;
    }

    private void CloseWriter()
    {
        try { _writer?.Dispose(); } catch { }
        _writer = null;
        _unflushed = 0;
    }

    public static string Quote(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

    public void Dispose() => Stop();
}
