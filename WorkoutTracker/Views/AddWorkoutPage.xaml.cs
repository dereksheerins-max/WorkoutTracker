using WorkoutTracker.ViewModels;

namespace WorkoutTracker.Views;

public partial class AddWorkoutPage : ContentPage, IQueryAttributable
{
    private readonly AddWorkoutViewModel _viewModel;
    private int? _workoutId;
    private bool _initialized;

    public AddWorkoutPage(AddWorkoutViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    // Typed accessor so item-template bindings can reach the commands by path
    // instead of walking the ancestor tree.
    public AddWorkoutViewModel ViewModel => _viewModel;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _workoutId = query.TryGetValue("Id", out var value)
                     && int.TryParse(value?.ToString(), out var id) && id > 0
            ? id
            : null;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_initialized)
            return;

        _initialized = true;
        await _viewModel.InitializeAsync(_workoutId);
    }
}
