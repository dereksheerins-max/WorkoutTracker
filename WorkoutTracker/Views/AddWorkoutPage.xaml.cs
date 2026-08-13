using WorkoutTracker.ViewModels;

namespace WorkoutTracker.Views;

public partial class AddWorkoutPage : ContentPage
{
    public AddWorkoutPage(AddWorkoutViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}