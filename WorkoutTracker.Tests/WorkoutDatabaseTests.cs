using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using WorkoutTracker.Models;
using WorkoutTracker.Services;
using Xunit;

namespace WorkoutTracker.Tests;

public class WorkoutDatabaseTests : IDisposable
{
    private readonly string _dbPath;
    private readonly WorkoutDatabase _database;

    public WorkoutDatabaseTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"workouts_{Guid.NewGuid()}.db3");
        _database = new WorkoutDatabase(_dbPath, NullLogger<WorkoutDatabase>.Instance);
        _database.InitializeAsync().GetAwaiter().GetResult();
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
        Assert.Single(reloaded?.Exercises);
        Assert.Equal("B", reloaded?.Exercises[0].Name);
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

        var all = await _database.GetWorkoutSessionsAsync();
        Assert.DoesNotContain(all, s => s.Id == id);
    }

    [Fact]
    public async Task FirstQuery_WithoutExplicitInitialize_ShouldSelfInitialize()
    {
        var path = Path.Combine(Path.GetTempPath(), $"workouts_{Guid.NewGuid()}.db3");
        using (var db = new WorkoutDatabase(path, NullLogger<WorkoutDatabase>.Instance))
        {
            var sessions = await db.GetWorkoutSessionsAsync();

            Assert.Empty(sessions);
        }

        File.Delete(path);
    }

    [Fact]
    public async Task Dispose_ShouldReleaseDatabaseFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"workouts_{Guid.NewGuid()}.db3");
        var db = new WorkoutDatabase(path, NullLogger<WorkoutDatabase>.Instance);
        await db.GetWorkoutSessionsAsync();

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
    public async Task GetWorkoutSessionsAsync_DatabaseFailure_ShouldThrow()
    {
        // SQLite cannot open a directory as a database file, so first use fails.
        var badPath = Path.Combine(Path.GetTempPath(), $"workouts_{Guid.NewGuid()}");
        Directory.CreateDirectory(badPath);
        try
        {
            var db = new WorkoutDatabase(badPath, NullLogger<WorkoutDatabase>.Instance);

            await Assert.ThrowsAnyAsync<Exception>(() => db.GetWorkoutSessionsAsync());
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
        Assert.Empty(await _database.GetWorkoutSessionsAsync());
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
        Assert.Empty(await _database.GetWorkoutSessionsAsync());
    }

    public void Dispose()
    {
        _database.Dispose();

        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
