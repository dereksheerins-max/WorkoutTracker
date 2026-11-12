using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SQLite;
using WorkoutTracker.Models;

namespace WorkoutTracker.Services;

public class WorkoutDatabase : IDisposable
{
    private readonly SQLiteAsyncConnection _database;
    private readonly ILogger<WorkoutDatabase>? _logger;
    private readonly Task _initTask;

    public WorkoutDatabase(string dbPath, ILogger<WorkoutDatabase>? logger = null)
    {
        _database = new SQLiteAsyncConnection(dbPath);
        _logger = logger;
        _initTask = InitializeCoreAsync();
    }

    // Tables are created once in the background; every operation awaits this first.
    public Task InitializeAsync() => _initTask;

    private async Task InitializeCoreAsync()
    {
        await _database.CreateTableAsync<WorkoutSession>();
        await _database.CreateTableAsync<Exercise>();
    }

    public async Task<List<WorkoutSession>> GetWorkoutSessionsAsync()
    {
        try
        {
            await InitializeAsync();

            var sessions = await _database.Table<WorkoutSession>()
                .OrderByDescending(s => s.Date)
                .ToListAsync();

            if (sessions.Count == 0)
                return sessions;

            // Batch load exercises to avoid N+1 queries
            var ids = sessions.Select(s => s.Id).ToList();
            var exercises = await _database.Table<Exercise>()
                .Where(e => ids.Contains(e.WorkoutSessionId))
                .ToListAsync();

            var bySession = exercises.GroupBy(e => e.WorkoutSessionId)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var session in sessions)
            {
                bySession.TryGetValue(session.Id, out var list);
                session.Exercises = list ?? new List<Exercise>();
            }

            return sessions;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error loading workout sessions");
            throw;
        }
    }

    public async Task<WorkoutSession?> GetWorkoutSessionAsync(int id)
    {
        try
        {
            await InitializeAsync();

            var session = await _database.Table<WorkoutSession>()
                .FirstOrDefaultAsync(s => s.Id == id);

            if (session != null)
            {
                session.Exercises = await _database.Table<Exercise>()
                    .Where(e => e.WorkoutSessionId == session.Id)
                    .ToListAsync();
            }

            return session;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error loading workout session {Id}", id);
            throw;
        }
    }

    public async Task<int> SaveWorkoutSessionAsync(WorkoutSession session)
    {
        try
        {
            await InitializeAsync();

            // Run the save in a single transaction to keep session and exercises consistent
            await _database.RunInTransactionAsync(conn =>
            {
                if (session.Id != 0)
                {
                    conn.Update(session);
                }
                else
                {
                    conn.Insert(session);
                }

                // At this point, session.Id will be populated for new inserts

                // Load existing exercises for the session
                var existing = conn.Table<Exercise>().Where(e => e.WorkoutSessionId == session.Id).ToList();
                var existingIds = existing.Select(e => e.Id).ToHashSet();
                var newIds = session.Exercises.Select(e => e.Id).Where(i => i != 0).ToHashSet();

                // Delete any exercises that existed but were removed in the UI
                foreach (var ex in existing)
                {
                    if (!newIds.Contains(ex.Id))
                        conn.Delete(ex);
                }

                // Insert or update provided exercises
                foreach (var exercise in session.Exercises)
                {
                    exercise.WorkoutSessionId = session.Id;
                    if (exercise.Id != 0)
                        conn.Update(exercise);
                    else
                        conn.Insert(exercise);
                }
            });

            return session.Id;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error saving workout session {Id}", session.Id);
            throw;
        }
    }

    public async Task DeleteWorkoutSessionAsync(WorkoutSession session)
    {
        try
        {
            await InitializeAsync();

            await _database.RunInTransactionAsync(conn =>
            {
                conn.Table<Exercise>().Delete(e => e.WorkoutSessionId == session.Id);
                conn.Delete(session);
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error deleting workout session {Id}", session.Id);
            throw;
        }
    }

    public Task CloseAsync() => _database.CloseAsync();

    public void Dispose() => CloseAsync().GetAwaiter().GetResult();
}