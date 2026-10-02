using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;
using Plugin.LocalNotification.Core.Models.AndroidOption;

namespace InCharge.Mobile.Services;

public class NotificationService(AppSettingsService appSettings)
{
    private int _lastNotificationId;

    public async Task<bool> AreNotificationsEnabledAsync()
    {
        await WaitUntilReadyAsync();
        return LocalNotificationCenter.Current is not null && await LocalNotificationCenter.Current.AreNotificationsEnabled();
    }

    public async Task<bool> RequestPermissionAsync()
    {
        await WaitUntilReadyAsync();
        return LocalNotificationCenter.Current is not null && await LocalNotificationCenter.Current.RequestNotificationPermission();
    }

    public async Task NotifyAsync(string title, string body)
    {
        await WaitUntilReadyAsync();
        if (LocalNotificationCenter.Current is null)
        {
            return;
        }

        await LocalNotificationCenter.Current.Show(new NotificationRequest
        {
            NotificationId = NextId(),
            Title = title,
            Description = body,
            ReturningData = "incharge"
        });
    }

    // A new phase gets a fresh notification id: that is what makes the phone buzz and the
    // watch relay the cue. The per-second updates below reuse the id silently.
    public async Task StartPhaseAsync(string heading, string detail, int remainingSeconds, int totalSeconds)
    {
        await WaitUntilReadyAsync();
        if (LocalNotificationCenter.Current is null)
        {
            return;
        }

        await LocalNotificationCenter.Current.Show(BuildTimer(NextId(), $"{heading}  {Format(remainingSeconds)}", detail, remainingSeconds, totalSeconds));
    }

    public async Task UpdateTimerAsync(string heading, string detail, int remainingSeconds, int totalSeconds)
    {
        await WaitUntilReadyAsync();
        if (LocalNotificationCenter.Current is null || _lastNotificationId == 0)
        {
            return;
        }

        await LocalNotificationCenter.Current.Show(BuildTimer(_lastNotificationId, $"{heading}  {Format(remainingSeconds)}", detail, remainingSeconds, totalSeconds));
    }

    public async Task ShowPausedAsync(string heading, int remainingSeconds, int totalSeconds)
    {
        await WaitUntilReadyAsync();
        if (LocalNotificationCenter.Current is null || _lastNotificationId == 0)
        {
            return;
        }

        await LocalNotificationCenter.Current.Show(BuildTimer(_lastNotificationId, $"Paused  {Format(remainingSeconds)}", heading, remainingSeconds, totalSeconds));
    }

    public async Task ClearTimerAsync()
    {
        await WaitUntilReadyAsync();
        if (LocalNotificationCenter.Current is null || _lastNotificationId == 0)
        {
            return;
        }

        LocalNotificationCenter.Current.Cancel(_lastNotificationId);
    }

    private int NextId()
    {
        if (appSettings.Load().OnlyLatestNotification && _lastNotificationId > 0)
        {
            LocalNotificationCenter.Current?.Cancel(_lastNotificationId);
        }
        return ++_lastNotificationId;
    }

    // Deliberately not Ongoing: watch bridges such as the Fitbit app usually skip ongoing
    // notifications, which would silence the phase cues.
    private static NotificationRequest BuildTimer(int id, string title, string detail, int remainingSeconds, int totalSeconds)
    {
        var remaining = Math.Max(0, remainingSeconds);
        return new NotificationRequest
        {
            NotificationId = id,
            Title = title,
            Description = detail,
            ReturningData = "incharge",
            Android =
            {
                AutoCancel = false,
                OnlyAlertOnce = true,
                ProgressBar = new AndroidProgressBar
                {
                    Max = Math.Max(1, totalSeconds),
                    Progress = Math.Max(0, totalSeconds - remaining)
                }
            }
        };
    }

    private static string Format(int seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"m\:ss");

    // LocalNotificationCenter.Current is populated by platform lifecycle events wired up
    // through UseLocalNotification() and can still be null for a brief moment while the
    // app is starting up - calling into it too early crashed the app on launch.
    private static async Task WaitUntilReadyAsync()
    {
        for (var i = 0; i < 40 && LocalNotificationCenter.Current is null; i++)
        {
            await Task.Delay(50);
        }
    }
}
