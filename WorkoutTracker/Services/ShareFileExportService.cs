namespace WorkoutTracker.Services;

public class ShareFileExportService : IFileExportService
{
    public async Task ExportAsync(string fileName, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);

        // Cache directory on purpose: the file goes straight to the share sheet and the
        // OS may clear it afterwards. The workout log itself stays in AppDataDirectory.
        var path = Path.Combine(FileSystem.CacheDirectory, fileName);
        await File.WriteAllTextAsync(path, content);

        try
        {
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Export workouts",
                File = new ShareFile(path)
            });
        }
        catch (Exception)
        {
            // Unpackaged Windows builds have no share sheet. Opening the file with its
            // default handler still gets the data out instead of dead-ending.
            var opened = await Launcher.Default.OpenAsync(new OpenFileRequest("Exported workouts", new ReadOnlyFile(path)));
            if (!opened)
                throw;
        }
    }
}
