using Microsoft.Extensions.Logging.Abstractions;
using WorkoutTracker.Models;
using WorkoutTracker.Services;
using WorkoutTracker.ViewModels;

namespace WorkoutTracker.Tests;

public class AddWorkoutViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly WorkoutDatabase _database;
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeNavigationService _navigation = new();

    public AddWorkoutViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"workouts_{Guid.NewGuid()}.db3");
        _database = new WorkoutDatabase(_dbPath, NullLogger<WorkoutDatabase>.Instance);
        _database.InitializeAsync().GetAwaiter().GetResult();
    }

    private AddWorkoutViewModel CreateViewModel() =>
        new(_database, _dialogs, _navigation, NullLogger<AddWorkoutViewModel>.Instance);

    // The list API is paged; these assertions only need everything that is stored.
    private async Task<List<WorkoutSession>> LoadAllAsync()
    {
        var all = new List<WorkoutSession>();
        WorkoutCursor? cursor = null;

        while (true)
        {
            var page = await _database.GetWorkoutSessionsPageAsync(50, cursor);
            all.AddRange(page.Sessions);

            if (!page.HasMore)
                return all;

            cursor = page.Next;
        }
    }

    private AddWorkoutViewModel CreateValidViewModel()
    {
        var viewModel = CreateViewModel();
        viewModel.WorkoutName = "Push Day";
        viewModel.Exercises.Add(new Exercise { Name = "Bench Press", Sets = 3, Reps = 5, Weight = 80 });
        return viewModel;
    }

    [Fact]
    public async Task Initialize_WithExistingId_LoadsWorkoutIntoForm()
    {
        var session = new WorkoutSession
        {
            Name = "Leg Day",
            Date = new DateTime(2026, 1, 15),
            Notes = "Felt strong",
            Exercises = new List<Exercise> { new() { Name = "Squat", Sets = 5, Reps = 5, Weight = 100 } }
        };
        await _database.SaveWorkoutSessionAsync(session);
        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync(session.Id);

        Assert.Equal("Leg Day", viewModel.WorkoutName);
        Assert.Equal(new DateTime(2026, 1, 15), viewModel.WorkoutDate);
        Assert.Equal("Felt strong", viewModel.WorkoutNotes);
        var exercise = Assert.Single(viewModel.Exercises);
        Assert.Equal("Squat", exercise.Name);
        Assert.Equal(5, exercise.Sets);
    }

    [Fact]
    public async Task LoadFailure_ShowsErrorAlert()
    {
        var badPath = Path.Combine(Path.GetTempPath(), $"workouts_{Guid.NewGuid()}");
        Directory.CreateDirectory(badPath);
        try
        {
            var db = new WorkoutDatabase(badPath, NullLogger<WorkoutDatabase>.Instance);
            var viewModel = new AddWorkoutViewModel(db, _dialogs, _navigation);

            await viewModel.InitializeAsync(1);

            Assert.Contains(_dialogs.Alerts, a => a.Title == "Error" && a.Message == "Failed to load workout");
            Assert.False(viewModel.IsLoading);
        }
        finally
        {
            Directory.Delete(badPath);
        }
    }

    [Fact]
    public async Task Save_WithEmptyName_ShowsErrorAndDoesNotSave()
    {
        var viewModel = CreateViewModel();
        viewModel.WorkoutName = "   ";

        await viewModel.SaveWorkoutCommand.ExecuteAsync(null);

        Assert.Contains(_dialogs.Alerts, a => a.Message == "Please enter a workout name");
        Assert.Empty(_navigation.Calls);
        Assert.Empty(await LoadAllAsync());
    }

    [Fact]
    public async Task Save_WithOverlongName_ShowsError()
    {
        var viewModel = CreateViewModel();
        viewModel.WorkoutName = new string('x', 101);

        await viewModel.SaveWorkoutCommand.ExecuteAsync(null);

        Assert.Contains(_dialogs.Alerts, a => a.Message.Contains("100 characters or fewer"));
    }

    [Fact]
    public async Task Save_WithOverlongNotes_ShowsError()
    {
        var viewModel = CreateValidViewModel();
        viewModel.WorkoutNotes = new string('x', 501);

        await viewModel.SaveWorkoutCommand.ExecuteAsync(null);

        Assert.Contains(_dialogs.Alerts, a => a.Message.Contains("500 characters or fewer"));
    }

    [Fact]
    public async Task Save_WithEmptyExerciseName_ShowsError()
    {
        var viewModel = CreateValidViewModel();
        viewModel.Exercises[0].Name = " ";

        await viewModel.SaveWorkoutCommand.ExecuteAsync(null);

        Assert.Contains(_dialogs.Alerts, a => a.Message == "All exercises must have a name.");
        Assert.Empty(_navigation.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Save_WithNonPositiveSets_ShowsError(int sets)
    {
        var viewModel = CreateValidViewModel();
        viewModel.Exercises[0].Sets = sets;

        await viewModel.SaveWorkoutCommand.ExecuteAsync(null);

        Assert.Contains(_dialogs.Alerts, a => a.Message == "All exercises must have at least one set.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Save_WithNonPositiveReps_ShowsError(int reps)
    {
        var viewModel = CreateValidViewModel();
        viewModel.Exercises[0].Reps = reps;

        await viewModel.SaveWorkoutCommand.ExecuteAsync(null);

        Assert.Contains(_dialogs.Alerts, a => a.Message == "All exercises must have at least one rep.");
    }

    [Fact]
    public async Task Save_WithNegativeWeight_ShowsError()
    {
        var viewModel = CreateValidViewModel();
        viewModel.Exercises[0].Weight = -2.5;

        await viewModel.SaveWorkoutCommand.ExecuteAsync(null);

        Assert.Contains(_dialogs.Alerts, a => a.Message == "Exercise weight cannot be negative.");
    }

    [Fact]
    public async Task Save_NewWorkout_PersistsAndGoesBack()
    {
        var viewModel = CreateValidViewModel();

        await viewModel.SaveWorkoutCommand.ExecuteAsync(null);

        var saved = Assert.Single(await LoadAllAsync());
        Assert.Equal("Push Day", saved.Name);
        Assert.Equal(1, saved.ExerciseCount);

        // The list query only counts exercises; the editor's query returns them.
        var stored = Assert.Single((await _database.GetWorkoutSessionAsync(saved.Id))!.Exercises);
        Assert.Equal("Bench Press", stored.Name);
        Assert.Equal(new[] { "Back" }, _navigation.Calls);
    }

    [Fact]
    public async Task Save_Edit_UpdatesAndGoesBack()
    {
        var session = new WorkoutSession
        {
            Name = "Old Name",
            Date = DateTime.Today,
            Exercises = new List<Exercise> { new() { Name = "Row", Sets = 3, Reps = 10, Weight = 40 } }
        };
        await _database.SaveWorkoutSessionAsync(session);
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync(session.Id);
        viewModel.WorkoutName = "New Name";
        viewModel.Exercises[0].Weight = 45;

        await viewModel.SaveWorkoutCommand.ExecuteAsync(null);

        var saved = Assert.Single(await LoadAllAsync());
        Assert.Equal("New Name", saved.Name);
        Assert.Equal(45, Assert.Single((await _database.GetWorkoutSessionAsync(saved.Id))!.Exercises).Weight);
        Assert.Equal(new[] { "Back" }, _navigation.Calls);
    }

    [Fact]
    public async Task Title_ReflectsAddVersusEditMode()
    {
        var viewModel = CreateViewModel();
        Assert.Equal("Add Workout", viewModel.Title);

        var session = new WorkoutSession { Name = "Leg Day", Date = DateTime.Today };
        await _database.SaveWorkoutSessionAsync(session);

        await viewModel.InitializeAsync(session.Id);

        Assert.Equal("Edit Workout", viewModel.Title);
    }

    [Fact]
    public async Task Save_AfterLoadFailure_DoesNotOverwriteStoredWorkout()
    {
        var badPath = Path.Combine(Path.GetTempPath(), $"workouts_{Guid.NewGuid()}");
        Directory.CreateDirectory(badPath);
        try
        {
            var db = new WorkoutDatabase(badPath, NullLogger<WorkoutDatabase>.Instance);
            var viewModel = new AddWorkoutViewModel(db, _dialogs, _navigation);
            await viewModel.InitializeAsync(7);
            _dialogs.Alerts.Clear();

            // The form looks empty; a user could retype a name and hit Save.
            viewModel.WorkoutName = "Retyped";
            await viewModel.SaveWorkoutCommand.ExecuteAsync(null);

            Assert.Contains(_dialogs.Alerts,
                a => a.Message == "This workout could not be loaded. Reopen it before saving.");
            Assert.Empty(_navigation.Calls);
        }
        finally
        {
            Directory.Delete(badPath);
        }
    }

    [Fact]
    public async Task Save_WhenStoredWorkoutWasDeleted_ShowsSaveErrorAndKeepsNothing()
    {
        var session = new WorkoutSession
        {
            Name = "Gone",
            Date = DateTime.Today,
            Exercises = new List<Exercise> { new() { Name = "Squat", Sets = 3, Reps = 5, Weight = 100 } }
        };
        await _database.SaveWorkoutSessionAsync(session);
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync(session.Id);

        // Row removed behind the editor (e.g. deleted from the list on another page).
        await _database.DeleteWorkoutSessionAsync(session);

        await viewModel.SaveWorkoutCommand.ExecuteAsync(null);

        Assert.Contains(_dialogs.Alerts, a => a.Title == "Error" && a.Message == "Failed to save workout");
        Assert.Empty(_navigation.Calls);
        Assert.Empty(await LoadAllAsync());
    }

    [Fact]
    public void AddAndRemoveExercise_ManagesCollection()
    {
        var viewModel = CreateViewModel();

        viewModel.AddExerciseCommand.Execute(null);
        var exercise = Assert.Single(viewModel.Exercises);
        Assert.Equal("New Exercise", exercise.Name);

        viewModel.RemoveExerciseCommand.Execute(exercise);
        Assert.Empty(viewModel.Exercises);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public async Task Save_WithNonFiniteWeight_ShowsError(double weight)
    {
        var viewModel = CreateValidViewModel();
        viewModel.Exercises[0].Weight = weight;

        await viewModel.SaveWorkoutCommand.ExecuteAsync(null);

        Assert.Contains(_dialogs.Alerts, a => a.Message == "Exercise weight must be a finite number.");
        Assert.Empty(_navigation.Calls);
        Assert.Empty(await LoadAllAsync());
    }

    [Fact]
    public async Task Save_TrimsWhitespaceBeforePersisting()
    {
        var viewModel = CreateViewModel();
        viewModel.WorkoutName = "  Push Day  ";
        viewModel.WorkoutNotes = "   notes   ";
        viewModel.Exercises.Add(new Exercise { Name = "  Bench Press  ", Sets = 3, Reps = 5, Weight = 80 });

        await viewModel.SaveWorkoutCommand.ExecuteAsync(null);

        var saved = Assert.Single(await LoadAllAsync());
        Assert.Equal("Push Day", saved.Name);
        Assert.Equal("notes", saved.Notes);
        Assert.Equal("Bench Press",
            Assert.Single((await _database.GetWorkoutSessionAsync(saved.Id))!.Exercises).Name);
    }

    public void Dispose()
    {
        _database.Dispose();

        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
