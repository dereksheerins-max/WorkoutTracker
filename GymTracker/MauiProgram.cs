using Microsoft.Extensions.Logging;
using GymTracker.Services; // Make sure this matches your namespace!
using GymTracker.ViewModels;
using GymTracker.Views;

namespace GymTracker;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		// Add your services here:
		builder.Services.AddSingleton<DatabaseService>();
		builder.Services.AddTransient<AddWorkoutViewModel>();
		builder.Services.AddTransient<AddWorkoutPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		var mauiApp = builder.Build();
		// expose the DI container for app-wide resolution
		App.Services = mauiApp.Services;

		return mauiApp;
	}
}
