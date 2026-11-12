using Microsoft.Extensions.Logging;
using WorkoutTracker.Services;
using WorkoutTracker.ViewModels;
using WorkoutTracker.Views;

namespace WorkoutTracker;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		// Database
		builder.Services.AddSingleton<WorkoutDatabase>(sp =>
		{
			var dbPath = Path.Combine(FileSystem.AppDataDirectory, "workouts.db3");
			DatabaseFileProtection.ExcludeFromBackup(dbPath);
			var logger = sp.GetService<ILogger<WorkoutDatabase>>();
			return new WorkoutDatabase(dbPath, logger);
		});

		// App services
		builder.Services.AddSingleton<IDialogService, ShellDialogService>();
		builder.Services.AddSingleton<INavigationService, ShellNavigationService>();

		// ViewModels
		builder.Services.AddTransient<WorkoutListViewModel>();
		builder.Services.AddTransient<AddWorkoutViewModel>();

		// Pages
		builder.Services.AddTransient<WorkoutListPage>();
		builder.Services.AddTransient<AddWorkoutPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}