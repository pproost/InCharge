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
