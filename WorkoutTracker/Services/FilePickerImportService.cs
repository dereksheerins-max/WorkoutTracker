namespace WorkoutTracker.Services;

public class FilePickerImportService : IFileImportService
{
    public async Task<string?> PickTextAsync()
    {
        // Deliberately unfiltered: a filter that guesses the platform's MIME type for a
        // downloaded backup can hide the very file the user needs. The document itself
        // is validated after reading, which is the check that matters.
        var picked = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Select a workout export"
        });

        if (picked is null)
            return null;

        await using var stream = await picked.OpenReadAsync();
        using var reader = new StreamReader(stream);

        // Read one character past the limit so an oversized file is rejected by the
        // parser without ever being fully buffered.
        var buffer = new char[WorkoutExport.MaxDocumentLength + 1];
        var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);

        return new string(buffer, 0, read);
    }
}
