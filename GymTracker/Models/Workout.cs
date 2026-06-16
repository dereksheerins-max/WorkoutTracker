using SQLite;

namespace GymTracker.Models;

// This defines the types of machines available
public enum ActivityType
{
    CrossTrainer,
    Treadmill,
    RowingMachine,
    Bicycle
}

// This is the table structure for your database
public class Workout
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public ActivityType Type { get; set; } // Tells us if it was a Treadmill, etc.
    public double Time { get; set; }       // Minutes or Seconds
    public int MaxHeartRate { get; set; }
    public double CaloriesBurned { get; set; }
    public double Distance { get; set; }
    public DateTime DateCreated { get; set; } = DateTime.Now;
}