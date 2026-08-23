# Session Handoff — 2026-08-23

## What was built

A DevExpress XAF 26.1.4 Blazor Server POC (.NET 8, EF Core, LocalDB) proving three
things work together in one app: an editable seeded report, admin-attachable filter
criteria via XAF's native criteria editor, and Hangfire cron scheduling with an
on-demand "Run Now". See `README.md` for the full description and `CLAUDE.md` for
repo conventions.

Built across 6 tasks, one commit per task (local only, nothing pushed):

| Commit | Summary |
|---|---|
| `8742375` | Scaffold: `dotnet new dx.xaf` (EF Core, SqlServer, Blazor, Password auth, Reports/Validation/Appearance modules), LocalDB + `ReportJobs` config, Hangfire.AspNetCore/InMemory added to Blazor.Server only |
| `3566a4a` | Sample `Customer`/`Order` domain + code-built `OrdersReport`, seeded as a non-predefined `ReportDataV2` row |
| `ffef9e5` | `ReportSchedule` entity with `[CriteriaOptions]` popup criteria editor, seeded "Weekly Acme Orders" + "E2E Acme Orders (CSV)" schedules |
| `a267596` | `ReportJob` (export via `IReportExportService`), Hangfire recurring-job sync at startup, "Run Now" controller action |
| `d155742` | Fix round: unregister Hangfire job on schedule delete, log sync-retry exhaustion, guard unhandled `ExportFormat` |
| `2f083ec` | C# Playwright E2E phase gate (`XafReportScheduler.E2ETests`) |
| `ec07a4a` | Final-review fix wave (code): cron validation on save, per-row sync isolation, logon inside try, no Hangfire retries, E2E fresh-clone robustness, `Nullable` on Module |
| `5909563` | Final-review fix wave (docs): E2E prerequisites, credential handling, scheduling ceilings, handoff pointers |
| `aa288de` | Re-review follow-up: don't validate cron on schedules being deleted (a bad-cron row was undeletable) |

## What was verified

Full E2E run (`dotnet run --project XafReportScheduler.E2ETests`), two consecutive
green runs, exit code 0:

```
--- SQL: Orders Report has no PredefinedReportTypeName (it is a plain, editable report)
    [ok] PredefinedReportTypeName is null/empty (was: )
--- Open Orders Report and launch the end-user Report Designer
    [ok] report designer surface (.dxrd-designer / dx-report-designer) rendered
--- Open the E2E Acme Orders (CSV) schedule and click Run Now
--- Poll output/ for the exported CSV (up to 30s)
    [ok] CSV file matching 'E2E Acme Orders (CSV)_*.csv' appeared
    exported CSV:
Orders,,,
    ORD-001,2026-08-21,Acme Corp,"1,250.00"
    [ok] ORD-001 (Acme Corp, within last week) present
    [ok] ORD-002 (Acme Corp, 30 days old) excluded by date criterion
    [ok] ORD-003 (Globex) excluded by Customer.Name criterion
--- Reload the schedule detail view and assert LastRunStatus = Succeeded
    [ok] 'Succeeded' found among detail view input values
--- App stdout must report the enabled recurring job was registered
    [ok] app stdout contains 'Registered 1 report schedules'
=== E2E PASSED ===
EXIT_CODE=0
```

Also manually verified live: deleting a `ReportSchedule` unregisters its Hangfire
recurring job (confirmed via app log + SQL soft-delete row check); the criteria editor
popup renders `Order Date` / `Customer.Name` as resolved tokens against the seeded
`Order` type.

Full detail of each task's verification is in
`.superpowers/sdd/2026-08-23-xaf-report-scheduler/task-{1..5}-report.md` on the authoring
machine (git-ignored). A final-review fix wave's report is at
`.superpowers/sdd/2026-08-23-xaf-report-scheduler/final-fix-report.md`, same basis.

## Open points

1. **Push / GitHub repo creation is pending owner go.** Nothing has been pushed; the
   repo is local-only (`git init`, no remote configured). Per the plan's Global
   Constraints, this stays that way until the owner explicitly says go.
2. **Before any public push: regenerate `UrlSigningKey` in `appsettings.json`.** It's
   still the scaffold's debug key (`XafReportScheduler.Blazor.Server/appsettings.json`),
   fine for a local POC but not something to publish as-is.
3. **DevExpress ticket outcome pending** — a support ticket about the "XafReportScheduler"
   name and publishing terms is open with DevExpress; the owner has a draft in their
   own scratchpad. Repo visibility/naming may need to change depending on the answer.
4. **Possible next steps** (none started, no work done toward these):
   - SMTP delivery sink alongside the folder sink.
   - A run-history entity (currently only the last run's status/message/path are kept
     on `ReportSchedule` itself).
   - Persistent Hangfire storage (SQL Server or PostgreSQL) instead of in-memory, so
     scheduled runs survive a restart / missed runs can be replayed.
   - PostgreSQL as an alternative to LocalDB.
   - Tree-friendly criteria examples/docs — the current seeded criteria string renders
     fine in Advanced/text mode but the visual criteria-tree editor can't build a tree
     for it (`LocalDateTimeLastWeek()`).
   - Retarget `net10.0` — the plan's Tech Stack line named .NET 10, but the `dotnet new
     dx.xaf` template's current default is `net8.0` and that's what's actually in every
     `.csproj` in this repo (flagged, not changed, in Task 1 — see its report for the
     reasoning). Revisit if .NET 10 is load-bearing for this POC.

## Known nits (deferred, non-blocking)

Fixed in the final-review fix wave (commit `ec07a4a`): `Module.csproj` now has
`<Nullable>enable</Nullable>` (builds with 0 warnings; `Blazor.Server.csproj` deliberately
left as-is); `ReportJob.Logon()`'s mixed soft-check/hard-cast style is gone; and the
previous "unused `using DevExpress.ExpressApp.DC;`" note (below, in earlier revisions of
this file) was wrong — it's what `FieldSizeAttribute` resolves through, so it's back and
genuinely used.

Still open:

- `ReportScheduleController`'s deleted-ids `HashSet<Guid>` isn't cleared if a commit is
  cancelled/rolled back — self-heals on the next successful save, not a leak.
- `ReportScheduleController`'s `ObjectSpace.Committed` handler only re-registers
  `ViewCurrentObject`; only matters if inline/nested editing of multiple schedules from
  one view is added later (out of scope for this POC's single-DetailView flow).
- The E2E's `PollForCsv` matches `{ScheduleName}_*.csv` by glob, not newest-by-mtime —
  fine with today's single CSV-exporting schedule, would need tightening if more CSV
  schedules are added.
- `ReportScheduleSyncService` gives up ~30 s after host start if it can't register
  recurring jobs by then (6 attempts, 5 s apart) — see README "Startup sync window".
- The E2E suite runs against the same dev LocalDB catalog as `dotnet run --project
  XafReportScheduler.Blazor.Server` (it deletes/re-seeds `Orders`/`Customers`) — don't run
  it against a database whose contents you care about; see README "E2E".

## Environment note

DEBUG builds auto-update the DB schema on startup (`BlazorApplication.cs`'s
`DatabaseVersionMismatch`/`OnSetupStarted` gates were changed from "`#if DEBUG` **and**
`Debugger.IsAttached`" to plain `#if DEBUG` in Task 4, folded in because Task 2 hit it
blocking a debugger-less live verification run). A genuine Release build with no
debugger still throws on schema mismatch, unchanged.
