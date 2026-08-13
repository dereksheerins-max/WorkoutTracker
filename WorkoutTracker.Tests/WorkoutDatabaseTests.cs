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

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch { /* ignore cleanup errors */ }
    }
}
