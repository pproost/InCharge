using System.Text.Json;

namespace InCharge.Mobile.Services;

public class IntervalBlock
{
    public bool IsMove { get; set; } = true;
    public int DurationSeconds { get; set; } = 60;
}

public class AdvancedSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public bool UseMinutes { get; set; } = true;
    public List<IntervalBlock> Blocks { get; set; } = new();
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
            return JsonSerializer.Deserialize<List<AdvancedSession>>(json) ?? new List<AdvancedSession>();
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
}
