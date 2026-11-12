#if __IOS__ || __MACCATALYST__
using Foundation;
#endif

namespace WorkoutTracker.Services;

/// <summary>
/// Keeps the workout database out of device backups.
/// Android already excludes the whole app's data with android:allowBackup="false";
/// Apple platforms back up the app's Library directory by default, including to
/// iCloud, so the file has to be marked individually.
/// </summary>
public static class DatabaseFileProtection
{
    public static void ExcludeFromBackup(string filePath)
    {
#if __IOS__ || __MACCATALYST__
        try
        {
            // The attribute lives on the file, which does not exist until the database
            // is first opened. An empty file is a valid (empty) SQLite database, so
            // creating it here is safe — but only create, never truncate.
            if (!File.Exists(filePath))
            {
                try
                {
                    using var created = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
                }
                catch (IOException)
                {
                    // Created by someone else in the meantime; the file is what matters.
                }
            }

            var url = NSUrl.FromFilename(filePath);
            if (!url.SetResource(NSUrl.IsExcludedFromBackupKey, NSNumber.FromBoolean(true), out var error))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Could not exclude the workout database from backup ({filePath}): {error}");
            }
        }
        catch (Exception ex)
        {
            // A backup hint must never stop the app from starting.
            System.Diagnostics.Debug.WriteLine(
                $"Could not exclude the workout database from backup ({filePath}): {ex}");
        }
#else
        // Windows, Android and Tizen need no per-file handling.
        _ = filePath;
#endif
    }
}
