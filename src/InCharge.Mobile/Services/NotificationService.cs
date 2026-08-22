using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;

namespace InCharge.Mobile.Services;

public class NotificationService
{
    private int _lastNotificationId;

    public Task<bool> AreNotificationsEnabledAsync() => LocalNotificationCenter.Current.AreNotificationsEnabled();

    public Task<bool> RequestPermissionAsync() => LocalNotificationCenter.Current.RequestNotificationPermission();

    public Task NotifyAsync(string title, string body)
    {
        var request = new NotificationRequest
        {
            NotificationId = ++_lastNotificationId,
            Title = title,
            Description = body,
            ReturningData = "incharge"
        };

        return LocalNotificationCenter.Current.Show(request);
    }
}
