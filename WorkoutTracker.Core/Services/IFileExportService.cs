namespace WorkoutTracker.Services;

/// <summary>
/// Hands a generated file to the platform so the user can save it or send it somewhere.
/// Abstracted so the ViewModel stays testable and the file plumbing stays in the app.
/// </summary>
public interface IFileExportService
{
    Task ExportAsync(string fileName, string content);
}
