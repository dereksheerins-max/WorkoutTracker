using CommunityToolkit.Mvvm.ComponentModel;
using GymTracker.Models;
using GymTracker.Services;
using System.Collections.ObjectModel;

namespace GymTracker.ViewModels;

public partial class MainViewModel : ObservableObject
{
    // This is the "Service" injected from MauiProgram.cs
    private readonly DatabaseService _dbService;

    // This holds the list of workouts we show on the screen
    [ObservableProperty]
    private ObservableCollection<Workout> _workouts = new();

    public MainViewModel(DatabaseService dbService)
    {
        _dbService = dbService;
        _workouts = new ObservableCollection<Workout>(_dbService.GetWorkouts());
    }

    // Example method to add a workout
    public void AddWorkout(Workout newWorkout)
    {
        _dbService.SaveWorkout(newWorkout);
    Workouts.Add(newWorkout); // This updates the UI automatically!
    }
}