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

    [Fact]
    public void Parse_ShouldRoundTripWhatWasExported()
    {
        var original = new List<WorkoutSession>
        {
            new()
            {
                Id = 3,
                Name = "Push Day",
                Date = new DateTime(2026, 4, 1, 18, 30, 0),
                Notes = "felt strong",
                Exercises = new List<Exercise> { new() { Name = "Bench Press", Sets = 3, Reps = 5, Weight = 82.5 } }
            }
        };

        var parsed = WorkoutExport.Parse(WorkoutExport.ToJson(original));

        Assert.True(parsed.Succeeded);
        var session = Assert.Single(parsed.Sessions);
        Assert.Equal("Push Day", session.Name);
        Assert.Equal(new DateTime(2026, 4, 1, 18, 30, 0), session.Date);
        Assert.Equal("felt strong", session.Notes);

        // Restored rows never keep the exported id, so they cannot overwrite local data.
        Assert.Equal(0, session.Id);

        var exercise = Assert.Single(session.Exercises);
        Assert.Equal("Bench Press", exercise.Name);
        Assert.Equal(3, exercise.Sets);
        Assert.Equal(5, exercise.Reps);
        Assert.Equal(82.5, exercise.Weight);
        Assert.Equal(0, exercise.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"formatVersion\":1,")]
    [InlineData("null")]
    public void Parse_ShouldRejectInputThatIsNotAnExport(string? json)
    {
        var parsed = WorkoutExport.Parse(json);

        Assert.False(parsed.Succeeded);
        Assert.NotNull(parsed.Error);
        Assert.Empty(parsed.Sessions);
    }

    [Fact]
    public void Parse_ShouldRejectAnUnsupportedFormatVersion()
    {
        var parsed = WorkoutExport.Parse("{\"formatVersion\": 99, \"sessions\": []}");

        Assert.False(parsed.Succeeded);
        Assert.Contains("99", parsed.Error);
        Assert.Contains(WorkoutExport.FormatVersion.ToString(), parsed.Error);
    }

    [Fact]
    public void Parse_ShouldRejectAWorkoutThatBreaksTheRules()
    {
        const string json = """
            {
              "formatVersion": 1,
              "sessions": [
                { "id": 1, "name": "Good", "date": "2026-04-01T00:00:00" },
                { "id": 2, "name": "Bad", "date": "2026-04-01T00:00:00",
                  "exercises": [{ "name": "Squat", "sets": 3, "reps": 5, "weight": -1 }] }
              ]
            }
            """;

        var parsed = WorkoutExport.Parse(json);

        Assert.False(parsed.Succeeded);
        Assert.Contains("'Bad'", parsed.Error);
        Assert.Contains("cannot be negative", parsed.Error);
        Assert.Empty(parsed.Sessions);
    }

    [Fact]
    public void Parse_ShouldRejectAnOversizedFile()
    {
        var json = new string('{', WorkoutExport.MaxDocumentLength + 1);

        var parsed = WorkoutExport.Parse(json);

        Assert.False(parsed.Succeeded);
        Assert.Contains("too large", parsed.Error);
    }

    [Fact]
    public void Parse_ShouldAcceptADocumentWithoutSessions()
    {
        var parsed = WorkoutExport.Parse("{\"formatVersion\": 1}");

        Assert.True(parsed.Succeeded);
        Assert.Empty(parsed.Sessions);
    }

    [Fact]
    public void Parse_ShouldIgnorePropertiesItDoesNotKnow()
    {
        const string json = """
            {
              "formatVersion": 1,
              "writtenBy": "a newer build",
              "sessions": [
                { "id": 1, "name": "Push Day", "date": "2026-04-01T00:00:00", "mood": "great" }
              ]
            }
            """;

        var parsed = WorkoutExport.Parse(json);

        Assert.True(parsed.Succeeded);
        Assert.Equal("Push Day", Assert.Single(parsed.Sessions).Name);
    }
}
