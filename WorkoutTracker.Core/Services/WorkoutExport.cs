using System.Text.Json;
using System.Text.Json.Serialization;
using WorkoutTracker.Models;

namespace WorkoutTracker.Services;

/// <summary>
/// Turns stored workouts into a self-describing JSON document: the only way data leaves
/// the app, since the database is excluded from device backups.
/// </summary>
public static class WorkoutExport
{
    // Version of the export document itself, independent of the database schema so the
    // shape can evolve without the two being forced together.
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string ToJson(IReadOnlyList<WorkoutSession> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var document = new ExportDocument(
            FormatVersion,
            DateTime.UtcNow,
            sessions
                .Select(session => new ExportSession(
                    session.Id,
                    session.Name,
                    session.Date,
                    session.Notes,
                    session.Exercises
                        .Select(exercise => new ExportExercise(
                            exercise.Name,
                            exercise.Sets,
                            exercise.Reps,
                            exercise.Weight))
                        .ToList()))
                .ToList());

        return JsonSerializer.Serialize(document, Options);
    }

    private sealed record ExportDocument(
        int FormatVersion,
        DateTime ExportedAtUtc,
        IReadOnlyList<ExportSession> Sessions);

    private sealed record ExportSession(
        int Id,
        string Name,
        DateTime Date,
        string? Notes,
        IReadOnlyList<ExportExercise> Exercises);

    private sealed record ExportExercise(
        string Name,
        int Sets,
        int Reps,
        double Weight);
}
