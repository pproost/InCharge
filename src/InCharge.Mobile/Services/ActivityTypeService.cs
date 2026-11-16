using System.Text.Json;

namespace InCharge.Mobile.Services;

public class ActivityType
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "🏃";
}

public class ActivityTypeService
{
    private const string Key = "incharge.activityTypes";

    public static readonly string[] Icons = ["🚴", "🏃", "🚶", "🥾", "🚣", "🏊", "⛷️", "🏋️", "🧘", "⚽", "🎾", "❤️"];

    private List<ActivityType>? _cached;

    public List<ActivityType> LoadAll()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var json = Preferences.Default.Get(Key, string.Empty);
        if (string.IsNullOrEmpty(json))
        {
            // Seeded only the very first time; an emptied list stays empty.
            _cached =
            [
                new ActivityType { Name = "Cycling", Icon = "🚴" },
                new ActivityType { Name = "Running", Icon = "🏃" },
                new ActivityType { Name = "Walking", Icon = "🚶" }
            ];
            SaveAll(_cached);
            return _cached;
        }

        try
        {
            _cached = JsonSerializer.Deserialize<List<ActivityType>>(json) ?? new List<ActivityType>();
        }
        catch (JsonException)
        {
            _cached = new List<ActivityType>();
        }

        return _cached;
    }

    public void SaveAll(List<ActivityType> types)
    {
        _cached = types;
        Preferences.Default.Set(Key, JsonSerializer.Serialize(types));
    }

    public ActivityType? Find(string? id) => id is null ? null : LoadAll().FirstOrDefault(t => t.Id == id);

    // Returns the first id that still exists, so a deleted type never ends up on a new entry.
    public string? Resolve(params string?[] candidates) => candidates.FirstOrDefault(id => Find(id) is not null);
}
