using System.Text.Json;
using System.Text.Json.Serialization;

namespace InCharge.Mobile.Services;

public class ActivityLogEntry
{
    public const string BasicSource = "basic";
    public const string AdvancedSource = "advanced";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public bool IsAdvanced { get; set; }

    // "basic", "advanced" or "coach"; null on entries logged before contract v1.
    public string? Source { get; set; }
    public string? SessionId { get; set; }
    public string? PlanId { get; set; }
    public string? TypeId { get; set; }
    public bool FinishedEarly { get; set; }

    [JsonConverter(typeof(LenientDateTimeOffsetConverter))]
    public DateTimeOffset StartedAt { get; set; }

    [JsonConverter(typeof(LenientDateTimeOffsetConverter))]
    public DateTimeOffset CompletedAt { get; set; }

    // Last local change (creation, type change); lets the server ignore out-of-order uploads.
    [JsonConverter(typeof(LenientDateTimeOffsetConverter))]
    public DateTimeOffset UpdatedAt { get; set; }

    // The structure as planned when the run started; null on entries logged before contract v1.
    public SessionStructure? Structure { get; set; }

    public int MoveSeconds { get; set; }
    public int RestSeconds { get; set; }
    public int WarmupSeconds { get; set; }
    public int CooldownSeconds { get; set; }
    public int MoveBlocks { get; set; }
    public int PauseCount { get; set; }
    public int PausedSeconds { get; set; }

    public int ActiveSeconds => MoveSeconds + RestSeconds + WarmupSeconds + CooldownSeconds;

    public string EffectiveSource => Source ?? (IsAdvanced ? AdvancedSource : BasicSource);

    public DateTimeOffset EffectiveUpdatedAt => UpdatedAt == default ? CompletedAt : UpdatedAt;
}

public class ActivityLogService
{
    private const string Key = "incharge.activityLog";
    private List<ActivityLogEntry>? _cached;

    // Raised after an entry was added, changed (deleted = false) or removed (deleted = true),
    // with the time of that change. The coach sync queues it for upload.
    public event Action<string, bool, DateTimeOffset>? EntryChanged;

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
        EntryChanged?.Invoke(entry.Id, false, entry.EffectiveUpdatedAt);
    }

    // Entries are edited in place (e.g. their type); this stamps the change and persists the list.
    public void SaveChanges(ActivityLogEntry changed)
    {
        changed.UpdatedAt = DateTimeOffset.Now;
        Save(LoadAll());
        EntryChanged?.Invoke(changed.Id, false, changed.UpdatedAt);
    }

    // A type was renamed, re-iconed or deleted: the logged sessions carry its name and icon to
    // the server, so they are stamped as changed and uploaded again.
    public void TouchType(string typeId)
    {
        var now = DateTimeOffset.Now;
        var touched = LoadAll().Where(e => e.TypeId == typeId).ToList();
        if (touched.Count == 0)
        {
            return;
        }

        foreach (var entry in touched)
        {
            entry.UpdatedAt = now;
        }
        Save(LoadAll());
        foreach (var entry in touched)
        {
            EntryChanged?.Invoke(entry.Id, false, now);
        }
    }

    public void Delete(string id)
    {
        var all = LoadAll();
        if (all.RemoveAll(e => e.Id == id) > 0)
        {
            Save(all);
            EntryChanged?.Invoke(id, true, DateTimeOffset.Now);
        }
    }

    private void Save(List<ActivityLogEntry> all)
    {
        _cached = all;
        Preferences.Default.Set(Key, JsonSerializer.Serialize(all));
    }
}

// Tracks one run (what was planned, start time and pauses) so both interval pages log an
// activity the same way.
public class ActivityRecorder
{
    private string _name = "";
    private string _source = ActivityLogEntry.BasicSource;
    private string? _sessionId;
    private string? _planId;
    private SessionStructure? _structure;
    private DateTimeOffset _startedAt;
    private DateTimeOffset? _pausedAt;
    private int _pauseCount;
    private double _pausedSeconds;

    public void Start(string name, string source, SessionStructure structure, string? sessionId = null, string? planId = null)
    {
        _name = name;
        _source = source;
        _structure = structure;
        _sessionId = sessionId;
        _planId = planId;
        _startedAt = DateTimeOffset.Now;
        _pausedAt = null;
        _pauseCount = 0;
        _pausedSeconds = 0;
    }

    public void Pause()
    {
        _pausedAt = DateTimeOffset.Now;
        _pauseCount++;
    }

    public void Resume()
    {
        if (_pausedAt is not null)
        {
            _pausedSeconds += (DateTimeOffset.Now - _pausedAt.Value).TotalSeconds;
            _pausedAt = null;
        }
    }

    // "phases" is what was actually done: for an early finish that includes the partial current phase.
    public ActivityLogEntry Complete(IEnumerable<(string Kind, int Seconds)> phases, bool finishedEarly = false)
    {
        Resume();
        var list = phases.ToList();
        int Sum(string kind) => list.Where(p => p.Kind == kind).Sum(p => p.Seconds);
        var now = DateTimeOffset.Now;
        return new ActivityLogEntry
        {
            Name = _name,
            Source = _source,
            IsAdvanced = _source != ActivityLogEntry.BasicSource,
            SessionId = _sessionId,
            PlanId = _planId,
            Structure = _structure,
            FinishedEarly = finishedEarly,
            StartedAt = _startedAt,
            CompletedAt = now,
            UpdatedAt = now,
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
