namespace WorkoutTracker.Services;

public interface IDialogService
{
    Task ShowAlertAsync(string title, string message, string cancel);

    Task<bool> ConfirmAsync(string title, string message, string accept, string cancel);
}
