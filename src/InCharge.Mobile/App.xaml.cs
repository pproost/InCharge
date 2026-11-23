using InCharge.Mobile.Services;

namespace InCharge.Mobile;

public partial class App : Application
{
	private readonly CoachSyncService _coachSync;

	public App(CoachSyncService coachSync)
	{
		InitializeComponent();
		_coachSync = coachSync;
	}

	// Back in the app (e.g. after a session at the gym without signal): send what's waiting.
	protected override void OnResume()
	{
		base.OnResume();
		_coachSync.SyncInBackground();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new MainPage()) { Title = "InCharge.Mobile" };
	}
}
