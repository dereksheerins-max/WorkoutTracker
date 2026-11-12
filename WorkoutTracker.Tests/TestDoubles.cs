using WorkoutTracker.Services;

namespace WorkoutTracker.Tests;

public class FakeDialogService : IDialogService
{
    public List<(string Title, string Message)> Alerts { get; } = new();

    public List<(string Title, string Message)> Confirms { get; } = new();

    public Queue<bool> ConfirmResults { get; } = new();

    public Task DisplayAlert(string title, string message, string cancel)
    {
        Alerts.Add((title, message));
        return Task.CompletedTask;
    }

    public Task<bool> DisplayAlert(string title, string message, string accept, string cancel)
    {
        Confirms.Add((title, message));
        return Task.FromResult(ConfirmResults.Count > 0 && ConfirmResults.Dequeue());
    }
}

public class FakeNavigationService : INavigationService
{
    public List<string> Calls { get; } = new();

    public Task GoToAddWorkoutAsync()
    {
        Calls.Add("Add");
        return Task.CompletedTask;
    }

    public Task GoToEditWorkoutAsync(int workoutId)
    {
        Calls.Add($"Edit:{workoutId}");
        return Task.CompletedTask;
    }

    public Task GoBackAsync()
    {
        Calls.Add("Back");
        return Task.CompletedTask;
    }
}
