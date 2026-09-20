# Bruno collection

Nineteen requests that double as an end-to-end smoke test: every one carries
assertions, so the collection both demonstrates the API and verifies it.

## Running it

**In the app** — Bruno → *Open Collection* → pick this `bruno/` folder, then
select the **Local** environment (top right). Start the API first:

```bash
dotnet run --project src/WorkoutLogger.Api
```

Run the whole collection with the ▶ on the collection root, or click through
folder by folder — they are ordered to tell a story.

**From the terminal** — no install needed:

```bash
cd bruno && npx @usebruno/cli run --env Local -r
```

## It writes real data

These requests import workouts into whatever database the API is pointed at.
To keep your real log clean, start the server against a throwaway file:

```bash
ConnectionStrings__WorkoutDb="Data Source=bruno-test.db" dotnet run --project src/WorkoutLogger.Api
```

Delete `bruno-test.db` to reset.

**Reruns are safe either way.** *Import workout* picks a random `sessionDate`
and `runId` at the start of every run, and everything downstream builds on
those, so a run never collides with one before it. The only residue is an
alias and a merged exercise per run, which are harmless.

## What the folders show

| Folder | The point |
|---|---|
| **Health** | Is it up. |
| **Import** | Name resolution, idempotent re-import, find-or-create by date, batch, and all-or-nothing validation. |
| **Workouts** | Reading it back — summaries, one nested workout, and the flat set rows. |
| **Exercises** | The route out of the unresolved backlog: alias vs. merge. |
| **Analytics** | The metric seam and estimated 1RM. |

Requests chain through runtime variables — `sessionDate` and `runId` from
*Import workout*, then `workoutId`, `benchPressId`, `barbellRowId` and
`unresolvedExerciseId` captured from responses along the way. Run a folder in
order, or run the collection whole. Firing a single request cold still works,
but assertions that depend on a captured id will not.

Each request's **Docs** tab explains what to look for in the response.
