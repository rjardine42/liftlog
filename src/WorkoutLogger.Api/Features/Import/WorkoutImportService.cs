using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using WorkoutLogger.Api.Features.Exercises;
using WorkoutLogger.Api.Common;
using WorkoutLogger.Api.Persistence;

namespace WorkoutLogger.Api.Features.Import;

/// <summary>
/// Turns a validated batch of workouts into rows.
/// </summary>
/// <remarks>
/// Two behaviours matter here:
/// <list type="bullet">
/// <item>Workouts are find-or-create by (PerformedOn, Label), so posting Monday's
/// bench now and Monday's squats later lands both in the same workout.</item>
/// <item>The dedupe unit is exercise + date, enforced by UNIQUE(WorkoutId, ExerciseId).
/// A repeat is skipped and reported rather than raised as an error, which makes
/// re-posting the same payload safe.</item>
/// </list>
/// The whole request runs in one transaction: validation has already passed at
/// this point, so any failure here is unexpected and should leave nothing behind.
/// </remarks>
public sealed class WorkoutImportService
{
    private const string DefaultSource = "manual";

    private readonly IDbConnectionFactory _connectionFactory;

    public WorkoutImportService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ImportResult> ImportAsync(ImportBatchRequest request, CancellationToken ct)
    {
        var workouts = request.Workouts ?? [];
        var source = string.IsNullOrWhiteSpace(request.Source) ? DefaultSource : request.Source.Trim();
        var now = Clock.UtcNowIso();

        using var connection = _connectionFactory.Create();
        using var transaction = connection.BeginTransaction();

        var resolver = new ExerciseResolver(connection, transaction);
        var batchId = await InsertBatchAsync(connection, transaction, source, HashPayload(request), workouts.Count, now, ct);

        var created = 0;
        var matched = 0;
        var imported = 0;
        var skipped = new List<SkippedExercise>();
        var unresolved = new List<UnresolvedExercise>();

        foreach (var workout in workouts)
        {
            var performedOn = workout.PerformedOn!.Trim();
            var label = string.IsNullOrWhiteSpace(workout.Label) ? null : workout.Label.Trim();

            var (workoutId, wasCreated) = await FindOrCreateWorkoutAsync(
                connection, transaction, workout, performedOn, label, source, now, ct);

            if (wasCreated)
            {
                created++;
            }
            else
            {
                matched++;
            }

            // Appending to an existing workout must continue its ordering, not restart at 1.
            var position = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COALESCE(MAX(Position), 0) FROM WorkoutExercise WHERE WorkoutId = @workoutId;",
                new { workoutId }, transaction, cancellationToken: ct));

