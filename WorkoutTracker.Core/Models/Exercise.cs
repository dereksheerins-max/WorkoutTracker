using SQLite;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WorkoutTracker.Models;

[Table("exercises")]
public partial class Exercise : ObservableObject
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private int sets;

    [ObservableProperty]
    private int reps;

    [ObservableProperty]
    private double weight;

    [Indexed]
    public int WorkoutSessionId { get; set; }
}