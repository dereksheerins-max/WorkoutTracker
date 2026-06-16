using SQLite;
using GymTracker.Models;

namespace GymTracker.Services;

public class DatabaseService
{
    private SQLiteConnection _database;

    // This constructor initializes the database connection
    public DatabaseService()
    {
        var databasePath = Path.Combine(FileSystem.AppDataDirectory, "GymTracker.db3");
        _database = new SQLiteConnection(databasePath);

        // Create the table if it doesn't exist yet
        _database.CreateTable<Workout>();
    }

    // Save a workout to the DB
    public int SaveWorkout(Workout workout)
    {
        return _database.Insert(workout);
    }

    // Get all workouts from the DB
    public List<Workout> GetWorkouts()
    {
        return _database.Table<Workout>().ToList();
    }
}