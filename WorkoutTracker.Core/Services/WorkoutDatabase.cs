using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SQLite;
using WorkoutTracker.Models;

namespace WorkoutTracker.Services;

public class WorkoutDatabase : IDisposable, IAsyncDisposable
{
    private readonly SQLiteAsyncConnection _database;
    private readonly ILogger<WorkoutDatabase>? _logger;
    private readonly object _initLock = new();
    private Task? _initTask;
    private bool _disposed;

    public WorkoutDatabase(string dbPath, ILogger<WorkoutDatabase>? logger = null)
    {
        _database = new SQLiteAsyncConnection(dbPath);
        _logger = logger;
    }

    // Tables are created once and every operation awaits that first. A failed attempt
    // is not cached: otherwise one transient problem at startup (a locked file, say)
    // would make every later call fail for the lifetime of the app.
    public async Task InitializeAsync()
    {
        var task = _initTask;
        if (task is null)
        {
            lock (_initLock)
            {
                task = _initTask ??= InitializeCoreAsync();
            }
        }

        try
        {
            await task;
        }
        catch
        {
            lock (_initLock)
            {
                if (ReferenceEquals(_initTask, task))
                    _initTask = null;
            }

            throw;
        }
    }

    private async Task InitializeCoreAsync()
    {
        // Mobile file systems can report SQLITE_BUSY briefly; wait instead of failing.
        await _database.ExecuteScalarAsync<int>("PRAGMA busy_timeout = 3000");
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
        ArgumentNullException.ThrowIfNull(session);

        // Checked before the transaction starts: a throw inside the transaction rolls
        // back after SQLite has already assigned ids to the entity, which is what
        // leaves callers holding ids that no longer exist.
        if (session.Exercises is null)
            throw new ArgumentException("Exercises must not be null; pass an empty list instead.", nameof(session));

        // The editor applies these too; enforcing them here means no other caller can
        // store a workout the app cannot render (a NaN weight used to load back as 0).
        WorkoutRules.Normalize(session);
        var errors = WorkoutRules.Validate(session);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(session));

        try
        {
            await InitializeAsync();

            // Run the save in a single transaction to keep session and exercises consistent
            await _database.RunInTransactionAsync(conn =>
            {
                if (session.Id != 0)
                {
                    // A missing row means the caller is working from stale state. Writing
                    // the exercises anyway would attach them to nothing, so refuse instead.
                    if (conn.Update(session) == 0)
                        throw new InvalidOperationException($"Workout session {session.Id} no longer exists.");
                }
                else
                {
                    conn.Insert(session);
                }

                // At this point, session.Id will be populated for new inserts

                // Load existing exercises for the session
                var existing = conn.Table<Exercise>().Where(e => e.WorkoutSessionId == session.Id).ToList();
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

                    // Update reports 0 rows for an id that no longer exists (a stale id
                    // left by a rolled-back save, for example), and updating a row that
                    // belongs to another session would steal it. Re-target both cases.
                    var owner = exercise.Id == 0 ? null : conn.Find<Exercise>(exercise.Id);
                    if (owner is null || owner.WorkoutSessionId != session.Id)
                    {
                        exercise.Id = 0;
                        conn.Insert(exercise);
                    }
                    else
                    {
                        conn.Update(exercise);
                    }
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

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _database.CloseAsync().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await _database.CloseAsync();
    }
}