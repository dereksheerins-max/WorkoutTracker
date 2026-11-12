using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SQLite;
using WorkoutTracker.Models;

namespace WorkoutTracker.Services;

public class WorkoutDatabase : IDisposable, IAsyncDisposable
{
    // Bump when the shape of the data changes and add the matching step in MigrateAsync.
    private const int SchemaVersion = 1;

    private readonly SQLiteAsyncConnection _database;
    private readonly string _dbPath;
    private readonly ILogger<WorkoutDatabase>? _logger;
    private readonly object _initLock = new();
    private Task? _initTask;
    private string? _quarantinedDatabasePath;
    private bool _disposed;

    public WorkoutDatabase(string dbPath, ILogger<WorkoutDatabase>? logger = null)
    {
        _dbPath = dbPath;
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

    // Returns the path of a database file that was unreadable and moved aside, once.
    public string? TakeQuarantineNotice() => Interlocked.Exchange(ref _quarantinedDatabasePath, null);

    private async Task InitializeCoreAsync()
    {
        // Mobile file systems can report SQLITE_BUSY briefly; wait instead of failing.
        await _database.ExecuteScalarAsync<int>("PRAGMA busy_timeout = 3000");

        try
        {
            await MigrateAsync();
        }
        catch (SQLiteException ex) when (ex.Result is SQLite3.Result.Corrupt or SQLite3.Result.NonDBFile)
        {
            // Otherwise every screen fails for the life of the app. The old file is
            // moved aside, never deleted, so the data can still be recovered by hand.
            await QuarantineUnreadableDatabaseAsync(ex);
            await MigrateAsync();
        }
    }

    private async Task MigrateAsync()
    {
        // Reading the pragma touches the file header, so a corrupt file fails here
        // instead of on whichever query happens to run first.
        var version = await _database.ExecuteScalarAsync<int>("PRAGMA user_version");

        // Idempotent by design: CreateTableAsync adds columns missing from older files.
        await _database.CreateTableAsync<WorkoutSession>();
        await _database.CreateTableAsync<Exercise>();

        // Created explicitly because CreateTableAsync only creates indexes when it
        // creates the table, which leaves older files without them.
        await _database.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS idx_workout_sessions_date ON workout_sessions (Date)");

        if (version < SchemaVersion)
        {
            await _database.ExecuteAsync($"PRAGMA user_version = {SchemaVersion}");
            _logger?.LogInformation("Migrated workout database from schema {From} to {To}", version, SchemaVersion);
        }
    }

    private async Task QuarantineUnreadableDatabaseAsync(SQLiteException cause)
    {
        await _database.CloseAsync();

        if (File.Exists(_dbPath))
        {
            var quarantinePath = $"{_dbPath}.unreadable-{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Move(_dbPath, quarantinePath);
            _quarantinedDatabasePath = quarantinePath;
        }

        _logger?.LogWarning(cause, "Workout database was unreadable and has been moved aside");
    }

    // Pages are keyset based: one page binds at most pageSize + 1 parameters, so the
    // IN (...) clause can no longer hit SQLite's variable limit as history grows.
    public async Task<WorkoutSessionsPage> GetWorkoutSessionsPageAsync(int pageSize, WorkoutCursor? after = null)
    {
        if (pageSize < 1)
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "Page size must be at least one.");

        try
        {
            await InitializeAsync();

            var query = _database.Table<WorkoutSession>();
            if (after is not null)
            {
                var cursorDate = after.Date;
                var cursorId = after.Id;
                query = query.Where(s => s.Date < cursorDate || (s.Date == cursorDate && s.Id < cursorId));
            }

            // One extra row tells us whether another page exists without a count query.
            var rows = await query
                .OrderByDescending(s => s.Date)
                .ThenByDescending(s => s.Id)
                .Take(pageSize + 1)
                .ToListAsync();

            var hasMore = rows.Count > pageSize;
            if (hasMore)
                rows.RemoveAt(rows.Count - 1);

            await PopulateExerciseCountsAsync(rows);

            return new WorkoutSessionsPage(rows, hasMore ? new WorkoutCursor(rows[^1].Date, rows[^1].Id) : null);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error loading workout sessions");
            throw;
        }
    }

    private async Task PopulateExerciseCountsAsync(List<WorkoutSession> sessions)
    {
        if (sessions.Count == 0)
            return;

        var ids = sessions.Select(s => s.Id).ToList();

        // Only the placeholders are interpolated; the ids themselves are bound.
        var placeholders = string.Join(",", ids.Select(_ => "?"));
        var sql = "SELECT WorkoutSessionId AS SessionId, COUNT(*) AS Total FROM exercises " +
                  $"WHERE WorkoutSessionId IN ({placeholders}) GROUP BY WorkoutSessionId";

        var counts = await _database.QueryAsync<ExerciseCountRow>(sql, ids.Cast<object>().ToArray());
        var byId = counts.ToDictionary(c => c.SessionId, c => c.Total);

        foreach (var session in sessions)
        {
            session.ExerciseCount = byId.TryGetValue(session.Id, out var total) ? total : 0;
        }
    }

    // Export needs every workout with its exercises. It pages through the list and
    // batch-loads the exercises per page, so the query count grows with pages, not rows.
    public async Task<List<WorkoutSession>> GetWorkoutSessionsWithExercisesAsync()
    {
        const int pageSize = 50;

        try
        {
            var all = new List<WorkoutSession>();
            WorkoutCursor? cursor = null;

            do
            {
                var page = await GetWorkoutSessionsPageAsync(pageSize, cursor);
                await PopulateExercisesAsync(page.Sessions);
                all.AddRange(page.Sessions);
                cursor = page.Next;
            }
            while (cursor is not null);

            return all;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error loading workout sessions for export");
            throw;
        }
    }

    private async Task PopulateExercisesAsync(IReadOnlyList<WorkoutSession> sessions)
    {
        if (sessions.Count == 0)
            return;

        var ids = sessions.Select(s => s.Id).ToList();
        var exercises = await _database.Table<Exercise>()
            .Where(e => ids.Contains(e.WorkoutSessionId))
            .OrderBy(e => e.Id)
            .ToListAsync();

        var bySession = exercises
            .GroupBy(e => e.WorkoutSessionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var session in sessions)
        {
            session.Exercises = bySession.TryGetValue(session.Id, out var list) ? list : new List<Exercise>();
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
                session.ExerciseCount = session.Exercises.Count;
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

            session.ExerciseCount = session.Exercises.Count;
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

    private sealed class ExerciseCountRow
    {
        public int SessionId { get; set; }

        public int Total { get; set; }
    }
}
