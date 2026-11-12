using System.Text.Json;
using System.Text.Json.Serialization;
using WorkoutTracker.Models;

namespace WorkoutTracker.Services;

/// <summary>Outcome of reading an export file. Errors are user-facing messages.</summary>
public sealed record WorkoutExportParseResult(IReadOnlyList<WorkoutSession> Sessions, string? Error)
{
    public bool Succeeded => Error is null;

    internal static WorkoutExportParseResult Failure(string error) => new(Array.Empty<WorkoutSession>(), error);
}

/// <summary>
/// Reads and writes the JSON document that carries a workout log in and out of the app.
/// Input is treated as untrusted: the document is size-limited, version-checked and
/// every workout is validated before anything is written.
/// </summary>
public static class WorkoutExport
{
    // Version of the export document itself, independent of the database schema so the
    // shape can evolve without the two being forced together.
    public const int FormatVersion = 1;

    // Upper bound on what is accepted for import; a workout log is kilobytes.
    public const int MaxDocumentLength = 20 * 1024 * 1024;

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

    public static WorkoutExportParseResult Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return WorkoutExportParseResult.Failure("That file is empty.");

        if (json.Length > MaxDocumentLength)
            return WorkoutExportParseResult.Failure("That file is too large to be a workout export.");

        ExportDocument? document;
        try
        {
            // Unknown properties are ignored, so a newer export still opens here.
            document = JsonSerializer.Deserialize<ExportDocument>(json, Options);
        }
        catch (JsonException)
        {
            return WorkoutExportParseResult.Failure("That file is not a valid workout export.");
        }

        if (document is null)
            return WorkoutExportParseResult.Failure("That file is not a valid workout export.");

        if (document.FormatVersion != FormatVersion)
        {
            return WorkoutExportParseResult.Failure(
                $"That export was written in format version {document.FormatVersion}; this app reads version {FormatVersion}.");
        }

        var sessions = new List<WorkoutSession>();
        var index = 0;

        foreach (var imported in document.Sessions ?? Array.Empty<ExportSession>())
        {
            index++;

            if (imported is null)
                return WorkoutExportParseResult.Failure($"Workout {index} in that file is empty.");

            // Ids are deliberately dropped: a restored workout is always added as a new
            // one, so a file can never overwrite a workout that is already stored.
            var session = new WorkoutSession
            {
                Name = imported.Name ?? string.Empty,
                Date = imported.Date,
                Notes = imported.Notes,
                Exercises = (imported.Exercises ?? Array.Empty<ExportExercise>())
                    .Where(exercise => exercise is not null)
                    .Select(exercise => new Exercise
                    {
                        Name = exercise.Name ?? string.Empty,
                        Sets = exercise.Sets,
                        Reps = exercise.Reps,
                        Weight = exercise.Weight
                    })
                    .ToList()
            };

            WorkoutRules.Normalize(session);
            var errors = WorkoutRules.Validate(session);
            if (errors.Count > 0)
            {
                var label = string.IsNullOrWhiteSpace(session.Name) ? $"Workout {index}" : $"'{session.Name}'";
                return WorkoutExportParseResult.Failure($"Workout {label} in that file is not valid: {errors[0]}");
            }

            sessions.Add(session);
        }

        return new WorkoutExportParseResult(sessions, null);
    }

    private sealed record ExportDocument(
        int FormatVersion,
        DateTime ExportedAtUtc,
        IReadOnlyList<ExportSession>? Sessions);

    private sealed record ExportSession(
        int Id,
        string? Name,
        DateTime Date,
        string? Notes,
        IReadOnlyList<ExportExercise>? Exercises);

    private sealed record ExportExercise(
        string? Name,
        int Sets,
        int Reps,
        double Weight);
}
