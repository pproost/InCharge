using InCharge.Mobile.Services;
using Microsoft.Extensions.Logging;
using Plugin.LocalNotification;

namespace InCharge.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseLocalNotification()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();
		builder.Services.AddSingleton<NotificationService>();
		builder.Services.AddSingleton<AppSettingsService>();
		builder.Services.AddSingleton<AdvancedSessionService>();
		builder.Services.AddSingleton<ActivityLogService>();
		builder.Services.AddSingleton<ActivityTypeService>();
		builder.Services.AddSingleton<CoachSyncService>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
