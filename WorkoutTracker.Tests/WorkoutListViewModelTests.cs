using Microsoft.Extensions.Logging.Abstractions;
using WorkoutTracker.Models;
using WorkoutTracker.Services;
using WorkoutTracker.ViewModels;

namespace WorkoutTracker.Tests;

public class WorkoutListViewModelTests : IAsyncLifetime
{
    private string _dbPath = string.Empty;
    private WorkoutDatabase _database = null!;
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeNavigationService _navigation = new();
    private readonly FakeFileExportService _export = new();

    public async Task InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"workouts_{Guid.NewGuid()}.db3");
        _database = new WorkoutDatabase(_dbPath, NullLogger<WorkoutDatabase>.Instance);
        await _database.InitializeAsync();
    }

    private WorkoutListViewModel CreateViewModel() =>
        new(_database, _dialogs, _navigation, _export, NullLogger<WorkoutListViewModel>.Instance);

    [Fact]
    public async Task LoadWorkouts_PopulatesSessionsNewestFirst()
    {
        await _database.SaveWorkoutSessionAsync(new WorkoutSession { Name = "Older", Date = DateTime.Today.AddDays(-2) });
        await _database.SaveWorkoutSessionAsync(new WorkoutSession { Name = "Newer", Date = DateTime.Today });
        var viewModel = CreateViewModel();

        await viewModel.LoadWorkoutsCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "Newer", "Older" }, viewModel.WorkoutSessions.Select(s => s.Name));
        Assert.False(viewModel.IsRefreshing);
    }

    [Fact]
    public async Task LoadWorkouts_DatabaseFailure_ShowsErrorAlert()
    {
        var badPath = Path.Combine(Path.GetTempPath(), $"workouts_{Guid.NewGuid()}");
        Directory.CreateDirectory(badPath);
        try
        {
            var db = new WorkoutDatabase(badPath, NullLogger<WorkoutDatabase>.Instance);
            var viewModel = new WorkoutListViewModel(db, _dialogs, _navigation, _export);

            await viewModel.LoadWorkoutsCommand.ExecuteAsync(null);

            Assert.Contains(_dialogs.Alerts, a => a.Title == "Error" && a.Message == "Failed to load workouts");
            Assert.False(viewModel.IsRefreshing);
        }
        finally
        {
            Directory.Delete(badPath);
        }
    }

    [Fact]
    public async Task AddWorkout_NavigatesToAddForm()
    {
        var viewModel = CreateViewModel();

        await viewModel.AddWorkoutCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "Add" }, _navigation.Calls);
    }

    [Fact]
    public async Task EditWorkout_NavigatesToFormWithId()
    {
        var viewModel = CreateViewModel();

        await viewModel.EditWorkoutCommand.ExecuteAsync(new WorkoutSession { Id = 7, Name = "Push Day" });

        Assert.Equal(new[] { "Edit:7" }, _navigation.Calls);
    }

    [Fact]
    public async Task DeleteWorkout_WhenConfirmed_RemovesIt()
    {
        var session = new WorkoutSession { Name = "Trash Me", Date = DateTime.Today };
        await _database.SaveWorkoutSessionAsync(session);
        var viewModel = CreateViewModel();
        await viewModel.LoadWorkoutsCommand.ExecuteAsync(null);
        _dialogs.ConfirmResults.Enqueue(true);

        await viewModel.DeleteWorkoutCommand.ExecuteAsync(viewModel.WorkoutSessions[0]);

        Assert.Empty(viewModel.WorkoutSessions);
        Assert.Single(_dialogs.Confirms);
        Assert.Null(await _database.GetWorkoutSessionAsync(session.Id));
    }

    [Fact]
    public async Task DeleteWorkout_WhenCancelled_KeepsIt()
    {
        var session = new WorkoutSession { Name = "Keep Me", Date = DateTime.Today };
        await _database.SaveWorkoutSessionAsync(session);
        var viewModel = CreateViewModel();
        await viewModel.LoadWorkoutsCommand.ExecuteAsync(null);
        _dialogs.ConfirmResults.Enqueue(false);

        await viewModel.DeleteWorkoutCommand.ExecuteAsync(viewModel.WorkoutSessions[0]);

        Assert.Single(viewModel.WorkoutSessions);
        Assert.NotNull(await _database.GetWorkoutSessionAsync(session.Id));
    }

    [Fact]
    public async Task LoadWorkouts_ConcurrentInvocations_LeaveConsistentStateAndClearSpinner()
    {
        await _database.SaveWorkoutSessionAsync(new WorkoutSession { Name = "Solo", Date = DateTime.Today });
        var viewModel = CreateViewModel();

        var first = viewModel.LoadWorkoutsCommand.ExecuteAsync(null);
        viewModel.IsRefreshing = true; // as a pull-to-refresh gesture would
        var second = viewModel.LoadWorkoutsCommand.ExecuteAsync(null);
        await Task.WhenAll(first, second);

        Assert.False(viewModel.IsRefreshing);
        Assert.Single(viewModel.WorkoutSessions);
        Assert.Equal("Solo", viewModel.WorkoutSessions[0].Name);
    }

    [Fact]
    public async Task LoadWorkouts_ThenLoadMore_AppendsTheNextPageWithoutDuplicates()
    {
        var baseDate = new DateTime(2026, 1, 1);
        var total = WorkoutListViewModel.PageSize + 5;
        for (var i = 0; i < total; i++)
        {
            await _database.SaveWorkoutSessionAsync(new WorkoutSession
            {
                Name = $"S{i:00}",
                Date = baseDate.AddMinutes(i)
            });
        }

        var viewModel = CreateViewModel();
        await viewModel.LoadWorkoutsCommand.ExecuteAsync(null);
        Assert.Equal(WorkoutListViewModel.PageSize, viewModel.WorkoutSessions.Count);
        Assert.Equal("S29", viewModel.WorkoutSessions[0].Name);

        await viewModel.LoadMoreWorkoutsCommand.ExecuteAsync(null);

        Assert.Equal(total, viewModel.WorkoutSessions.Count);
        Assert.Equal(total, viewModel.WorkoutSessions.Select(s => s.Id).Distinct().Count());
        Assert.Equal("S00", viewModel.WorkoutSessions[^1].Name);

        // Asking again once the data is exhausted must not change anything.
        await viewModel.LoadMoreWorkoutsCommand.ExecuteAsync(null);
        Assert.Equal(total, viewModel.WorkoutSessions.Count);
    }

    [Fact]
    public async Task LoadWorkouts_AfterUnreadableDatabase_ShowsRecoveryNoticeOnlyOnce()
    {
        var path = Path.Combine(Path.GetTempPath(), $"workouts_{Guid.NewGuid()}.db3");
        await File.WriteAllTextAsync(path, "not a database");
        var db = new WorkoutDatabase(path, NullLogger<WorkoutDatabase>.Instance);

        try
        {
            var viewModel = new WorkoutListViewModel(db, _dialogs, _navigation, _export);

            await viewModel.LoadWorkoutsCommand.ExecuteAsync(null);
            await viewModel.LoadWorkoutsCommand.ExecuteAsync(null);

            Assert.Single(_dialogs.Alerts, a => a.Title == "Recovered");
        }
        finally
        {
            db.Dispose();
            File.Delete(path);
            foreach (var leftover in Directory.GetFiles(Path.GetTempPath(), $"{Path.GetFileName(path)}.unreadable-*"))
                File.Delete(leftover);
        }
    }

    [Fact]
    public async Task ExportWorkouts_ShouldHandOverEveryWorkoutWithExercises()
    {
        await _database.SaveWorkoutSessionAsync(new WorkoutSession
        {
            Name = "Push Day",
            Date = DateTime.Today,
            Notes = "felt strong",
            Exercises = new List<Exercise> { new() { Name = "Bench Press", Sets = 3, Reps = 5, Weight = 82.5 } }
        });
        var viewModel = CreateViewModel();

        await viewModel.ExportWorkoutsCommand.ExecuteAsync(null);

        var export = Assert.Single(_export.Exports);
        Assert.EndsWith(".json", export.FileName);
        Assert.Contains("Push Day", export.Content);
        Assert.Contains("Bench Press", export.Content);
        Assert.Contains("felt strong", export.Content);
        Assert.Empty(_dialogs.Alerts);
    }

    [Fact]
    public async Task ExportWorkouts_WithNothingStored_ShouldExplainAndNotExport()
    {
        var viewModel = CreateViewModel();

        await viewModel.ExportWorkoutsCommand.ExecuteAsync(null);

        Assert.Empty(_export.Exports);
        Assert.Contains(_dialogs.Alerts, a => a.Message == "There are no workouts to export yet.");
    }

    [Fact]
    public async Task ExportWorkouts_WhenThePlatformStepFails_ShouldReportAnError()
    {
        await _database.SaveWorkoutSessionAsync(new WorkoutSession { Name = "Push Day", Date = DateTime.Today });
        _export.Failure = new InvalidOperationException("share sheet denied");
        var viewModel = CreateViewModel();

        await viewModel.ExportWorkoutsCommand.ExecuteAsync(null);

        Assert.Contains(_dialogs.Alerts, a => a.Title == "Error" && a.Message == "Failed to export workouts");
    }

    public async Task DisposeAsync()
    {
        await _database.DisposeAsync();

        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
