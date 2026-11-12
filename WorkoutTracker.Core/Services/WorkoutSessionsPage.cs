using WorkoutTracker.Models;

namespace WorkoutTracker.Services;

/// <summary>
/// Position of the last row a page returned, used to resume the list exactly where
/// it stopped. Keyset, not offset: rows inserted while the user scrolls cannot shift
/// the window, so pages neither duplicate nor skip workouts.
/// </summary>
public sealed record WorkoutCursor(DateTime Date, int Id);

/// <summary>One page of the workout list, plus the cursor for the next page if any.</summary>
public sealed record WorkoutSessionsPage(IReadOnlyList<WorkoutSession> Sessions, WorkoutCursor? Next)
{
    public bool HasMore => Next is not null;
}
