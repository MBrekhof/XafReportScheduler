# How it works — for XAF developers

The README states *what* this POC is; this doc explains the design to a fellow XAF
programmer: the question it set out to answer and what made the answer work.

## The original question

Every XAF dev knows the standard way to ship a report: build the `XtraReport` in code,
register it with `PredefinedReportsUpdater`. The catch — a predefined report is
**locked**. `IsPredefined` is true, XAF regenerates it from the code type, end users can
view it but not redesign it. So "ship a report" and "let the customer's admin tweak it"
are normally opposites.

The question this POC answers: **can one XAF Blazor Server app ship a code-built report
that end users can still redesign, let an admin attach a saved filter to it through the
standard criteria editor, and run it unattended on a cron schedule (plus on demand),
exporting to disk?**

## What was realized — the three pieces

### 1. The editable seed trick

`IsPredefined` isn't a flag you set — it's *derived from how the row got there*. Only
rows registered via `PredefinedReportsUpdater` get a `PredefinedReportTypeName`; a row
written at runtime through `IReportStorage.SaveReport` doesn't, and stays editable
([`ReportDataV2.IsPredefined` docs](https://docs.devexpress.com/eXpressAppFramework/DevExpress.Persistent.BaseImpl.EF.ReportDataV2.IsPredefined)).

So the seeder (`ReportSeeder`, a plain `ModuleUpdater`) builds the `XtraReport` in code,
then calls `SaveReport` — never `PredefinedReportsUpdater`. Result: a report authored in
C# but stored as if a user had made it in the designer. The seeder only creates the row
if none with that `DisplayName` exists, so user edits survive restarts. Verified by SQL
assert in the E2E gate: `PredefinedReportTypeName IS NULL`.

### 2. Criteria via the machinery you already have

`ReportSchedule` is a small entity: FK to `ReportDataV2`, a criteria string with
`[CriteriaOptions]`, and a computed `ObjectType` property that follows the selected
report's data type — so the stock popup criteria editor shows the *right field list* per
report. No custom editor.

A finding worth knowing: a criteria string like

```
[OrderDate] >= LocalDateTimeLastWeek() And [Customer.Name] Like 'Acme%'
```

goes through the EF Core criteria pipeline (`IReportExportService.SetupReport`)
unmodified — both the built-in `LocalDateTimeLastWeek()` function and SQL-style `Like`
translate correctly; no `StartsWith` rewrite needed.

One wart: the Blazor criteria editor's visual-tree view can't render
`LocalDateTimeLastWeek()` ("Cannot create a tree for this expression"). Text/Advanced
mode works fine, and token coloring confirms the fields resolve.

### 3. Hangfire, but the entity is the source of truth

Hangfire runs on in-memory storage — deliberately. A `BackgroundService`
(`ReportScheduleSyncService`) rebuilds all recurring jobs from the `ReportSchedule`
rows at startup; saving a schedule re-syncs its job, deleting one unregisters it, and a
"Run Now" action on the detail view enqueues an immediate run.

The job (`ReportJob`) logs on as a configured service user (its own `IObjectSpace`, a
real security context), loads the report, applies the schedule's criteria via
`IReportExportService.SetupReport`, exports to Pdf/Xlsx/Csv, and writes the outcome back
to the schedule row.

Trade-off accepted: nothing is lost across a restart (the rows rebuild the jobs), but a
run whose scheduled time falls while the app is down is not replayed — there is no
persistent job queue. Automatic retries are off (`Attempts = 0`); a failure lands on
`LastRunStatus`/`LastRunMessage` and a human re-runs it via Run Now.

Layering: `ReportJob` and all domain types live in `XafReportScheduler.Module` with
**zero Hangfire reference**; the Hangfire wiring, sync service, and controller live in
`XafReportScheduler.Blazor.Server` only.

## Proof

A C# Playwright E2E gate (`XafReportScheduler.E2ETests`) runs the real app and asserts
the whole chain: seeded report non-predefined → designer opens on it → Run Now exports
a CSV containing *only* the order matching both criteria (recent + Acme; a 30-day-old
Acme order and a recent Globex order are excluded) → `LastRunStatus = Succeeded` →
startup log shows the recurring job registered. Two consecutive green runs.

## The one-line answer

**Yes — seed through `IReportStorage.SaveReport` instead of `PredefinedReportsUpdater`,
and everything else (criteria editor, scheduling) composes on top with stock XAF parts.**
