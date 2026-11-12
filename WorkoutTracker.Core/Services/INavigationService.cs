namespace WorkoutTracker.Services;

public interface INavigationService
{
    Task GoToAddWorkoutAsync();

    Task GoToEditWorkoutAsync(int workoutId);

    Task GoBackAsync();
}
