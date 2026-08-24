# CLAUDE.md

Instructions for working in this repo. See `README.md` for what the app does and why.

## Project overview

DevExpress XAF 26.1.4 Blazor Server POC (.NET 8, EF Core, SQL Server LocalDB) that
seeds a user-editable report, lets an admin attach filter criteria to it via XAF's
criteria editor, and runs it on a Hangfire cron schedule or on demand ("Run Now"),
exporting to a folder.

- `XafReportScheduler.Module` — domain (`Customer`, `Order`), `ReportSchedule` entity,
  code-built `OrdersReport`, seeding (`ReportSeeder`), `ReportJob` (export logic). No
  Hangfire reference — must stay that way.
- `XafReportScheduler.Blazor.Server` — Hangfire wiring, `ReportScheduleSyncService`
  (startup sync), `ReportScheduleJobs` (register/remove/run-now helpers),
  `ReportScheduleController` (Run Now action + re-sync on save/delete).
- `XafReportScheduler.E2ETests` — console app, C# Playwright, the phase-gate test.

## Build / run / E2E

```bash
dotnet build XafReportScheduler.sln
dotnet run --project XafReportScheduler.Blazor.Server   # http://localhost:5000, https://localhost:5001
dotnet run --project XafReportScheduler.E2ETests         # builds+runs the app on :5100, asserts, exits 0/1
```

LocalDB catalog: `XafReportScheduler` on `(localdb)\mssqllocaldb`. DEBUG builds
auto-update the schema on startup — no separate migration step. Login: `Admin`, empty
password (see Limitations in README — DEBUG-only).

## Non-negotiables

- **DevExpress packages pinned to 26.1.4.** Never mix in 25.2 packages.
- **EF Core only — never XPO** (owner rule, applies to every project in this repo).
- **All DevExpress behaviour claims must be verified** in dxdocs (`devexpress_docs_search`
  / `devexpress_docs_get_content`) or the installed source at
  `C:\Program Files\DevExpress 26.1\Components\Sources\DevExpress.ExpressApp` — never
  guess an API surface.
- **The seeded report must never be registered through `PredefinedReportsUpdater`.**
  `ReportDataV2.PredefinedReportTypeName` for the seeded "Orders Report" row must stay
  null/empty — that's the whole point of the POC (see README "Why the seeded report is
  editable"). `XafReportScheduler.Module/DatabaseUpdate/ReportSeeder.cs` is the only
  place that writes it, via `IReportStorage.SaveReport`.

Also: `XafReportScheduler.Module` must not reference Hangfire (Hangfire lives in
`Blazor.Server` only) — a project-level constraint from the original plan, enforced by
`ReportJob` living in the Module and taking no Hangfire dependency.

## Task state

No `TODO.md` — this repo is not wired to ContextBoard yet. Open items live in
`SESSION_HANDOFF.md` until a board project exists for it.

## Repo state

**Private** on GitHub: `github.com/MBrekhof/XafReportScheduler`, owner account `MBrekhof`
(`gh auth switch -u MBrekhof` if a push 404s). Pushing to `master` is fine.

**Never make the repo public without an explicit go from the owner** — a DevExpress
support ticket about the name and publishing terms is still open, and the committed
`UrlSigningKey` is in git history. See `SESSION_HANDOFF.md` "Open points".
