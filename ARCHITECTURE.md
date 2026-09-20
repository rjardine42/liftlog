# Navigating this repo

Orientation for anyone whose usual .NET is a layered enterprise stack —
controllers, a service layer, repository interfaces, EF Core, separate
`Core`/`Infrastructure` projects. This codebase is minimal APIs, feature
folders and Dapper, and most of the adjustment is knowing where your habits
map to.

About 2,500 lines total, one API project and one test project.

## 1. The translation table

| Your usual stack                              | Here                                                             | Where                                   |
| --------------------------------------------- | ---------------------------------------------------------------- | --------------------------------------- |
| `Startup.cs` + `Program.cs`                   | One file, top-level statements, 47 lines                         | `Program.cs`                            |
| `Controllers/WorkoutsController.cs`           | `static class WorkoutEndpoints` with a `MapXxx` extension method | `Features/Workouts/WorkoutEndpoints.cs` |
| `[HttpGet("{id}")]` attribute routing         | `app.MapGet("/api/workouts/{id:long}", GetAsync)`                | `WorkoutEndpoints.cs`                   |
| `[Route("api/workouts")]` on the class        | `app.MapGroup("/api/workouts")`                                  | `ImportEndpoints.cs:10`                 |
| `IActionResult` / `ActionResult<T>`           | `IResult`                                                        | everywhere                              |
| `Ok()`, `NotFound()`, `BadRequest()`          | `Results.Ok()`, `Results.NotFound()`, …                          |                                         |
| `ModelState.IsValid` + `[ApiController]`      | Nothing automatic — the handler calls the validator              | `ImportEndpoints.cs:51`                 |
| `Core` / `Domain` / `Infrastructure` projects | One project, `Features/` folders                                 |                                         |
| `IWorkoutRepository` + impl + mock            | Concrete `WorkoutRepository`, no interface                       |                                         |
| `DbContext`, `DbSet<T>`, LINQ                 | `IDbConnectionFactory` + SQL strings                             | `Persistence/`                          |
| EF migrations                                 | `Schema.sql` run at startup                                      | `Persistence/DatabaseInitializer.cs`    |
| `ServiceCollectionExtensions` per module      | 12 lines in `Program.cs`                                         | `Program.cs:13-26`                      |
| Mocked unit tests                             | `WebApplicationFactory` against real SQLite                      | `tests/ApiFactory.cs`                   |

## 2. Start by reading `Program.cs` top to bottom

47 lines, and it is genuinely the whole composition root. There is no hidden
`AddInfrastructure()` extension chain to chase — what you see registered is
what exists.

Three sections: configuration, DI registrations (13-26), route registrations
(37-42). `app.MapImportEndpoints()` and friends are extension methods on
`IEndpointRouteBuilder` defined in each feature folder. That is the only
indirection, and it exists so `Program.cs` does not become 200 lines of
`MapGet`.

**Line 30 is unusual and worth noting:** `DatabaseInitializer.Initialize()`
runs synchronously at startup, before `app.Run()`. That is where the schema
comes from.

## 3. Parameter binding — the biggest adjustment

This is where MVC habits get burned. There are **no `[FromBody]`,
`[FromQuery]`, `[FromRoute]` or `[FromServices]` attributes.** Binding is by
convention:

| Parameter                                                                   | Binds from                  |
| --------------------------------------------------------------------------- | --------------------------- |
| Name matches a `{token}` in the route template                              | Route                       |
| Type is registered in DI                                                    | Services                    |
| Simple type (`string`, `int`, `DateOnly`, `bool`, `Guid`…) not in the route | **Query string**            |
| Complex type not in DI                                                      | **JSON body** (at most one) |
| `CancellationToken`, `HttpContext`, `ClaimsPrincipal`, `Stream`             | Special-cased               |

Every one of those appears in one real signature, `ImportEndpoints.cs:21`:

