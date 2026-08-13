using SQLite;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WorkoutTracker.Models;

[Table("workout_sessions")]
public partial class WorkoutSession : ObservableObject
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private DateTime date;

    [ObservableProperty]
    private string? notes;

    [Ignore]
    public List<Exercise> Exercises { get; set; } = new();
}