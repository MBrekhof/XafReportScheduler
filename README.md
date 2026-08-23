# XafReportScheduler

A DevExpress XAF Blazor Server proof of concept: scheduled report generation with
user-editable seeded reports and admin-configurable filter criteria. Built to answer
one question — can an XAF app seed a report that end users can still redesign, attach
a saved filter to it via the standard criteria editor, and run it unattended on a cron
schedule (or on demand), exporting to a folder?

## What it is

**1. An editable seeded report.** `OrdersReport` is a code-built `XtraReport` over the
sample `Order` entity. A `ModuleUpdater` (`ReportSeeder`) writes it into the database
once, as an ordinary `ReportDataV2` row — not through XAF's `PredefinedReportsUpdater`.
That means it opens in the end-user Report Designer like any report a user created
themselves, and edits survive app restarts (the seeder only ever creates the row if
none exists yet, by `DisplayName`).

**2. Admin-attachable criteria.** `ReportSchedule` is a small entity that pairs a report
with a criteria string, using XAF's built-in `[CriteriaOptions]` + popup criteria editor
— the same UI XAF uses everywhere else for filters. The editor's field list follows
whichever report is selected, via a computed `ObjectType` property.

**3. Cron scheduling + Run Now.** Each `ReportSchedule` carries a Hangfire cron
expression. Enabled schedules become Hangfire recurring jobs at startup; saving a
schedule re-syncs its job, deleting one unregisters it. A "Run Now" action on the
detail view enqueues an immediate run. A job logs on as a configured service user,
loads the report, applies the criteria, exports to Pdf/Xlsx/Csv, and records the
outcome on the schedule row.

## Why the seeded report is editable