            foreach (var exercise in workout.Exercises!)
            {
                var resolved = await resolver.ResolveAsync(exercise.Name!, ct);

                if (resolved.WasCreatedUnresolved)
                {
                    unresolved.Add(new UnresolvedExercise(resolved.ExerciseId, resolved.CanonicalName));
                }

                var workoutExerciseId = await TryInsertWorkoutExerciseAsync(
                    connection, transaction, workoutId, resolved.ExerciseId, ++position,
                    exercise, batchId, now, ct);

                if (workoutExerciseId is null)
                {
                    position--; // nothing was inserted, so the slot is still free
                    skipped.Add(new SkippedExercise(
                        performedOn,
                        resolved.CanonicalName,
                        "Already logged for this date."));
                    continue;
                }

                await InsertSetsAsync(connection, transaction, workoutExerciseId.Value, exercise, now, ct);
                imported++;
            }
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE ImportBatch
            SET ExercisesImported = @imported, ExercisesSkipped = @skippedCount, Status = 'Accepted'
            WHERE Id = @batchId;
            """,
            new { imported, skippedCount = skipped.Count, batchId }, transaction, cancellationToken: ct));

        transaction.Commit();

        return new ImportResult
        {
            BatchId = batchId,
            WorkoutsReceived = workouts.Count,
            WorkoutsCreated = created,
            WorkoutsMatched = matched,
            ExercisesImported = imported,
            ExercisesSkipped = skipped.Count,
            Skipped = skipped,
            UnresolvedExercises = unresolved
        };
    }

    private static async Task<long> InsertBatchAsync(
        SqliteConnection connection, SqliteTransaction transaction,
        string source, string payloadHash, int workoutsReceived, string now, CancellationToken ct)
    {
        // Inserted up front with zero counts because WorkoutExercise carries an
        // FK to it; the totals are written back once the batch has been walked.
        const string sql =
            """
            INSERT INTO ImportBatch
                (ReceivedAtUtc, SourceName, PayloadHash, WorkoutsReceived, ExercisesImported, ExercisesSkipped, Status)
            VALUES (@now, @source, @payloadHash, @workoutsReceived, 0, 0, 'Pending')
            RETURNING Id;
            """;

        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            sql, new { now, source, payloadHash, workoutsReceived }, transaction, cancellationToken: ct));
    }

    private static async Task<(long WorkoutId, bool WasCreated)> FindOrCreateWorkoutAsync(
        SqliteConnection connection, SqliteTransaction transaction, WorkoutDto workout,
        string performedOn, string? label, string source, string now, CancellationToken ct)
    {
        var existing = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT Id FROM Workout WHERE PerformedOn = @performedOn AND COALESCE(Label, '') = COALESCE(@label, '');",
            new { performedOn, label }, transaction, cancellationToken: ct));

        if (existing is not null)
        {
            return (existing.Value, false);
        }

        const string insert =
            """
            INSERT INTO Workout (ExternalId, PerformedOn, Label, Notes, SourceName, CreatedAtUtc)
            VALUES (@externalId, @performedOn, @label, @notes, @source, @now)
            RETURNING Id;
            """;

        var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            insert,
            new { externalId = workout.ExternalId, performedOn, label, notes = workout.Notes, source, now },
            transaction, cancellationToken: ct));

        return (id, true);
    }

    private static async Task<long?> TryInsertWorkoutExerciseAsync(
        SqliteConnection connection, SqliteTransaction transaction,
        long workoutId, long exerciseId, int position, WorkoutExerciseDto exercise,
        long batchId, string now, CancellationToken ct)
    {
        // ON CONFLICT DO NOTHING turns the UNIQUE(WorkoutId, ExerciseId) dedupe
        // into a null result rather than an exception, so a repeat import is a
        // reportable skip instead of a failure.
        const string sql =
            """
            INSERT INTO WorkoutExercise
                (WorkoutId, ExerciseId, Position, RawExerciseName, Notes, ImportBatchId, CreatedAtUtc)
            VALUES (@workoutId, @exerciseId, @position, @rawName, @notes, @batchId, @now)
            ON CONFLICT (WorkoutId, ExerciseId) DO NOTHING
            RETURNING Id;
            """;

        return await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            sql,
            new { workoutId, exerciseId, position, rawName = exercise.Name!.Trim(), notes = exercise.Notes, batchId, now },
            transaction, cancellationToken: ct));
    }

    private static async Task InsertSetsAsync(
        SqliteConnection connection, SqliteTransaction transaction,
        long workoutExerciseId, WorkoutExerciseDto exercise, string now, CancellationToken ct)
    {
        const string sql =
            """
            INSERT INTO WorkoutSet
                (WorkoutExerciseId, SetNumber, Reps, Weight, WeightUnit, Rpe, IsWarmup, CreatedAtUtc)
            VALUES (@workoutExerciseId, @setNumber, @reps, @weight, @weightUnit, @rpe, @isWarmup, @now);
            """;

        var setNumber = 0;
        var rows = exercise.Sets!.Select(set => new
        {
            workoutExerciseId,
            setNumber = ++setNumber,
            reps = set.Reps!.Value,
            weight = set.Weight,
            weightUnit = string.IsNullOrWhiteSpace(set.Unit)
                ? WeightUnits.Pounds
                : set.Unit.Trim().ToLowerInvariant(),
            // The per-exercise RPE is a hand-entry shorthand; an explicit per-set value wins.
            rpe = set.Rpe ?? exercise.Rpe,
            isWarmup = set.IsWarmup ? 1 : 0,
            now
        }).ToList();

        await connection.ExecuteAsync(new CommandDefinition(sql, rows, transaction, cancellationToken: ct));
    }

    private static string HashPayload(ImportBatchRequest request)
    {
        var json = JsonSerializer.Serialize(request);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}
