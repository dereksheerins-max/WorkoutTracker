using WorkoutTracker.ViewModels;

namespace WorkoutTracker.Views;

public partial class AddWorkoutPage : ContentPage, IQueryAttributable
{
    private readonly AddWorkoutViewModel _viewModel;

    public AddWorkoutPage(AddWorkoutViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("Id", out var value) && value is not null)
        {
            _viewModel.WorkoutId = Convert.ToInt32(value);
        }
    }
}
