# Workout Logger — Backend

A small .NET 10 backend for logging workouts to SQLite and reading the cleaned
data back out. No frontend, single user, one shared API key.

The priority is **clean ingestion**. Analytics is deliberately a skeleton with
one working metric and a seam to add more.

## Running it

```bash
dotnet run --project src/WorkoutLogger.Api
```

Listens on `http://localhost:5204` and creates `workouts.db` next to the project
on first start. Point it elsewhere with `ConnectionStrings__WorkoutDb`.

Every route except `/health` requires an `X-Api-Key` header matching the
`API_KEY` setting. Outside Development the app refuses to start without one;
in Development an unset key turns the check off. `API_KEY` is read from
configuration like any other setting (environment variable, app setting on
Azure) — a `.env` file is not loaded.

```bash
dotnet test
```

There is also a [Bruno](https://usebruno.com) collection in `bruno/` — 19
requests with assertions, covering the same ground end to end. See
[bruno/README.md](bruno/README.md).

```bash
cd bruno && npx @usebruno/cli run --env Local -r
```

## Finding your way around

If you are used to layered enterprise .NET — controllers, a service layer,
repository interfaces, EF Core — [ARCHITECTURE.md](ARCHITECTURE.md) maps those
habits onto this codebase: minimal-API parameter binding, why everything is a
Singleton, where transactions live, and the Dapper traps.

There is no routing table, so to find where a route is handled, grep for it:

```bash
grep -rn "MapPost\|MapGet\|MapDelete\|MapPut" src/WorkoutLogger.Api/Features
```

## Importing

A **workout** is the unit of import: one date, several exercises, each with its
own sets. `POST /api/workouts` takes one; `POST /api/workouts/batch` takes
`{ "source": "...", "workouts": [ ... ] }`.

```bash
curl -s -X POST http://localhost:5204/api/workouts \
  -H 'Content-Type: application/json' -d @samples/workout.json
```

```json
{
  "performedOn": "2026-09-18",
  "notes": "Push day",
  "exercises": [
    {
      "name": "Barbell Bench Press",
      "rpe": 8,
      "sets": [
        { "reps": 10, "weight": 95, "isWarmup": true },
        { "reps": 8, "weight": 185, "rpe": 7.5 },
        { "reps": 6, "weight": 195 }
      ]
    }
  ]
}
```

| Field | Notes |
|---|---|
| `performedOn` | Required, `yyyy-MM-dd`, not in the future. |
| `label` | Optional session tag (`"am"`/`"pm"`). Omit it and the date is the whole key. |
| `externalId`, `notes` | Optional, stored as-is. |
| `exercises[].name` | Free text. Resolved against the catalogue — see below. |
| `exercises[].rpe` | Shorthand applied to any set in the block that omits its own. |
| `sets[].reps` | Required, > 0. |
| `sets[].weight` | Optional (omit for bodyweight), >= 0. |
| `sets[].unit` | Optional, **must be `lb`** — see *Units*. |
| `sets[].rpe` | Optional, 1–10 in steps of 0.5. Wins over the exercise-level value. |

### Four behaviours worth knowing

**Exercise names are normalized.** `"bench"`, `"BB Bench"` and
`"Barbell  Bench-Press"` all resolve to the same catalogue entry, so a lift does
not fragment into several progression lines. A name that matches nothing is
still stored, flagged unresolved, and returned in the response — see *Unresolved
exercises*.

**Workouts are find-or-create by date.** Posting bench for Monday now and squats
for Monday later attaches both to the same workout rather than creating two.

**Dedupe is exercise + date.** Re-posting an exercise already logged for that
date is skipped and reported, not an error, so re-running an import is safe:

```json
{ "workoutsMatched": 1, "exercisesImported": 0, "exercisesSkipped": 3,
  "skipped": [ { "performedOn": "2026-09-18", "exercise": "Barbell Bench Press",
                 "reason": "Already logged for this date." } ] }
```

**Validation is all-or-nothing.** The whole payload is checked first; if
anything fails you get a 400 listing *every* problem and nothing is written. Fix
the payload and re-post it.

```json
{ "errors": {
    "exercises[0].sets[0].reps": ["reps must be greater than zero."],
    "exercises[0].sets[0].rpe":  ["rpe must be between 1 and 10 in steps of 0.5."] } }
```

## Unresolved exercises

An unrecognised name is stored rather than rejected, so an import never loses
data over a spelling. Clear the backlog with either:

```bash
curl -s http://localhost:5204/api/exercises/unresolved

# Teach an existing exercise another spelling (affects future imports):
curl -X POST http://localhost:5204/api/exercises/12/aliases \
  -H 'Content-Type: application/json' -d '{"alias":"Pendlay Row"}'

# Or fold a mistaken entry into the right one (also repoints what is already logged):
curl -X POST http://localhost:5204/api/exercises/35/merge-into/12
```

Merging moves existing sets across, adds the old name as an alias, and deletes
the source. If a workout held both exercises, the sets are combined into one
block and renumbered.

## Reading the data

| Endpoint | Returns |
|---|---|
| `GET /api/workouts?from=&to=&page=&pageSize=` | Summaries, newest first. |
| `GET /api/workouts/{id}` | One workout, exercises and sets nested. |
| `GET /api/sets?exerciseId=&from=&to=&includeWarmups=` | **Flat, chart-ready rows.** Warmups excluded unless asked for. |
| `GET /api/exercises` · `/unresolved` | The catalogue. |
| `GET /api/analytics/metrics` | Metrics this server can compute. |
| `GET /api/analytics/{key}?exerciseId=&from=&to=&bucket=day\|week\|month` | One metric as a time series. |
| `GET /health` | Liveness. |

`/api/sets` is the one to point a frontend or a notebook at.

## Adding an analytics metric

Implement `IWorkoutMetric` and register it. No new endpoint, no change to any
caller — every metric shares the route and the `MetricSeries` response shape.

```csharp
public sealed class VolumeMetric : IWorkoutMetric
{
    public string Key => "volume";
    public string DisplayName => "Total Volume";
    public string Unit => "lb";

    public Task<MetricSeries> ComputeAsync(MetricQuery q, SqliteConnection c, CancellationToken ct) { ... }
}
```

```csharp
builder.Services.AddWorkoutMetric<VolumeMetric>();   // Program.cs
```

`EstimatedOneRepMaxMetric` ("e1rm", Epley) is the worked example.

## Design notes

**Units.** Weights are assumed to be pounds. `unit` is accepted and stored per
set, but any value other than `lb` is *rejected* rather than stored as-is —
silently mis-recording a unit is the one data error that is expensive to find
later. Adding conversion means changing validation and reading the column that
is already there.

**No migrations.** `DatabaseInitializer` runs `Schema.sql`
(`CREATE TABLE IF NOT EXISTS`) and `Seed.sql` (`INSERT OR IGNORE`) at startup;
both are idempotent, so every boot after the first is a no-op. There is no
versioning, so **changing the schema means deleting the `.db` file and
re-importing.** Deliberate for a personal app — swapping in a migration tool
later replaces only that one class.

**Dapper, not EF Core.** Hand-written SQL throughout. Note that types Dapper
materializes use init-only properties rather than positional records: SQLite
reports every integer as `INTEGER` and every float as `REAL`, which defeats
constructor binding for `int`/`bool`/`decimal`.

**Foreign keys.** SQLite disables FK enforcement by default, per connection.
`SqliteConnectionFactory` sets `Foreign Keys=True`; without it every FK and
cascade in the schema is silently inert.

## Layout

```
src/WorkoutLogger.Api/
  Common/         TextNormalizer (the exercise-name lookup key), Clock, validation helpers
  Persistence/    Connection factory, DatabaseInitializer, Schema.sql, Seed.sql
  Features/
    Import/       DTOs, validators, WorkoutImportService, both import endpoints
    Exercises/    ExerciseResolver (alias -> canonical -> create-unresolved), repository
    Workouts/     Read endpoints and queries
    Analytics/    IWorkoutMetric seam, registry, EstimatedOneRepMaxMetric
tests/WorkoutLogger.Api.Tests/   Integration tests against a real temp SQLite file
samples/         Example payloads
```
