using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymTracker.Models;
using GymTracker.Services;
using System.Windows.Input;

namespace GymTracker.ViewModels;

public partial class AddWorkoutViewModel : ObservableObject
{
    private readonly DatabaseService _dbService;

    [ObservableProperty]
    private Workout _currentWorkout = new();

    // This property helps the UI know which exercise was picked in the Picker
    [ObservableProperty]
    private ActivityType _selectedType;

    public List<ActivityType> AvailableTypes { get; } = new()
    {
        ActivityType.CrossTrainer,
        ActivityType.Treadmill,
        ActivityType.RowingMachine,
        ActivityType.Bicycle
    };

    public AddWorkoutViewModel(DatabaseService dbService)
    {
        _dbService = dbService;
        // Default the selection to CrossTrainer
        _selectedType = ActivityType.CrossTrainer;
    }

    [[RelayCommand]
private async Task SaveWorkout()
{
    // Basic Validation: Ensure values are greater than zero
    if (CurrentWorkout.Time <= 0 || CurrentWorkout.MaxHeartRate == 0)
    {
        await Shell.Current.DisplayAlert("Input Error", "Please enter valid numbers for Time and Heart Rate.",
"OK");
        return;
    }

    if (CurrentWorkout.Calories <= 0 || CurrentWorkout.Distance < 0)
    {
        await Shell.Current.DisplayAlert("Input Error", "Please enter positive values for Calories and Distance.",
"OK");
        return;
    }

    // If validation passes, save it
    CurrentWorkout.Type = SelectedType;
    _dbService.SaveWorkout(CurrentWorkout);

    await Shell.Current.DisplayAlert("Success", "Workout saved to your log!", "OK");

    // Optional: Navigate back or clear the form here
    }
}