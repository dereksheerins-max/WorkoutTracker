using WorkoutTracker.Views;

namespace WorkoutTracker.Services;

public class ShellNavigationService : INavigationService
{
    public Task GoToAddWorkoutAsync() =>
        Shell.Current.GoToAsync(nameof(AddWorkoutPage));

    public Task GoToEditWorkoutAsync(int workoutId) =>
        Shell.Current.GoToAsync($"{nameof(AddWorkoutPage)}?{RouteKeys.WorkoutId}={workoutId}");

    public Task GoBackAsync() =>
        Shell.Current.GoToAsync("..");
}
