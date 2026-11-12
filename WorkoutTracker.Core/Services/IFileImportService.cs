namespace WorkoutTracker.Services;

/// <summary>
/// Asks the user for a file and returns its text, or null when they cancel.
/// Abstracted so the ViewModel stays testable and file access stays in the app.
/// </summary>
public interface IFileImportService
{
    Task<string?> PickTextAsync();
}
