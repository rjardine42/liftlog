-- Workout Logger schema. Idempotent: safe to run on every startup.
-- No migration framework by design (personal app). Changing this file means
-- deleting the .db and re-importing.

CREATE TABLE IF NOT EXISTS ImportBatch (
    Id                INTEGER PRIMARY KEY AUTOINCREMENT,
    ReceivedAtUtc     TEXT    NOT NULL,
    SourceName        TEXT    NOT NULL,
    PayloadHash       TEXT    NOT NULL,
    WorkoutsReceived  INTEGER NOT NULL,
    ExercisesImported INTEGER NOT NULL,
    ExercisesSkipped  INTEGER NOT NULL,
    Status            TEXT    NOT NULL
);

CREATE TABLE IF NOT EXISTS Exercise (
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    CanonicalName  TEXT    NOT NULL UNIQUE,
    NormalizedName TEXT    NOT NULL UNIQUE,
    MuscleGroup    TEXT    NULL,
    Modality       TEXT    NULL,
    IsUnresolved   INTEGER NOT NULL DEFAULT 0,
    CreatedAtUtc   TEXT    NOT NULL
);

CREATE TABLE IF NOT EXISTS ExerciseAlias (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    ExerciseId      INTEGER NOT NULL REFERENCES Exercise(Id) ON DELETE CASCADE,
    NormalizedAlias TEXT    NOT NULL UNIQUE,
    RawAlias        TEXT    NOT NULL,
    CreatedAtUtc    TEXT    NOT NULL
);

CREATE TABLE IF NOT EXISTS Workout (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    ExternalId   TEXT NULL,
    PerformedOn  TEXT NOT NULL,          -- yyyy-MM-dd
    Label        TEXT NULL,              -- e.g. "am" / "pm"; NULL = the day's only session
    Notes        TEXT NULL,
    SourceName   TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL
);

-- SQLite treats NULLs as distinct in a UNIQUE constraint, so a plain
-- UNIQUE(PerformedOn, Label) would let unlabelled duplicates of the same day
-- through. COALESCE in an expression index collapses them correctly.
CREATE UNIQUE INDEX IF NOT EXISTS UX_Workout_Day
    ON Workout (PerformedOn, COALESCE(Label, ''));

CREATE TABLE IF NOT EXISTS WorkoutExercise (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    WorkoutId       INTEGER NOT NULL REFERENCES Workout(Id) ON DELETE CASCADE,
    ExerciseId      INTEGER NOT NULL REFERENCES Exercise(Id),
    Position        INTEGER NOT NULL,
    RawExerciseName TEXT    NOT NULL,    -- provenance: what the payload actually said
    Notes           TEXT    NULL,
    ImportBatchId   INTEGER NOT NULL REFERENCES ImportBatch(Id),
    CreatedAtUtc    TEXT    NOT NULL,
    UNIQUE (WorkoutId, ExerciseId)       -- the dedupe unit: exercise + date
);

CREATE TABLE IF NOT EXISTS WorkoutSet (
    Id                INTEGER PRIMARY KEY AUTOINCREMENT,
    WorkoutExerciseId INTEGER NOT NULL REFERENCES WorkoutExercise(Id) ON DELETE CASCADE,
    SetNumber         INTEGER NOT NULL,
    Reps              INTEGER NOT NULL,
    Weight            REAL    NULL,
    WeightUnit        TEXT    NOT NULL DEFAULT 'lb',
    Rpe               REAL    NULL,
    IsWarmup          INTEGER NOT NULL DEFAULT 0,
    CreatedAtUtc      TEXT    NOT NULL,
    UNIQUE (WorkoutExerciseId, SetNumber)
);

CREATE INDEX IF NOT EXISTS IX_WorkoutExercise_ExerciseId ON WorkoutExercise (ExerciseId);
CREATE INDEX IF NOT EXISTS IX_Workout_PerformedOn        ON Workout (PerformedOn);
CREATE INDEX IF NOT EXISTS IX_ExerciseAlias_Normalized   ON ExerciseAlias (NormalizedAlias);
