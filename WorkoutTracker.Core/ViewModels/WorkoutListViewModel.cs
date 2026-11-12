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
    private readonly IFileExportService _exportService;
    private readonly IFileImportService _importService;
    private readonly ILogger<WorkoutListViewModel>? _logger;
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
        IFileExportService exportService,
        IFileImportService importService,
        ILogger<WorkoutListViewModel>? logger = null)
    {
        _database = database;
        _dialogService = dialogService;
        _navigationService = navigationService;
        _exportService = exportService;
        _importService = importService;
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
            await LoadFirstPageAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed loading workouts");
            await _dialogService.ShowAlertAsync("Error", "Failed to load workouts", "OK");
        }
        finally
        {
            IsRefreshing = false;
            _loadLock.Release();
        }
    }

    // Assumes the load lock is held.
    private async Task LoadFirstPageAsync()
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
            await _dialogService.ShowAlertAsync("Error", "Failed to load more workouts", "OK");
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

        await _dialogService.ShowAlertAsync(
            "Recovered",
            $"Your workout log could not be read, so the file was kept at:\n{quarantinedPath}\n\nA new, empty log has been started.",
            "OK");
    }

    [RelayCommand]
    private async Task AddWorkoutAsync()
    {
        await _navigationService.GoToAddWorkoutAsync();
    }

    // The only way data leaves the app: the database is excluded from device backups,
    // so a lost device loses the history unless the user exports it.
    [RelayCommand]
    private async Task ExportWorkoutsAsync()
    {
        try
        {
            var sessions = await _database.GetWorkoutSessionsWithExercisesAsync();
            if (sessions.Count == 0)
            {
                await _dialogService.ShowAlertAsync("Export", "There are no workouts to export yet.", "OK");
                return;
            }

            var fileName = $"workouts-{DateTime.Now:yyyyMMdd-HHmm}.json";
            await _exportService.ExportAsync(fileName, WorkoutExport.ToJson(sessions));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed exporting workouts");
            await _dialogService.ShowAlertAsync("Error", "Failed to export workouts", "OK");
        }
    }

    // Restore path. Imported workouts are added alongside what is already stored; the
    // confirmation step exists because it is the one action that adds a batch of data.
    [RelayCommand]
    private async Task ImportWorkoutsAsync()
    {
        try
        {
            var content = await _importService.PickTextAsync();
            if (content is null)
                return;

            var parsed = WorkoutExport.Parse(content);
            if (!parsed.Succeeded)
            {
                await _dialogService.ShowAlertAsync("Import", parsed.Error ?? "That file is not a valid workout export.", "OK");
                return;
            }

            if (parsed.Sessions.Count == 0)
            {
                await _dialogService.ShowAlertAsync("Import", "That file does not contain any workouts.", "OK");
                return;
            }

            var question = parsed.Sessions.Count == 1
                ? "Add 1 workout from this file?"
                : $"Add {parsed.Sessions.Count} workouts from this file?";

            if (!await _dialogService.ConfirmAsync("Restore", question, "Restore", "Cancel"))
                return;

            var imported = await _database.ImportWorkoutSessionsAsync(parsed.Sessions);

            // Wait for the lock rather than skipping: the list must show the restored
            // workouts as soon as this reports success.
            await _loadLock.WaitAsync();
            try
            {
                await LoadFirstPageAsync();
            }
            finally
            {
                _loadLock.Release();
            }

            await _dialogService.ShowAlertAsync(
                "Import",
                imported == 1 ? "Restored 1 workout." : $"Restored {imported} workouts.",
                "OK");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed importing workouts");
            await _dialogService.ShowAlertAsync("Error", "Failed to import workouts", "OK");
        }
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

        if (await _dialogService.ConfirmAsync("Delete", $"Delete {session.Name}?", "Yes", "No"))
        {
            try
            {
                await _database.DeleteWorkoutSessionAsync(session);
                WorkoutSessions.Remove(session);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed deleting workout {Id}", session.Id);
                await _dialogService.ShowAlertAsync("Error", "Failed to delete workout", "OK");
            }
        }
    }
}
