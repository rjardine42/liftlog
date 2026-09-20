using WorkoutLogger.Api.Features.Analytics;
using WorkoutLogger.Api.Features.Analytics.Metrics;
using WorkoutLogger.Api.Features.Exercises;
using WorkoutLogger.Api.Features.Import;
using WorkoutLogger.Api.Features.Workouts;
using WorkoutLogger.Api.Persistence;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("WorkoutDb")
                       ?? "Data Source=workouts.db";

builder.Services.AddSingleton<IDbConnectionFactory>(_ => new SqliteConnectionFactory(connectionString));
builder.Services.AddSingleton<DatabaseInitializer>();

builder.Services.AddSingleton<WorkoutImportService>();
builder.Services.AddSingleton<ImportBatchRequestValidator>();
builder.Services.AddSingleton<WorkoutRepository>();
builder.Services.AddSingleton<ExerciseRepository>();

// Adding a metric is one line here plus one class; the route and response
// shape are already in place.
builder.Services.AddSingleton<MetricRegistry>();
builder.Services.AddWorkoutMetric<EstimatedOneRepMaxMetric>();

builder.Services.AddOpenApi();

var app = builder.Build();

app.Services.GetRequiredService<DatabaseInitializer>().Initialize();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).WithTags("Health");

app.MapImportEndpoints();
app.MapWorkoutEndpoints();
app.MapExerciseEndpoints();
app.MapAnalyticsEndpoints();

app.Run();

/// <summary>Exposed so the test host can reference this entry point.</summary>
public partial class Program;
