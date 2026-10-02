using System.Text.Json;

namespace InCharge.Mobile.Services;

public class ActivityLogEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public bool IsAdvanced { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public int MoveSeconds { get; set; }
    public int RestSeconds { get; set; }
    public int WarmupSeconds { get; set; }
    public int CooldownSeconds { get; set; }
    public int MoveBlocks { get; set; }
    public int PauseCount { get; set; }
    public int PausedSeconds { get; set; }

    public int ActiveSeconds => MoveSeconds + RestSeconds + WarmupSeconds + CooldownSeconds;
}

public class ActivityLogService
{
    private const string Key = "incharge.activityLog";
    private List<ActivityLogEntry>? _cached;

    public List<ActivityLogEntry> LoadAll()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var json = Preferences.Default.Get(Key, string.Empty);
        try
        {
            _cached = string.IsNullOrEmpty(json)
                ? new List<ActivityLogEntry>()
                : JsonSerializer.Deserialize<List<ActivityLogEntry>>(json) ?? new List<ActivityLogEntry>();
        }
        catch (JsonException)
        {
            _cached = new List<ActivityLogEntry>();
        }

        return _cached;
    }

    public void Add(ActivityLogEntry entry)
    {
        var all = LoadAll();
        all.Add(entry);
        Save(all);
    }

    public void Delete(string id)
    {
        var all = LoadAll();
        all.RemoveAll(e => e.Id == id);
        Save(all);
    }

    private void Save(List<ActivityLogEntry> all)
    {
        _cached = all;
        Preferences.Default.Set(Key, JsonSerializer.Serialize(all));
    }
}

// Tracks one run (start time and pauses) so both interval pages log a completed
// activity the same way.
public class ActivityRecorder
{
    private DateTime _startedAt;
    private DateTime? _pausedAt;
    private int _pauseCount;
    private double _pausedSeconds;

    public void Start()
    {
        _startedAt = DateTime.Now;
        _pausedAt = null;
        _pauseCount = 0;
        _pausedSeconds = 0;
    }

    public void Pause()
    {
        _pausedAt = DateTime.Now;
        _pauseCount++;
    }

    public void Resume()
    {
        if (_pausedAt is not null)
        {
            _pausedSeconds += (DateTime.Now - _pausedAt.Value).TotalSeconds;
            _pausedAt = null;
        }
    }

    public ActivityLogEntry Complete(string name, bool isAdvanced, IEnumerable<(string Kind, int Seconds)> phases)
    {
        var list = phases.ToList();
        int Sum(string kind) => list.Where(p => p.Kind == kind).Sum(p => p.Seconds);
        return new ActivityLogEntry
        {
            Name = name,
            IsAdvanced = isAdvanced,
            StartedAt = _startedAt,
            CompletedAt = DateTime.Now,
            MoveSeconds = Sum("Move"),
            RestSeconds = Sum("Rest"),
            WarmupSeconds = Sum("Warmup"),
            CooldownSeconds = Sum("Cooldown"),
            MoveBlocks = list.Count(p => p.Kind == "Move"),
            PauseCount = _pauseCount,
            PausedSeconds = (int)Math.Round(_pausedSeconds)
        };
    }
}
