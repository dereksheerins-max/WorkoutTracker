namespace WorkoutTracker.Models;

/// <summary>
/// Single source of truth for what a workout may contain. Applied by the editor
/// before saving and again by the database, so no caller can bypass it.
/// </summary>
public static class WorkoutRules
{
    public const int MaxWorkoutNameLength = 100;
    public const int MaxNotesLength = 500;
    public const int MaxExerciseNameLength = 100;

    // Whitespace around user input is never meaningful; normalize before validating
    // and before writing, otherwise a padded name passes the length check and then
    // gets stored at its unpadded size.
    public static void Normalize(WorkoutSession session)
    {
        session.Name = (session.Name ?? string.Empty).Trim();
        session.Notes = string.IsNullOrWhiteSpace(session.Notes) ? null : session.Notes.Trim();

        foreach (var exercise in session.Exercises)
        {
            exercise.Name = (exercise.Name ?? string.Empty).Trim();
        }
    }

    public static IReadOnlyList<string> Validate(WorkoutSession session)
    {
        var errors = new List<string>();

        var name = (session.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            errors.Add("Please enter a workout name");
        else if (name.Length > MaxWorkoutNameLength)
            errors.Add($"Workout name must be {MaxWorkoutNameLength} characters or fewer.");

        if ((session.Notes ?? string.Empty).Trim().Length > MaxNotesLength)
            errors.Add($"Notes must be {MaxNotesLength} characters or fewer.");

        foreach (var exercise in session.Exercises)
        {
            var exerciseName = (exercise.Name ?? string.Empty).Trim();
            if (exerciseName.Length == 0)
                errors.Add("All exercises must have a name.");
            else if (exerciseName.Length > MaxExerciseNameLength)
                errors.Add($"Exercise names must be {MaxExerciseNameLength} characters or fewer.");

            if (exercise.Sets < 1)
                errors.Add("All exercises must have at least one set.");

            if (exercise.Reps < 1)
                errors.Add("All exercises must have at least one rep.");

            // NaN fails every comparison, so it slipped past the negative check and
            // was stored as NULL, reloading as zero.
            if (!double.IsFinite(exercise.Weight))
                errors.Add("Exercise weight must be a finite number.");
            else if (exercise.Weight < 0)
                errors.Add("Exercise weight cannot be negative.");
        }

        return errors;
    }
}
