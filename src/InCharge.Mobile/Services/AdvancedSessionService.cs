using System.Text.Json;

namespace InCharge.Mobile.Services;

public class IntervalBlock
{
    public bool IsMove { get; set; } = true;
    public int DurationSeconds { get; set; } = 60;
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
    public List<IntervalGroup> Groups { get; set; } = new();
}

public class AdvancedSessionService
{
    private const string Key = "incharge.advancedSessions";

    public List<AdvancedSession> LoadAll()
    {
        var json = Preferences.Default.Get(Key, string.Empty);
        if (string.IsNullOrEmpty(json))
        {
            return new List<AdvancedSession>();
        }

        try
        {
            var stored = JsonSerializer.Deserialize<List<StoredSession>>(json) ?? new List<StoredSession>();
            return stored.Select(ToSession).ToList();
        }
        catch (JsonException)
        {
            return new List<AdvancedSession>();
        }
    }

    public void SaveAll(List<AdvancedSession> sessions)
    {
        Preferences.Default.Set(Key, JsonSerializer.Serialize(sessions));
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
        public List<IntervalGroup>? Groups { get; set; }
        public List<IntervalBlock>? Blocks { get; set; }
    }
}
