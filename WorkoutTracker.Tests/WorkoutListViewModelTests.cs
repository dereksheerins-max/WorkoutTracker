using Microsoft.Extensions.Logging.Abstractions;
using WorkoutTracker.Models;
using WorkoutTracker.Services;
using WorkoutTracker.ViewModels;

namespace WorkoutTracker.Tests;

public class WorkoutListViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly WorkoutDatabase _database;
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeNavigationService _navigation = new();

    public WorkoutListViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"workouts_{Guid.NewGuid()}.db3");
        _database = new WorkoutDatabase(_dbPath, NullLogger<WorkoutDatabase>.Instance);
        _database.InitializeAsync().GetAwaiter().GetResult();
    }

    private WorkoutListViewModel CreateViewModel() =>
        new(_database, _dialogs, _navigation, NullLogger<WorkoutListViewModel>.Instance);

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
            var viewModel = new WorkoutListViewModel(db, _dialogs, _navigation);

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

    public void Dispose()
    {
        _database.Dispose();

        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
