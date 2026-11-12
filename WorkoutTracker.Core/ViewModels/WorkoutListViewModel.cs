using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using WorkoutTracker.Models;
using WorkoutTracker.Services;

namespace WorkoutTracker.ViewModels;

public partial class WorkoutListViewModel : ObservableObject
{
    public const int PageSize = 25;

    private readonly WorkoutDatabase _database;
    private readonly IDialogService _dialogService;
    private readonly INavigationService _navigationService;
    private readonly Microsoft.Extensions.Logging.ILogger<WorkoutListViewModel>? _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private WorkoutCursor? _cursor;
    private bool _hasMore;

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
            var page = await _database.GetWorkoutSessionsPageAsync(PageSize);

            WorkoutSessions.Clear();
            foreach (var session in page.Sessions)
            {
                WorkoutSessions.Add(session);
            }

            _cursor = page.Next;
            _hasMore = page.HasMore;

            await NotifyIfDatabaseWasQuarantinedAsync();
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

    // Appends the next page; driven by the list reaching its remaining-items threshold.
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadMoreWorkoutsAsync()
    {
        if (!_hasMore || _cursor is null)
            return;

        // A refresh is already running and will repopulate the list from the top.
        if (!await _loadLock.WaitAsync(0))
            return;

        try
        {
            var page = await _database.GetWorkoutSessionsPageAsync(PageSize, _cursor);

            foreach (var session in page.Sessions)
            {
                WorkoutSessions.Add(session);
            }

            _cursor = page.Next;
            _hasMore = page.HasMore;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed loading more workouts");
            await _dialogService.DisplayAlert("Error", "Failed to load more workouts", "OK");
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task NotifyIfDatabaseWasQuarantinedAsync()
    {
        if (_database.TakeQuarantineNotice() is not { } quarantinedPath)
            return;

        await _dialogService.DisplayAlert(
            "Recovered",
            $"Your workout log could not be read, so the file was kept at:\n{quarantinedPath}\n\nA new, empty log has been started.",
            "OK");
    }

    [RelayCommand]
    private async Task AddWorkoutAsync()
    {
        await _navigationService.GoToAddWorkoutAsync();
    }

    [RelayCommand]
    private async Task EditWorkoutAsync(WorkoutSession session)
    {
        if (session is null)
            return;

        await _navigationService.GoToEditWorkoutAsync(session.Id);
    }

    [RelayCommand]
    private async Task DeleteWorkoutAsync(WorkoutSession session)
    {
        if (session is null)
            return;

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
