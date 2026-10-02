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

        // A fresh id (rather than reusing the previous one) is what makes Android buzz again
        // and relay the cue to the watch, so the old one is cancelled instead of updated.
        if (appSettings.Load().OnlyLatestNotification && _lastNotificationId > 0)
        {
            LocalNotificationCenter.Current.Cancel(_lastNotificationId);
        }

        var request = new NotificationRequest
        {
            NotificationId = ++_lastNotificationId,
            Title = title,
            Description = body,
            ReturningData = "incharge"
        };

        await LocalNotificationCenter.Current.Show(request);
    }

    // Updated in place every second (same id, silent) so the remaining time can be the
    // large title text - Android's chronometer only renders small in the header.
    public async Task ShowProgressAsync(int progressNotificationId, string heading, string detail, int remainingSeconds, int totalSeconds)
    {
        await WaitUntilReadyAsync();
        if (LocalNotificationCenter.Current is null)
        {
            return;
        }

        var remaining = Math.Max(0, remainingSeconds);
        var request = new NotificationRequest
        {
            NotificationId = progressNotificationId,
            Title = $"{heading}  {TimeSpan.FromSeconds(remaining):m\\:ss}",
            Description = detail,
            ReturningData = "incharge",
            Android =
            {
                Ongoing = true,
                AutoCancel = false,
                OnlyAlertOnce = true,
                ProgressBar = new AndroidProgressBar
                {
                    Max = Math.Max(1, totalSeconds),
                    Progress = Math.Max(0, totalSeconds - remaining)
                }
            }
        };

        await LocalNotificationCenter.Current.Show(request);
    }

    // Same ongoing notification, but frozen (no chronometer) - used while the activity is paused.
    public async Task ShowFrozenProgressAsync(int progressNotificationId, string title, string body)
    {
        await WaitUntilReadyAsync();
        if (LocalNotificationCenter.Current is null)
        {
            return;
        }

        var request = new NotificationRequest
        {
            NotificationId = progressNotificationId,
            Title = title,
            Description = body,
            ReturningData = "incharge",
            Android =
            {
                Ongoing = true,
                AutoCancel = false,
                OnlyAlertOnce = true,
                UsesChronometer = false
            }
        };

        await LocalNotificationCenter.Current.Show(request);
    }

    public async Task ClearProgressAsync(int progressNotificationId)
    {
        await WaitUntilReadyAsync();
        if (LocalNotificationCenter.Current is null)
        {
            return;
        }

        LocalNotificationCenter.Current.Cancel(progressNotificationId);
    }

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
