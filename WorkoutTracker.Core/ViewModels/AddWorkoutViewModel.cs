using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Extensions.Logging;
using WorkoutTracker.Models;
using WorkoutTracker.Services;

namespace WorkoutTracker.ViewModels;

public partial class AddWorkoutViewModel : ObservableObject
{
    private readonly WorkoutDatabase _database;
    private readonly IDialogService _dialogService;
    private readonly INavigationService _navigationService;
    private readonly Microsoft.Extensions.Logging.ILogger<AddWorkoutViewModel>? _logger;
    private Task? _loadTask;

    public Task? LoadingTask => _loadTask;

    public string Title => WorkoutId > 0 ? "Edit Workout" : "Add Workout";

    [ObservableProperty]
    private int _workoutId;

    [ObservableProperty]
    private string _workoutName = string.Empty;

    [ObservableProperty]
    private DateTime _workoutDate = DateTime.Today;

    [ObservableProperty]
    private string _workoutNotes = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<Exercise> Exercises { get; } = new();

    public AddWorkoutViewModel(
        WorkoutDatabase database,
        IDialogService dialogService,
        INavigationService navigationService,
        Microsoft.Extensions.Logging.ILogger<AddWorkoutViewModel>? logger = null)
    {
        _database = database;
        _dialogService = dialogService;
        _navigationService = navigationService;
        _logger = logger;
    }

    // The generated partial can't be async; the helper observes errors and the
    // task is tracked so Save can wait for the load to finish.
    partial void OnWorkoutIdChanged(int value)
    {
        OnPropertyChanged(nameof(Title));

        if (value > 0)
        {
            _loadTask = LoadAndHandleAsync(value);
        }
    }

    private async Task LoadAndHandleAsync(int id)
    {
        IsLoading = true;
        try
        {
            await LoadWorkoutAsync(id);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load workout {Id}", id);
            await _dialogService.DisplayAlert("Error", "Failed to load workout", "OK");
        }
        finally
        {
            IsLoading = false;
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
        // Don't snapshot form state until the initial load has finished
        if (_loadTask != null)
        {
            await _loadTask;
        }

        if (string.IsNullOrWhiteSpace(WorkoutName))
        {
            await _dialogService.DisplayAlert("Error", "Please enter a workout name", "OK");
            return;
        }

        if (WorkoutName.Trim().Length > MaxNameLength)
        {
            await _dialogService.DisplayAlert("Error", $"Workout name must be {MaxNameLength} characters or fewer.", "OK");
            return;
        }

        if (WorkoutNotes.Length > MaxNotesLength)
        {
            await _dialogService.DisplayAlert("Error", $"Notes must be {MaxNotesLength} characters or fewer.", "OK");
            return;
        }

        foreach (var exercise in Exercises)
        {
            if (string.IsNullOrWhiteSpace(exercise.Name))
            {
                await _dialogService.DisplayAlert("Error", "All exercises must have a name.", "OK");
                return;
            }

            if (exercise.Name.Trim().Length > MaxExerciseNameLength)
            {
                await _dialogService.DisplayAlert("Error", $"Exercise names must be {MaxExerciseNameLength} characters or fewer.", "OK");
                return;
            }

            if (exercise.Sets < 1)
            {
                await _dialogService.DisplayAlert("Error", "All exercises must have at least one set.", "OK");
                return;
            }

            if (exercise.Reps < 1)
            {
                await _dialogService.DisplayAlert("Error", "All exercises must have at least one rep.", "OK");
                return;
            }

            if (exercise.Weight < 0)
            {
                await _dialogService.DisplayAlert("Error", "Exercise weight cannot be negative.", "OK");
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
            await _navigationService.GoBackAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed saving workout {Id}", session.Id);
            await _dialogService.DisplayAlert("Error", "Failed to save workout", "OK");
        }
    }
}