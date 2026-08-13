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
			var logger = sp.GetService<ILogger<WorkoutDatabase>>();
			var db = new WorkoutDatabase(dbPath, logger);
			// Initialize synchronously during startup; use an explicit async initializer to avoid .Wait() in ctor
			db.InitializeAsync().GetAwaiter().GetResult();
			return db;
		});

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