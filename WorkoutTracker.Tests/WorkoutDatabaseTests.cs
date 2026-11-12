using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using WorkoutTracker.Models;
using WorkoutTracker.Services;
using Xunit;

namespace WorkoutTracker.Tests;

public class WorkoutDatabaseTests : IAsyncLifetime
{
    private string _dbPath = string.Empty;
    private WorkoutDatabase _database = null!;

    public async Task InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"workouts_{Guid.NewGuid()}.db3");
        _database = new WorkoutDatabase(_dbPath, NullLogger<WorkoutDatabase>.Instance);
        await _database.InitializeAsync();
    }

    private Task<List<WorkoutSession>> LoadAllAsync() => LoadAllAsync(_database);

    // Walks the pages the way the list screen does, which also exercises pagination.
    private static async Task<List<WorkoutSession>> LoadAllAsync(WorkoutDatabase database)
    {
        var all = new List<WorkoutSession>();
        WorkoutCursor? cursor = null;

        while (true)
        {
            var page = await database.GetWorkoutSessionsPageAsync(50, cursor);
            all.AddRange(page.Sessions);

            if (!page.HasMore)
                return all;

            cursor = page.Next;
        }
    }

    [Fact]
    public async Task SaveAndGetSession_ShouldPersistExercises()
    {
        var session = new WorkoutSession
        {
            Name = "Test Session",
            Date = DateTime.Today,
            Exercises = new System.Collections.Generic.List<Exercise>
            {
                new Exercise { Name = "Squat", Sets = 3, Reps = 5, Weight = 100 },
            }
        };

        var id = await _database.SaveWorkoutSessionAsync(session);
        Assert.True(id != 0 || session.Id != 0);

        var loaded = await _database.GetWorkoutSessionAsync(session.Id);
        Assert.NotNull(loaded);
        Assert.Equal(session.Name, loaded!.Name);
        Assert.NotNull(loaded.Exercises);
        Assert.Single(loaded.Exercises);
        Assert.Equal("Squat", loaded.Exercises[0].Name);
    }

    [Fact]
    public async Task Save_UpdateRemoveExercise_ShouldReflectChanges()
    {
        var session = new WorkoutSession
        {
            Name = "Update Session",
            Date = DateTime.Today,
            Exercises = new System.Collections.Generic.List<Exercise>
            {
                new Exercise { Name = "A", Sets = 1, Reps = 1, Weight = 1 },
                new Exercise { Name = "B", Sets = 2, Reps = 2, Weight = 2 }
            }
        };

        await _database.SaveWorkoutSessionAsync(session);
        var loaded = await _database.GetWorkoutSessionAsync(session.Id);
        Assert.Equal(2, loaded?.Exercises.Count);

        // Remove one exercise and save
        session.Exercises.RemoveAt(0);
        await _database.SaveWorkoutSessionAsync(session);

        var reloaded = await _database.GetWorkoutSessionAsync(session.Id);
        Assert.NotNull(reloaded);
        Assert.Single(reloaded!.Exercises);
        Assert.Equal("B", reloaded.Exercises[0].Name);
    }

    [Fact]
    public async Task DeleteSession_ShouldRemoveSessionAndExercises()
    {
        var session = new WorkoutSession
        {
            Name = "Delete Session",
            Date = DateTime.Today,
            Exercises = new System.Collections.Generic.List<Exercise>
            {
                new Exercise { Name = "X", Sets = 1, Reps = 1, Weight = 0 },
            }
        };

        await _database.SaveWorkoutSessionAsync(session);
        var id = session.Id;
        await _database.DeleteWorkoutSessionAsync(session);

        var loaded = await _database.GetWorkoutSessionAsync(id);
        Assert.Null(loaded);

        var all = await LoadAllAsync();
        Assert.DoesNotContain(all, s => s.Id == id);
    }

    [Fact]
    public async Task FirstQuery_WithoutExplicitInitialize_ShouldSelfInitialize()
    {
        var path = Path.Combine(Path.GetTempPath(), $"workouts_{Guid.NewGuid()}.db3");
        using (var db = new WorkoutDatabase(path, NullLogger<WorkoutDatabase>.Instance))
        {
            var sessions = await LoadAllAsync(db);

            Assert.Empty(sessions);
        }

        File.Delete(path);
    }

    [Fact]
    public async Task Dispose_ShouldReleaseDatabaseFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"workouts_{Guid.NewGuid()}.db3");
        var db = new WorkoutDatabase(path, NullLogger<WorkoutDatabase>.Instance);
        await LoadAllAsync(db);

        db.Dispose();

        // Throws IOException if the connection still holds the file open
        File.Delete(path);
    }

    [Fact]
    public async Task GetWorkoutSessionAsync_UnknownId_ShouldReturnNull()
    {
        var loaded = await _database.GetWorkoutSessionAsync(123456);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task LoadWorkouts_DatabaseFailure_ShouldThrow()
    {
        // SQLite cannot open a directory as a database file, so first use fails.
        var badPath = Path.Combine(Path.GetTempPath(), $"workouts_{Guid.NewGuid()}");
        Directory.CreateDirectory(badPath);
        try
        {
            var db = new WorkoutDatabase(badPath, NullLogger<WorkoutDatabase>.Instance);

            await Assert.ThrowsAnyAsync<Exception>(() => LoadAllAsync(db));
        }
        finally
        {
            Directory.Delete(badPath);
        }
    }

    [Fact]
    public async Task Save_NewSessionWithStaleExerciseId_ShouldPersistTheExercise()
    {
        // A rolled-back save leaves the entity holding an id that no longer exists.
        var session = new WorkoutSession
        {
            Name = "Stale Id",
            Date = DateTime.Today,
            Exercises = new List<Exercise>
            {
                new() { Id = 999_999, Name = "Phantom", Sets = 3, Reps = 5, Weight = 26.2 }
            }
        };

        await _database.SaveWorkoutSessionAsync(session);

        var loaded = await _database.GetWorkoutSessionAsync(session.Id);
        var exercise = Assert.Single(loaded!.Exercises);
        Assert.Equal("Phantom", exercise.Name);
        Assert.NotEqual(999_999, exercise.Id);

        // Retrying must update the now-real row rather than insert a duplicate.
        session.Exercises[0].Weight = 30;
        await _database.SaveWorkoutSessionAsync(session);

        var reloaded = await _database.GetWorkoutSessionAsync(session.Id);
        var updated = Assert.Single(reloaded!.Exercises);
        Assert.Equal(30, updated.Weight);
    }

    [Fact]
    public async Task Save_ExerciseOwnedByAnotherSession_ShouldNotBeStolen()
    {
        var other = new WorkoutSession
        {
            Name = "Other",
            Date = DateTime.Today,
            Exercises = new List<Exercise> { new() { Name = "Row", Sets = 3, Reps = 10, Weight = 40 } }
        };
        await _database.SaveWorkoutSessionAsync(other);
        var otherExerciseId = other.Exercises[0].Id;

        var mine = new WorkoutSession
        {
            Name = "Mine",
            Date = DateTime.Today,
            Exercises = new List<Exercise>
            {
                new() { Id = otherExerciseId, Name = "Bench", Sets = 3, Reps = 5, Weight = 80 }
            }
        };

        await _database.SaveWorkoutSessionAsync(mine);

        var otherReloaded = await _database.GetWorkoutSessionAsync(other.Id);
        Assert.Equal("Row", Assert.Single(otherReloaded!.Exercises).Name);

        var mineReloaded = await _database.GetWorkoutSessionAsync(mine.Id);
        Assert.Equal("Bench", Assert.Single(mineReloaded!.Exercises).Name);
    }

    [Fact]
    public async Task Save_WhenSessionRowNoLongerExists_ShouldThrow()
    {
        var session = new WorkoutSession
        {
            Name = "Deleted Elsewhere",
            Date = DateTime.Today,
            Exercises = new List<Exercise> { new() { Name = "Squat", Sets = 3, Reps = 5, Weight = 100 } }
        };
        await _database.SaveWorkoutSessionAsync(session);
        await _database.DeleteWorkoutSessionAsync(session);

        session.Exercises[0].Weight = 110;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _database.SaveWorkoutSessionAsync(session));
        Assert.Empty(await LoadAllAsync());
    }

    [Fact]
    public async Task Save_WithNullSession_ShouldThrowArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _database.SaveWorkoutSessionAsync(null!));
    }

    [Fact]
    public async Task Save_WithNullExerciseList_ShouldThrowArgumentException()
    {
        var session = new WorkoutSession { Name = "Null Exercises", Date = DateTime.Today, Exercises = null! };

        await Assert.ThrowsAsync<ArgumentException>(() => _database.SaveWorkoutSessionAsync(session));
        Assert.Empty(await LoadAllAsync());
    }

    [Fact]
    public async Task Save_WithInvalidValues_ShouldThrowBeforeWriting()
    {
        var session = new WorkoutSession
        {
            Name = "   ",
            Date = DateTime.Today,
            Exercises = new List<Exercise> { new() { Name = "Squat", Sets = 3, Reps = 5, Weight = 100 } }
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _database.SaveWorkoutSessionAsync(session));
        Assert.Empty(await LoadAllAsync());
    }

    [Fact]
    public async Task Save_WithNonFiniteWeight_ShouldThrowBeforeWriting()
    {
        var session = new WorkoutSession
        {
            Name = "NaN Press",
            Date = DateTime.Today,
            Exercises = new List<Exercise> { new() { Name = "Squat", Sets = 3, Reps = 5, Weight = double.NaN } }
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _database.SaveWorkoutSessionAsync(session));
        Assert.Empty(await LoadAllAsync());
    }

    [Fact]
    public async Task Save_ShouldTrimWhitespaceAroundUserInput()
    {
        var session = new WorkoutSession
        {
            Name = "  Push Day  ",
            Date = DateTime.Today,
            Notes = "   felt strong   ",
            Exercises = new List<Exercise> { new() { Name = "  Bench Press  ", Sets = 3, Reps = 5, Weight = 80 } }
        };

        await _database.SaveWorkoutSessionAsync(session);

        var loaded = await _database.GetWorkoutSessionAsync(session.Id);
        Assert.Equal("Push Day", loaded!.Name);
        Assert.Equal("felt strong", loaded.Notes);
        Assert.Equal("Bench Press", Assert.Single(loaded.Exercises).Name);
    }

    [Fact]
    public async Task Initialize_AfterTransientFailure_ShouldRecoverOnTheNextCall()
    {
        // A directory at the database path makes the first open fail.
        var path = Path.Combine(Path.GetTempPath(), $"workouts_{Guid.NewGuid()}");
        Directory.CreateDirectory(path);

        var db = new WorkoutDatabase(path, NullLogger<WorkoutDatabase>.Instance);
        await Assert.ThrowsAnyAsync<Exception>(() => LoadAllAsync(db));

        Directory.Delete(path); // the transient problem goes away

        Assert.Empty(await LoadAllAsync(db));
        db.Dispose();
    }

    [Fact]
    public async Task DisposeAsync_ShouldReleaseDatabaseFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"workouts_{Guid.NewGuid()}.db3");
        var db = new WorkoutDatabase(path, NullLogger<WorkoutDatabase>.Instance);
        await LoadAllAsync(db);

        await db.DisposeAsync();

        File.Delete(path); // throws if the connection still holds the file
    }

    [Fact]
    public void Dispose_CalledTwice_ShouldNotThrow()
    {
        var path = Path.Combine(Path.GetTempPath(), $"workouts_{Guid.NewGuid()}.db3");
        var db = new WorkoutDatabase(path, NullLogger<WorkoutDatabase>.Instance);

        db.Dispose();
        db.Dispose();
    }

    [Fact]
    public async Task GetWorkoutSessionsPage_ShouldPageNewestFirstWithoutDuplicates()
    {
        var baseDate = new DateTime(2026, 1, 1);
        for (var i = 0; i < 5; i++)
        {
            await _database.SaveWorkoutSessionAsync(new WorkoutSession
            {
                Name = $"Session {i}",
                Date = baseDate.AddDays(i)
            });
        }

        var seen = new List<string>();
        WorkoutCursor? cursor = null;
        var pages = 0;

        while (true)
        {
            var page = await _database.GetWorkoutSessionsPageAsync(2, cursor);
            pages++;
            seen.AddRange(page.Sessions.Select(s => s.Name));

            if (!page.HasMore)
                break;

            cursor = page.Next;
        }

        Assert.Equal(3, pages);
        Assert.Equal(
            new[] { "Session 4", "Session 3", "Session 2", "Session 1", "Session 0" },
            seen);
    }

    [Fact]
    public async Task GetWorkoutSessionsPage_ShouldCountExercisesWithoutLoadingThem()
    {
        await _database.SaveWorkoutSessionAsync(new WorkoutSession
        {
            Name = "Two Exercises",
            Date = DateTime.Today,
            Exercises = new List<Exercise>
            {
                new() { Name = "Squat", Sets = 3, Reps = 5, Weight = 100 },
                new() { Name = "Row", Sets = 3, Reps = 10, Weight = 40 }
            }
        });
        await _database.SaveWorkoutSessionAsync(new WorkoutSession { Name = "No Exercises", Date = DateTime.Today.AddDays(-1) });

        var page = await _database.GetWorkoutSessionsPageAsync(10);

        Assert.Equal(2, page.Sessions.Count);
        Assert.Equal(2, page.Sessions[0].ExerciseCount);
        Assert.Equal(0, page.Sessions[1].ExerciseCount);
        Assert.All(page.Sessions, s => Assert.Empty(s.Exercises));
    }

    [Fact]
    public async Task GetWorkoutSessionsPage_WithEqualDates_ShouldOrderByIdDescending()
    {
        var sameDate = new DateTime(2026, 3, 1);
        var first = new WorkoutSession { Name = "First", Date = sameDate };
        var second = new WorkoutSession { Name = "Second", Date = sameDate };
        await _database.SaveWorkoutSessionAsync(first);
        await _database.SaveWorkoutSessionAsync(second);

        var page = await _database.GetWorkoutSessionsPageAsync(10);

        Assert.Equal(new[] { "Second", "First" }, page.Sessions.Select(s => s.Name));
    }

    [Fact]
    public async Task GetWorkoutSessionsPage_ShouldRejectNonPositivePageSize()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _database.GetWorkoutSessionsPageAsync(0));
    }

    [Fact]
    public async Task Initialize_ShouldStampSchemaVersion()
    {
        var path = Path.Combine(Path.GetTempPath(), $"workouts_{Guid.NewGuid()}.db3");
        using (var db = new WorkoutDatabase(path, NullLogger<WorkoutDatabase>.Instance))
        {
            await db.InitializeAsync();
        }

        using (var connection = new SQLite.SQLiteConnection(path))
        {
            Assert.Equal(1, connection.ExecuteScalar<int>("PRAGMA user_version"));
        }

        File.Delete(path);
    }

    [Fact]
    public async Task Initialize_OnOlderDatabaseWithoutIndex_ShouldMigrateAndKeepData()
    {
        var path = Path.Combine(Path.GetTempPath(), $"workouts_{Guid.NewGuid()}.db3");

        using (var db = new WorkoutDatabase(path, NullLogger<WorkoutDatabase>.Instance))
        {
            await db.SaveWorkoutSessionAsync(new WorkoutSession { Name = "Survivor", Date = DateTime.Today });
        }

        // Roll the file back to how a pre-migration build left it.
        using (var raw = new SQLite.SQLiteConnection(path))
        {
            raw.Execute("DROP INDEX IF EXISTS idx_workout_sessions_date");
            raw.Execute("PRAGMA user_version = 0");
        }

        using (var db = new WorkoutDatabase(path, NullLogger<WorkoutDatabase>.Instance))
        {
            var sessions = await LoadAllAsync(db);
            Assert.Equal("Survivor", Assert.Single(sessions).Name);
        }

        using (var connection = new SQLite.SQLiteConnection(path))
        {
            Assert.Equal(1, connection.ExecuteScalar<int>("PRAGMA user_version"));
            Assert.Equal(1, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'idx_workout_sessions_date'"));
        }

        File.Delete(path);
    }

    [Fact]
    public async Task Initialize_WithUnreadableFile_ShouldQuarantineItAndStartFresh()
    {
        var path = Path.Combine(Path.GetTempPath(), $"workouts_{Guid.NewGuid()}.db3");
        const string garbage = "this is not a sqlite database";
        await File.WriteAllTextAsync(path, garbage);

        var db = new WorkoutDatabase(path, NullLogger<WorkoutDatabase>.Instance);
        string? quarantined;

        try
        {
            Assert.Empty(await LoadAllAsync(db));

            quarantined = db.TakeQuarantineNotice();
            Assert.NotNull(quarantined);
            Assert.True(File.Exists(quarantined));
            Assert.Equal(garbage, await File.ReadAllTextAsync(quarantined!));

            // The notice is handed out once, so the alert cannot repeat on every navigation.
            Assert.Null(db.TakeQuarantineNotice());

            // The app is usable again rather than failing on every screen.
            await db.SaveWorkoutSessionAsync(new WorkoutSession { Name = "After Recovery", Date = DateTime.Today });
            Assert.Single(await LoadAllAsync(db));
        }
        finally
        {
            db.Dispose();
        }

        File.Delete(path);
        File.Delete(quarantined!);
    }

    [Fact]
    public async Task GetWorkoutSessionsWithExercises_ShouldSpanPagesAndIncludeExercises()
    {
        const int total = 55; // more than one export page
        var baseDate = new DateTime(2026, 1, 1);
        for (var i = 0; i < total; i++)
        {
            await _database.SaveWorkoutSessionAsync(new WorkoutSession
            {
                Name = $"S{i:00}",
                Date = baseDate.AddMinutes(i),
                Exercises = new List<Exercise> { new() { Name = $"Exercise {i:00}", Sets = 3, Reps = 5, Weight = 10 } }
            });
        }

        var sessions = await _database.GetWorkoutSessionsWithExercisesAsync();

        Assert.Equal(total, sessions.Count);
        Assert.Equal("S54", sessions[0].Name);
        Assert.Equal("S00", sessions[^1].Name);
        Assert.All(sessions, s => Assert.Single(s.Exercises));
        Assert.Equal("Exercise 54", sessions[0].Exercises[0].Name);
    }

    public async Task DisposeAsync()
    {
        await _database.DisposeAsync();

        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
