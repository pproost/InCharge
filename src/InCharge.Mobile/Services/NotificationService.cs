using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;

namespace InCharge.Mobile.Services;

public class NotificationService
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

        var request = new NotificationRequest
        {
            NotificationId = ++_lastNotificationId,
            Title = title,
            Description = body,
            ReturningData = "incharge"
        };

        await LocalNotificationCenter.Current.Show(request);
    }

    // Shows (or updates in place, since it always reuses progressNotificationId) an ongoing
    // notification that uses Android's native chronometer view to count down "remaining"
    // on its own, so the notification shade shows a live timer without us re-posting every second.
    public async Task ShowProgressAsync(int progressNotificationId, string title, string body, TimeSpan remaining)
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
                When = DateTimeOffset.Now.Add(remaining),
                UsesChronometer = true,
                ChronometerCountDown = true
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
