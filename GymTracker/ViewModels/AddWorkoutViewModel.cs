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

    [RelayCommand]
    private async Task SaveWorkout()
    {
        // Validation: Ensure required fields aren't empty/zero before saving
        if (CurrentWorkout.Time > 0 && CurrentWorkout.MaxHeartRate > 0)
        {
            // Update the type from the picker selection
            CurrentWorkout.Type = SelectedType;

            // Save to Database
            _dbService.SaveWorkout(CurrentWorkout);

            // Success! (We will add navigation logic here later)
            await Shell.Current.DisplayAlert("Success", "Workout saved!", "OK");
        }
        else
        {
            await Shell.Current.DisplayAlert("Error", "Please fill in all fields.", "OK");
        }
    }
}