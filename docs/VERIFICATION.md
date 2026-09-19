# Milestone A verification — 2026-09-19

## Automated
- Installed SDK: 10.0.400, Windows Desktop runtime 10.0.11, Windows x64.
- Debug build: zero warnings/errors.
- Final Release build: zero warnings/errors; warnings are treated as errors.
- Release unit tests: 35 passed (26 Core, 9 Infrastructure), zero skipped/failed.
- WPF smoke harness: passed. Creates offscreen window handles, renders each navigation view, exercises editor validation/add/edit/duplicate messages, inventory search/delete and settings commands. Tests light/dark switching and captures binding errors. Uses a unique temporary database, not user application data. Images are under ignored artifacts/ui-smoke.
- Reviewed rendered light/dark device view and editor. Fixed inherited Fluent styles after visual review found dark-mode contrast problems.
- NuGet vulnerability query: no known vulnerable packages reported by configured nuget.org source for the five solution projects at verification time.
- All 29 resolved package licenses inventoried in dependency-licenses.json. Available packaged license files retained in docs/licenses.
- No real network scans, wake packets, remote sessions, shell commands or credential writes occur in tests.

## Reproduce
```powershell
dotnet restore RemoteManager.slnx --locked-mode
dotnet build RemoteManager.slnx -c Release --no-restore
dotnet test RemoteManager.slnx -c Release --no-build
dotnet run --project tools/RemoteManager.SmokeTests -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Write-DependencyLicenses.ps1
dotnet list RemoteManager.slnx package --vulnerable --include-transitive
```

## Limits and manual follow-up
Offscreen rendering is not a full interactive desktop acceptance test. Manually verify native title bars, 125%/150% DPI, keyboard focus/access keys, screen reader behavior, native delete confirmation, and OS theme changes on the target desktop. Test multiple simultaneous application instances before claiming multi-instance support.

The smoke harness composes production view models and repositories but does not run the production OnStartup method or exercise OS credential facilities (not implemented). No installer/publish validation yet; run from the SDK or the built Release executable with the matching .NET Desktop runtime.

The only diagnostic opt-in is WPF0001 in ThemeManager: WPF marks dynamic ThemeMode switching experimental even in this .NET 10 SDK. The adapter contains it; no project-wide warning suppression.
