namespace GymTracker.Views;

public partial class AddWorkoutPage : ContentPage
{
    public AddWorkoutPage()
    {
        InitializeComponent();
        BindingContext = App.Services?.GetService<ViewModels.AddWorkoutViewModel>();
    }

    public AddWorkoutPage(ViewModels.AddWorkoutViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm; // This connects the XAML to the ViewModel
    }
}