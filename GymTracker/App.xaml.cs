namespace GymTracker;

public partial class App : Application
{
    public static IServiceProvider? Services { get; internal set; }

	public App()
	{
		InitializeComponent();

		MainPage = new AppShell();
	}
}
