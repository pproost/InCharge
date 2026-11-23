using System.Text.Json;

namespace InCharge.Mobile.Services;

public class IntervalBlock
{
    // Has to stay readable next to the phase on a Charge 6 (see docs/contract.md).
    public const int MaxLabelLength = 20;

    public bool IsMove { get; set; } = true;
    public int DurationSeconds { get; set; } = 60;
    public string? Label { get; set; }

    public static string? CleanLabel(string? label)
    {
        var trimmed = label?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }
        return trimmed.Length > MaxLabelLength ? trimmed[..MaxLabelLength] : trimmed;
    }
}

public class IntervalGroup
{
    public int RepeatCount { get; set; } = 1;
    public List<IntervalBlock> Blocks { get; set; } = new();
}

public class AdvancedSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public bool UseMinutes { get; set; } = true;
    public bool WarmupEnabled { get; set; }
    public int WarmupDurationSeconds { get; set; } = 300;
    public bool CooldownEnabled { get; set; }
    public int CooldownDurationSeconds { get; set; } = 300;
    public string? TypeId { get; set; }
    public string? Source { get; set; }
    public string? PlanId { get; set; }
    public DateOnly? PlannedDate { get; set; }
    public string? Note { get; set; }
    public List<IntervalGroup> Groups { get; set; } = new();

    public const string CoachSource = "coach";
    public bool IsCoachPlan => Source == CoachSource;
}

public class AdvancedSessionService
{
    private const string Key = "incharge.advancedSessions";

    // One shared list: the Advanced page edits it and the coach sync adds imported plans to it,
    // so neither can overwrite the other with a stale copy.
    private List<AdvancedSession>? _cached;

    // Raised (on the main thread) after coach plans were added.
    public event Action? SessionsImported;

    public List<AdvancedSession> LoadAll()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var json = Preferences.Default.Get(Key, string.Empty);
        try
        {
            _cached = string.IsNullOrEmpty(json)
                ? new List<AdvancedSession>()
                : (JsonSerializer.Deserialize<List<StoredSession>>(json) ?? new List<StoredSession>()).Select(ToSession).ToList();
        }
        catch (JsonException)
        {
            _cached = new List<AdvancedSession>();
        }

        return _cached;
    }

    public void SaveAll(List<AdvancedSession> sessions)
    {
        _cached = sessions;
        Preferences.Default.Set(Key, JsonSerializer.Serialize(sessions));
    }

    public bool HasPlan(string planId) => LoadAll().Any(s => s.PlanId == planId);

    public void AddImported(IEnumerable<AdvancedSession> imported)
    {
        var all = LoadAll();
        all.AddRange(imported);
        SaveAll(all);
        SessionsImported?.Invoke();
    }

    // Older saved sessions stored a flat "Blocks" list with no grouping/warmup/cooldown.
    // Reading that shape here (instead of losing it to a schema mismatch) turns each one
    // into a single, once-through group so existing sessions keep working after the update.
    private static AdvancedSession ToSession(StoredSession stored)
    {
        var groups = stored.Groups is { Count: > 0 }
            ? stored.Groups
            : stored.Blocks is { Count: > 0 }
                ? new List<IntervalGroup> { new() { RepeatCount = 1, Blocks = stored.Blocks } }
                : new List<IntervalGroup>();

        return new AdvancedSession
        {
            Id = stored.Id,
            Name = stored.Name,
            UseMinutes = stored.UseMinutes,
            WarmupEnabled = stored.WarmupEnabled,
            WarmupDurationSeconds = stored.WarmupDurationSeconds,
            CooldownEnabled = stored.CooldownEnabled,
            CooldownDurationSeconds = stored.CooldownDurationSeconds,
            TypeId = stored.TypeId,
            Source = stored.Source,
            PlanId = stored.PlanId,
            PlannedDate = stored.PlannedDate,
            Note = stored.Note,
            Groups = groups
        };
    }

    private class StoredSession
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public bool UseMinutes { get; set; } = true;
        public bool WarmupEnabled { get; set; }
        public int WarmupDurationSeconds { get; set; } = 300;
        public bool CooldownEnabled { get; set; }
        public int CooldownDurationSeconds { get; set; } = 300;
        public string? TypeId { get; set; }
        public string? Source { get; set; }
        public string? PlanId { get; set; }
        public DateOnly? PlannedDate { get; set; }
        public string? Note { get; set; }
        public List<IntervalGroup>? Groups { get; set; }
        public List<IntervalBlock>? Blocks { get; set; }
    }
}
