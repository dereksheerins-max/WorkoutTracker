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
    private bool _isInitialized;
    private bool _loadFailed;

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

    partial void OnWorkoutIdChanged(int value)
    {
        OnPropertyChanged(nameof(Title));
    }

    // Loading is explicit and awaited by the page: a load that fails must never be
    // mistaken for an empty form, because saving that form would overwrite the
    // existing workout and delete its exercises.
    public async Task InitializeAsync(int? workoutId)
    {
        if (_isInitialized || workoutId is not > 0)
            return;

        _isInitialized = true;
        WorkoutId = workoutId.Value;
        IsLoading = true;

        try
        {
            var session = await _database.GetWorkoutSessionAsync(WorkoutId);
            if (session is null)
            {
                _loadFailed = true;
                await _dialogService.DisplayAlert("Error", "This workout no longer exists.", "OK");
                return;
            }

            WorkoutName = session.Name;
            WorkoutDate = session.Date;
            WorkoutNotes = session.Notes ?? string.Empty;

            // Clear existing to avoid duplicates when re-loading
            Exercises.Clear();
            foreach (var exercise in session.Exercises)
            {
                Exercises.Add(exercise);
            }
        }
        catch (Exception ex)
        {
            _loadFailed = true;
            _logger?.LogError(ex, "Failed to load workout {Id}", WorkoutId);
            await _dialogService.DisplayAlert("Error", "Failed to load workout", "OK");
        }
        finally
        {
            IsLoading = false;
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
        // Never snapshot form state while the load is still running, and never save
        // a form whose workout failed to load: the empty form would overwrite the
        // stored workout and delete its exercises.
        if (IsLoading)
            return;

        if (_loadFailed)
        {
            await _dialogService.DisplayAlert("Error", "This workout could not be loaded. Reopen it before saving.", "OK");
            return;
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