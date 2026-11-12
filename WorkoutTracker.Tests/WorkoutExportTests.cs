using System.Text.Json;
using WorkoutTracker.Models;
using WorkoutTracker.Services;

namespace WorkoutTracker.Tests;

public class WorkoutExportTests
{
    [Fact]
    public void ToJson_ShouldDescribeEveryWorkoutAndExercise()
    {
        var sessions = new List<WorkoutSession>
        {
            new()
            {
                Id = 7,
                Name = "Push Day",
                Date = new DateTime(2026, 4, 1, 18, 30, 0),
                Notes = "felt strong",
                Exercises = new List<Exercise>
                {
                    new() { Name = "Bench Press", Sets = 3, Reps = 5, Weight = 82.5 }
                }
            }
        };

        var json = WorkoutExport.ToJson(sessions);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(WorkoutExport.FormatVersion, root.GetProperty("formatVersion").GetInt32());
        Assert.True(root.TryGetProperty("exportedAtUtc", out _));

        var session = Assert.Single(root.GetProperty("sessions").EnumerateArray());
        Assert.Equal(7, session.GetProperty("id").GetInt32());
        Assert.Equal("Push Day", session.GetProperty("name").GetString());
        Assert.Equal(new DateTime(2026, 4, 1, 18, 30, 0), session.GetProperty("date").GetDateTime());
        Assert.Equal("felt strong", session.GetProperty("notes").GetString());

        var exercise = Assert.Single(session.GetProperty("exercises").EnumerateArray());
        Assert.Equal("Bench Press", exercise.GetProperty("name").GetString());
        Assert.Equal(3, exercise.GetProperty("sets").GetInt32());
        Assert.Equal(5, exercise.GetProperty("reps").GetInt32());
        Assert.Equal(82.5, exercise.GetProperty("weight").GetDouble());
    }

    [Fact]
    public void ToJson_WithoutNotes_ShouldOmitTheField()
    {
        var sessions = new List<WorkoutSession> { new() { Id = 1, Name = "Leg Day", Date = DateTime.Today } };

        using var document = JsonDocument.Parse(WorkoutExport.ToJson(sessions));
        var session = document.RootElement.GetProperty("sessions")[0];

        Assert.False(session.TryGetProperty("notes", out _));
        Assert.Empty(session.GetProperty("exercises").EnumerateArray());
    }

    [Fact]
    public void ToJson_WithNoSessions_ShouldStillBeAValidDocument()
    {
        using var document = JsonDocument.Parse(WorkoutExport.ToJson(Array.Empty<WorkoutSession>()));

        Assert.Equal(WorkoutExport.FormatVersion, document.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Empty(document.RootElement.GetProperty("sessions").EnumerateArray());
    }

    [Fact]
    public void ToJson_WithNullSessions_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() => WorkoutExport.ToJson(null!));
    }
}
