using System.Text.Json;

namespace InCharge.Mobile.Services;

public class AppSettings
{
    public bool ShowDurationOnComplete { get; set; } = true;
    public bool OnlyLatestNotification { get; set; } = true;
    public bool AutoSaveToLog { get; set; } = true;
}

public class AppSettingsService
{
    private const string Key = "incharge.appSettings";
    private AppSettings? _cached;

    public AppSettings Load()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var json = Preferences.Default.Get(Key, string.Empty);
        if (string.IsNullOrEmpty(json))
        {
            _cached = new AppSettings();
            return _cached;
        }

        try
        {
            _cached = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (JsonException)
        {
            _cached = new AppSettings();
        }

        return _cached;
    }

    public void Save(AppSettings settings)
    {
        _cached = settings;
        Preferences.Default.Set(Key, JsonSerializer.Serialize(settings));
    }
}
