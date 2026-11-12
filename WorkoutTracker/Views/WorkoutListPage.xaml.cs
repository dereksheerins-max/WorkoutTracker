using WorkoutTracker.ViewModels;

namespace WorkoutTracker.Views;

public partial class WorkoutListPage : ContentPage
{
    private readonly WorkoutListViewModel _viewModel;

    public WorkoutListPage(WorkoutListViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    // Typed accessor so item-template bindings can reach the commands by path
    // instead of walking the ancestor tree.
    public WorkoutListViewModel ViewModel => _viewModel;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadWorkoutsCommand.Execute(null);
    }
}