```csharp
private static async Task<IResult> ImportSingleAsync(
    WorkoutDto workout,                        // complex, not in DI → JSON body
    string? source,                            // simple, not in route → ?source=
    ImportBatchRequestValidator validator,     // registered in DI → service
    WorkoutImportService importService,        // registered in DI → service
    CancellationToken ct)                      // special-cased
```

And a route-bound one, `ExerciseEndpoints.cs:28`, mapped at
`MapPost("/{id:long}/aliases", ...)`:

```csharp
private static async Task<IResult> AddAliasAsync(
    long id,                    // matches {id:long} → route
    AddAliasRequest request,    // complex, not in DI → body
    ExerciseRepository repository, CancellationToken ct)
```

**Two failure modes to watch for.** Register a type in DI that you meant to
come from the body and it silently binds from DI instead — no error, just an
empty object. Forget to register a service and the framework tries to
deserialize it from the body and throws. Both are confusing the first time,
because there is no attribute to point at.

`{id:long}` is a **route constraint**, replacing what attribute routing plus
model binding would express. A non-numeric `id` 404s before the handler runs.

## 4. One surprise in the layout

`/api/workouts` is mapped in **two different files**:

- `POST /api/workouts` and `POST /api/workouts/batch` → `Features/Import/`
- `GET /api/workouts` and `GET /api/workouts/{id}` → `Features/Workouts/`

Deliberate — writes and reads are different concerns — but it breaks the
controller habit of "one URL prefix, one file". **To find where a route lives,
grep the verb and path rather than guessing the folder:**

```bash
grep -rn "MapPost\|MapGet\|MapDelete\|MapPut" src/WorkoutLogger.Api/Features
```

That prints every route in the app in about ten lines. It is the closest thing
this repo has to a routing table.

## 5. Everything is a Singleton, and that is not a mistake

In a stack built on EF Core, `DbContext` is Scoped, so repositories are Scoped,
so services are Scoped, and Singleton is a footgun. Here there is **no
`DbContext`**. `SqliteConnectionFactory` hands out a fresh connection per
operation and the caller disposes it:

```csharp
using var connection = _connectionFactory.Create();
```

So `WorkoutRepository`, `ExerciseRepository` and `WorkoutImportService` hold no
mutable state and are safely Singleton.

**If you add a class that holds per-request state, register it Scoped** — the
existing pattern will not warn you.

One class deliberately escapes DI: `ExerciseResolver`. It holds a per-request
lookup cache and needs the _caller's_ open transaction, so it is constructed
inside `WorkoutImportService` rather than injected. If you see a class
instantiated directly, check whether it holds state or needs a transaction —
that is usually the reason.

## 6. No repository interfaces

`WorkoutRepository` is a concrete class with no `IWorkoutRepository`. This
feels wrong coming from a stack where every service has an interface for
mocking.

Nothing mocks them. Tests boot the real app against a real temporary SQLite
file, because the value here is in the SQL itself — a mocked repository would
test nothing. The one interface that does exist, `IDbConnectionFactory`, exists
so tests can point at a different file, not so it can be faked.

**Do not add an interface until something needs a second implementation.**
`IWorkoutMetric` is the counter-example: multiple implementations by design.

## 7. Dapper vs EF Core

| EF Core habit                          | Here                                                                     |
| -------------------------------------- | ------------------------------------------------------------------------ |
| `_db.Workouts.Include(w => w.Sets)`    | Write the JOIN, group in memory — see `WorkoutRepository.GetAsync`       |
| Change tracking, `SaveChanges()`       | Every write is an explicit `INSERT`/`UPDATE` that runs immediately       |
| Navigation properties                  | None. Rows are flat; you shape them                                      |
| `IQueryable` composition               | SQL strings with `(@param IS NULL OR col = @param)` for optional filters |
| Implicit transaction per `SaveChanges` | Explicit `BeginTransaction()` / `Commit()`                               |
| `Add-Migration`                        | Edit `Schema.sql`, delete the `.db`, re-import                           |

