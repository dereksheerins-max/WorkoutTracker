namespace GymTracker.Views;

using Microsoft.Extensions.DependencyInjection;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
    }

    private async void OnAddWorkoutClicked(object sender, EventArgs e)
    {
        // Resolve the AddWorkoutPage (and its ViewModel) from DI and navigate to it
        var page = App.Services?.GetService<AddWorkoutPage>();
        if (page != null)
        {
            await Navigation.PushAsync(page);
            return;
        }

        // Fallback: resolve the ViewModel and construct the page
        var vm = App.Services?.GetService<ViewModels.AddWorkoutViewModel>();
        if (vm != null)
            await Navigation.PushAsync(new AddWorkoutPage(vm));
    }
}

