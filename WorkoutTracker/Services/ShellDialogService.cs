namespace WorkoutTracker.Services;

public class ShellDialogService : IDialogService
{
    public Task DisplayAlert(string title, string message, string cancel) =>
        Shell.Current.DisplayAlert(title, message, cancel);

    public Task<bool> DisplayAlert(string title, string message, string accept, string cancel) =>
        Shell.Current.DisplayAlert(title, message, accept, cancel);
}
