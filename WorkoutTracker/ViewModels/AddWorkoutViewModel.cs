using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Extensions.Logging;
using WorkoutTracker.Models;
using WorkoutTracker.Services;

namespace WorkoutTracker.ViewModels;

[QueryProperty(nameof(WorkoutId), "Id")]
public partial class AddWorkoutViewModel : ObservableObject
{
    private readonly WorkoutDatabase _database;
    private readonly Microsoft.Extensions.Logging.ILogger<AddWorkoutViewModel>? _logger;

    [ObservableProperty]
    private int _workoutId;

    [ObservableProperty]
    private string _workoutName = string.Empty;

    [ObservableProperty]
    private DateTime _workoutDate = DateTime.Today;

    [ObservableProperty]
    private string _workoutNotes = string.Empty;

    public ObservableCollection<Exercise> Exercises { get; } = new();

    public AddWorkoutViewModel(WorkoutDatabase database, Microsoft.Extensions.Logging.ILogger<AddWorkoutViewModel>? logger = null)
    {
        _database = database;
        _logger = logger;
    }

    // Call the async loader without changing the generated partial's signature.
    // Use a helper that observes errors so they don't go unhandled.
    partial void OnWorkoutIdChanged(int value)
    {
        if (value > 0)
        {
            _ = LoadAndHandleAsync(value);
        }
    }

    private async Task LoadAndHandleAsync(int id)
    {
        try
        {
            await LoadWorkoutAsync(id);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load workout {Id}", id);
            await Shell.Current.DisplayAlert("Error", "Failed to load workout", "OK");
        }
    }

    private async Task LoadWorkoutAsync(int id)
    {
        var session = await _database.GetWorkoutSessionAsync(id);
        if (session != null)
        {
            WorkoutName = session.Name;
            WorkoutDate = session.Date;
            WorkoutNotes = session.Notes ?? string.Empty;

            // Clear existing to avoid duplicates when re-loading
            Exercises.Clear();
            foreach (var exercise in session.Exercises ?? Enumerable.Empty<Exercise>())
            {
                Exercises.Add(exercise);
            }
        }
    }

    [RelayCommand]
    private void AddExercise()
    {
        Exercises.Add(new Exercise
        {
            Name = "New Exercise",
            Sets = 3,
            Reps = 10,
            Weight = 0
        });
    }

    [RelayCommand]
    private void RemoveExercise(Exercise exercise)
    {
        Exercises.Remove(exercise);
    }

    private const int MaxNameLength = 100;
    private const int MaxNotesLength = 500;
    private const int MaxExerciseNameLength = 100;

    [RelayCommand]
    private async Task SaveWorkoutAsync()
    {
        if (string.IsNullOrWhiteSpace(WorkoutName))
        {
            await Shell.Current.DisplayAlert("Error", "Please enter a workout name", "OK");
            return;
        }

        if (WorkoutName.Trim().Length > MaxNameLength)
        {
            await Shell.Current.DisplayAlert("Error", $"Workout name must be {MaxNameLength} characters or fewer.", "OK");
            return;
        }

        if (WorkoutNotes.Length > MaxNotesLength)
        {
            await Shell.Current.DisplayAlert("Error", $"Notes must be {MaxNotesLength} characters or fewer.", "OK");
            return;
        }

        foreach (var exercise in Exercises)
        {
            if (string.IsNullOrWhiteSpace(exercise.Name))
            {
                await Shell.Current.DisplayAlert("Error", "All exercises must have a name.", "OK");
                return;
            }

            if (exercise.Name.Trim().Length > MaxExerciseNameLength)
            {
                await Shell.Current.DisplayAlert("Error", $"Exercise names must be {MaxExerciseNameLength} characters or fewer.", "OK");
                return;
            }
        }

        var session = new WorkoutSession
        {
            Id = WorkoutId,
            Name = WorkoutName,
            Date = WorkoutDate,
            Notes = string.IsNullOrWhiteSpace(WorkoutNotes) ? null : WorkoutNotes,
            Exercises = Exercises.ToList()
        };

        try
        {
            await _database.SaveWorkoutSessionAsync(session);
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed saving workout {Id}", session.Id);
            await Shell.Current.DisplayAlert("Error", "Failed to save workout", "OK");
        }
    }
}