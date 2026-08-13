using WorkoutTracker.ViewModels;

namespace WorkoutTracker.Views;

public partial class WorkoutListPage : ContentPage
{
    public WorkoutListPage(WorkoutListViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is WorkoutListViewModel viewModel)
        {
            viewModel.LoadWorkoutsCommand.Execute(null);
        }
    }
}