The optional-filter idiom is everywhere and worth internalizing:

```sql
WHERE (@exerciseId IS NULL OR we.ExerciseId = @exerciseId)
  AND (@from IS NULL OR w.PerformedOn >= @from)
```

That is how a nullable C# parameter becomes an optional SQL filter without
string concatenation.

**Transactions live in the service, not the repository.**
`WorkoutImportService.ImportAsync` opens one connection and one transaction and
threads both through every helper. That is why those helpers take
`SqliteConnection` and `SqliteTransaction` as parameters instead of creating
their own. If you add a step, follow that convention or it will silently run
outside the transaction.

**The Dapper trap:** types Dapper materializes use `{ get; init; }` properties,
not positional records. SQLite reports every integer as `INTEGER` and every
float as `REAL`, which defeats constructor binding for `int`, `bool` and
`decimal` — you get a runtime 500, not a compile error. Copy the existing
row-type style.

## 8. Validation is explicit, not ambient

With `[ApiController]`, model validation runs before the action and you check
`ModelState`. Minimal APIs have **no equivalent** — an invalid body reaches the
handler untouched.

So the handler calls the validator itself (`ImportEndpoints.cs:51`), and the
validators are plain FluentValidation classes registered in DI. To make this
automatic later, the hook is an **endpoint filter**
(`.AddEndpointFilter<T>()`), the minimal-API equivalent of an action filter.

## 9. Testing

No mocks. `ApiFactory` is a `WebApplicationFactory<Program>` overriding one
config value:

```csharp
builder.UseSetting("ConnectionStrings:WorkoutDb", $"Data Source={_databasePath}");
```

Used as `IClassFixture<ApiFactory>`, so **each test class gets its own
database**. Tests hit the real HTTP surface with `_client.PostAsJsonAsync(...)`
and assert against the real schema.

Two consequences:

- Tests within a class **share a database**, and xunit does not guarantee
  order. Use a distinct date or exercise name per test — every test in
  `ImportEndpointTests` does. Two tests once collided on a shared exercise name
  and the failure looked like a logic bug.
- `public partial class Program;` at the bottom of `Program.cs` exists only so
  the test project can name the entry point. Do not delete it.

## 10. Tracing a request end to end

`POST /api/workouts`:

1. `Program.cs:39` → `app.MapImportEndpoints()`
2. `ImportEndpoints.cs:12` → `group.MapPost("/", ImportSingleAsync)`
3. `ImportSingleAsync` wraps the single workout as a batch of one, delegates to `RunAsync`
4. `RunAsync` validates everything — returns 400 with all errors, or calls the service
5. `WorkoutImportService.ImportAsync` — opens the transaction, resolves names via `ExerciseResolver`, find-or-creates the workout, inserts with `ON CONFLICT DO NOTHING`, commits
6. The result serializes to JSON automatically

The only non-obvious hop is 1 → 2, which the grep in §4 solves.

## 11. Recipes

**Add a read endpoint.** Method on `WorkoutRepository` (SQL plus a row type
with init properties) → private handler in `WorkoutEndpoints` → one `MapGet`
line. No DI change; the repository is already registered.

**Add an analytics metric.** Implement `IWorkoutMetric`, then add
`builder.Services.AddWorkoutMetric<YourMetric>();` to `Program.cs`. It appears
at `/api/analytics/{key}` automatically — `MetricRegistry` takes
`IEnumerable<IWorkoutMetric>`, the standard multi-registration pattern.

**Add a validation rule.** The validators in `WorkoutValidator.cs` nest one per
level (batch → workout → exercise → set). Add the rule at the matching level
and the error path builds itself.

**Change the schema.** Edit `Schema.sql`, delete the `.db`, restart. No
migration to generate — and no versioning, so existing data is lost.
