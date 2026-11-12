using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using WorkoutTracker.Models;
using WorkoutTracker.Services;

namespace WorkoutTracker.ViewModels;

public partial class WorkoutListViewModel : ObservableObject
{
    private readonly WorkoutDatabase _database;
    private readonly IDialogService _dialogService;
    private readonly INavigationService _navigationService;
    private readonly Microsoft.Extensions.Logging.ILogger<WorkoutListViewModel>? _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    [ObservableProperty]
    private bool _isRefreshing;

    public ObservableCollection<WorkoutSession> WorkoutSessions { get; } = new();

    public WorkoutListViewModel(
        WorkoutDatabase database,
        IDialogService dialogService,
        INavigationService navigationService,
        Microsoft.Extensions.Logging.ILogger<WorkoutListViewModel>? logger = null)
    {
        _database = database;
        _dialogService = dialogService;
        _navigationService = navigationService;
        _logger = logger;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadWorkoutsAsync()
    {
        // A pull-to-refresh can arrive while the OnAppearing load is still running.
        // Skipping it is fine, but the spinner must be cleared either way.
        if (!await _loadLock.WaitAsync(0))
        {
            IsRefreshing = false;
            return;
        }

        IsRefreshing = true;
        try
        {
            var sessions = await _database.GetWorkoutSessionsAsync();
            WorkoutSessions.Clear();
            foreach (var session in sessions)
            {
                WorkoutSessions.Add(session);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed loading workouts");
            await _dialogService.DisplayAlert("Error", "Failed to load workouts", "OK");
        }
        finally
        {
            IsRefreshing = false;
            _loadLock.Release();
        }
    }

    [RelayCommand]
    private async Task AddWorkoutAsync()
    {
        await _navigationService.GoToAddWorkoutAsync();
    }

    [RelayCommand]
    private async Task EditWorkoutAsync(WorkoutSession session)
    {
        await _navigationService.GoToEditWorkoutAsync(session.Id);
    }

    [RelayCommand]
    private async Task DeleteWorkoutAsync(WorkoutSession session)
    {
        if (await _dialogService.DisplayAlert("Delete", $"Delete {session.Name}?", "Yes", "No"))
        {
            try
            {
                await _database.DeleteWorkoutSessionAsync(session);
                WorkoutSessions.Remove(session);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed deleting workout {Id}", session.Id);
                await _dialogService.DisplayAlert("Error", "Failed to delete workout", "OK");
            }
        }
    }
}