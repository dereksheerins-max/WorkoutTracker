namespace WorkoutTracker.Services;

public class ShellDialogService : IDialogService
{
    public Task ShowAlertAsync(string title, string message, string cancel) =>
        Shell.Current.DisplayAlert(title, message, cancel);

    public Task<bool> ConfirmAsync(string title, string message, string accept, string cancel) =>
        Shell.Current.DisplayAlert(title, message, accept, cancel);
}
