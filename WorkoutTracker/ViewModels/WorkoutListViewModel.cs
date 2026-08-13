using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using WorkoutTracker.Models;
using WorkoutTracker.Services;
using WorkoutTracker.Views;

namespace WorkoutTracker.ViewModels;

public partial class WorkoutListViewModel : ObservableObject
{
    private readonly WorkoutDatabase _database;
    private readonly Microsoft.Extensions.Logging.ILogger<WorkoutListViewModel>? _logger;

    [ObservableProperty]
    private bool _isRefreshing;

    public ObservableCollection<WorkoutSession> WorkoutSessions { get; } = new();

    public WorkoutListViewModel(WorkoutDatabase database, Microsoft.Extensions.Logging.ILogger<WorkoutListViewModel>? logger = null)
    {
        _database = database;
        _logger = logger;
    }

    [RelayCommand]
    private async Task LoadWorkoutsAsync()
    {
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
            await Shell.Current.DisplayAlert("Error", "Failed to load workouts", "OK");
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private async Task AddWorkoutAsync()
    {
        await Shell.Current.GoToAsync(nameof(AddWorkoutPage));
    }

    [RelayCommand]
    private async Task EditWorkoutAsync(WorkoutSession session)
    {
        await Shell.Current.GoToAsync($"{nameof(AddWorkoutPage)}?Id={session.Id}");
    }

    [RelayCommand]
    private async Task DeleteWorkoutAsync(WorkoutSession session)
    {
        if (await Shell.Current.DisplayAlert("Delete", $"Delete {session.Name}?", "Yes", "No"))
        {
            try
            {
                await _database.DeleteWorkoutSessionAsync(session);
                WorkoutSessions.Remove(session);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed deleting workout {Id}", session.Id);
                await Shell.Current.DisplayAlert("Error", "Failed to delete workout", "OK");
            }
        }
    }
}