`ReportDataV2.IsPredefined` is `true` only for reports registered through
`PredefinedReportsUpdater` in code; a row created at runtime via `IReportStorage.SaveReport`
is `false` and stays editable in the end-user designer
([DevExpress docs](https://docs.devexpress.com/eXpressAppFramework/DevExpress.Persistent.BaseImpl.EF.ReportDataV2.IsPredefined)).
`ReportSeeder.UpdateDatabaseAfterUpdateSchema` calls `SaveReport` directly and never
touches `PredefinedReportsUpdater`, so `PredefinedReportTypeName` stays `NULL` on the
seeded row — verified by SQL and by the E2E gate on every run.

## Criteria examples

The seeded schedules use:

```
[OrderDate] >= LocalDateTimeLastWeek() And [Customer.Name] Like 'Acme%'
```

Both forms — the built-in `LocalDateTimeLastWeek()` function and SQL-style `Like` —
translate correctly through XAF's EF Core criteria pipeline (`IReportExportService.SetupReport`);
no `StartsWith` rewrite was needed. Verified end-to-end: running the report against the
seed data (Acme orders at -2 and -30 days, a Globex order at -1 day) exports only the
Acme order from two days ago.

**UX limitation:** the Blazor criteria editor shows friendly captions (`[Order Date]`,
not `[OrderDate]`) and its visual-tree view reports "Cannot create a tree for this
expression" for this string — the tree builder can't render `LocalDateTimeLastWeek()`.
Text/Advanced mode works and is what the seeded criteria use; token coloring confirms
the fields resolve correctly.

## Scheduling

`ReportSchedule.CronExpression` is a standard Hangfire cron string, evaluated in the
**server's local time zone** (`RecurringJobOptions.TimeZone = TimeZoneInfo.Local`). The
seeded schedule uses `0 7 * * 6` — Saturdays at 07:00.

Hangfire runs on **in-memory storage** (`Hangfire.InMemory`). Every app restart rebuilds
all recurring jobs from the `ReportSchedule` table (a `BackgroundService` does this a few
seconds after startup, retrying while the database comes up). This means:
- Nothing is lost across a restart — schedules are the source of truth, not Hangfire's
  storage.
- A run whose scheduled time falls **while the app is down** is not replayed. There is
  no persistent job queue.

## Run

Prerequisites: .NET 8 SDK, SQL Server LocalDB (`(localdb)\mssqllocaldb`), a DevExpress
26.1.4 license/NuGet feed available to `dotnet restore`.

```bash
dotnet run --project XafReportScheduler.Blazor.Server
```

- HTTP: `http://localhost:5000` (redirects to HTTPS)
- HTTPS: `https://localhost:5001`
- Log in as `Admin` with an empty password (DEBUG-only seed — see Limitations).
- DEBUG builds auto-create/update the database schema on startup; there is no manual
  migration step for local dev.

Job execution reads `ReportJobs` from `appsettings.json`:

```json
"ReportJobs": {
  "UserName": "Admin",
  "Password": "",
  "OutputFolder": "output"
}
```

`OutputFolder` is resolved against the Blazor.Server content root when relative (and used
as-is when rooted); the default `output` folder is git-ignored. A schedule can override
it per-row via its own `OutputFolder` field.

## E2E

Once, after the first build, install the Playwright browser:

```bash
pwsh XafReportScheduler.E2ETests/bin/Debug/net8.0/playwright.ps1 install chromium
```

Then:

```bash
dotnet run --project XafReportScheduler.E2ETests
```

A self-contained console app (C# `Microsoft.Playwright`, headless Chromium): pre-cleans
seed data, builds and starts the app on port `5100` (separate from the dev port), then
asserts, against the real running app. It uses the same dev LocalDB catalog as `dotnet run
--project XafReportScheduler.Blazor.Server` — it deletes and re-seeds `Orders`/`Customers`
and asserts exactly one enabled recurring schedule is registered, so don't run it against
a database you care about the contents of. Assertions:
- the seeded report is non-predefined (SQL),
- the report designer surface renders for it,
- **Run Now** on the seeded CSV schedule produces a CSV containing only the order that
  matches both criteria,
- the schedule's `LastRunStatus` becomes `Succeeded`,
- the enabled recurring job was registered at startup (`Registered 1 report schedules`
  in app stdout).

Screenshots from a passing run land in `XafReportScheduler.E2ETests/bin/Debug/net8.0/screenshots/`
(git-ignored, printed at the start of the run) — separate from the committed evidence
screenshots checked into `docs/screenshots/`, which each E2E run no longer overwrites. Exit
code `0` on full pass, `1` on a failed assertion, `2` if the Playwright browser isn't installed
(see the install step above).

## Limitations

- **Folder sink only.** Exports land on local disk; no e-mail or other delivery.
- **No run-history entity.** Only the most recent run's status/message/output path are
  kept, on the `ReportSchedule` row itself.
- **In-memory Hangfire.** See Scheduling above — no persistent job storage, no replay
  of missed runs.
- **Criteria strings are passed through as-is.** XAF object parameters (e.g.
  `CurrentUserId()`) are not supported — there's no dialog to resolve them against in an
  unattended job.
- **SQL Server LocalDB only** — no other database provider is wired up.
- **Credentials.** DEBUG builds seed `Admin`/`User` with empty passwords. The job's
  service user (`ReportJobs:UserName`/`Password` in `appsettings.json`) is plaintext
  config, and the job runs with that user's full permissions. Fine for a POC; beyond
  that, use `dotnet user-secrets` or encrypted settings instead.
- **Retries.** Hangfire automatic retry is disabled (`Attempts = 0`). A failed run shows
  up on the schedule row (`LastRunStatus`/`LastRunMessage`); re-run it via **Run Now**.
- **Startup sync window.** `ReportScheduleSyncService` retries for ~30 s after host start
  before giving up on registering recurring jobs. In DEBUG, the database schema update
  runs on the first browser request — on a cold/fresh database, open the app within that
  window (or restart it) so the sync attempt lands after the schema exists.

## Licence

Source only. This repository does not include a DevExpress licence; you need your own
DevExpress subscription/NuGet feed to restore and build it. This project is not a
DevExpress product and is not affiliated with DevExpress.
