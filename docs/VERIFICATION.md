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
dotnet restore MythLab.slnx --locked-mode
dotnet build MythLab.slnx -c Release --no-restore
dotnet test MythLab.slnx -c Release --no-build
dotnet run --project tools/MythLab.SmokeTests -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Write-DependencyLicenses.ps1
dotnet list MythLab.slnx package --vulnerable --include-transitive
```

## Limits and manual follow-up
Offscreen rendering is not a full interactive desktop acceptance test. Manually verify native title bars, 125%/150% DPI, keyboard focus/access keys, screen reader behavior, native delete confirmation, and OS theme changes on the target desktop. Test multiple simultaneous application instances before claiming multi-instance support.

The smoke harness composes production view models and repositories but does not run the production OnStartup method or exercise OS credential facilities (not implemented). No installer/publish validation yet; run from the SDK or the built Release executable with the matching .NET Desktop runtime.

The only diagnostic opt-in is WPF0001 in ThemeManager: WPF marks dynamic ThemeMode switching experimental even in this .NET 10 SDK. The adapter contains it; no project-wide warning suppression.

## Portable follow-up — 2026-09-19
- Release build: zero warnings/errors. 40 tests passed (26 Core, 14 Infrastructure).
- Added executable-relative path, fresh portable setup, existing-folder protection, committed WAL/settings migration, and failed-import rollback tests.
- Published app output contains exactly MythLab.exe, approximately 63.4 MiB, with .NET and SQLite bundled.
- Published and executed the WPF smoke harness as a self-contained single-file Windows x64 executable; all existing UI/SQLite checks passed. This validates the deployment mechanism, not a manual interactive launch of the final application.
- Normal locked restore passes. Portable publishing has its own packages.portable.lock.json because the RID and SDK single-file build tooling change restore requirements.
- Package inventory now includes the SDK-added Microsoft.NET.ILLink.Tasks (MIT); trimming remains disabled. Restore with -p:PublishProfile=Portable before regenerating the complete publish-time license inventory.

## Milestone B — portable policy and LAN discovery
- Release build: zero warnings/errors. 72 tests passed (47 Core, 25 Infrastructure), no failures/skips.
- Offline coverage now includes subnet/broadcast/mask/range rules, non-ping ARP/cache results, interface/range isolation, concurrency, cancellation, changed adapters and cache failures.
- Portable boundary tests verify known-host/WebView2 paths under Data, write-probe cleanup, no browser initialization and write failure without fallback. Serialization allowlists protect credential/profile metadata shapes.
- WPF smoke passed: Discovery renders in light/dark, scans fake evidence, opens and saves the prefilled Add dialog, marks results managed, cancels scans, and drains cancellation before closing the main window. No binding errors. Final Discovery screenshot reviewed.
- Explicit live native smoke: Windows enumeration found one active IPv4 interface and eleven valid ARP entries; at most one cached on-subnet neighbor was scanned successfully. This caught and fixed IPv6-only adapter enumeration failures. It is not a full-LAN acceptance test.
- NuGet vulnerability query: no known vulnerable packages reported by configured sources. No new third-party library; Infrastructure now directly references the existing Microsoft.Extensions.Logging.Abstractions package (MIT).
- Portable executable rebuilt; existing Data directory preserved. Missing-secret recovery, SSH trust storage and WebView2 runtime detection remain Milestone D implementation gates; they are documented, not advertised as implemented.
- Remaining manual checks: full subnet discovery on the target LAN, cancellation during native ARP delays, overlapping/VPN adapters, DPI/keyboard/accessibility. OUI lookup is a provider boundary without a bundled database. Monitor/WoL transport remain Milestone C.

## Appearance and card-layout fix
WPF smoke now selects Light/Dark/System through the actual Settings ComboBox, verifies theme brush changes before Save and persistence after Save, and checks six cards at 920/1600-DIP window widths. Narrow mode uses one column; wide mode uses at least three with no vertical scroll when cards fit; reducing window height restores scrolling. Wide dark and narrow light renderings were visually reviewed. Existing discovery/shutdown smoke checks still pass.

## Milestone C — Wake-on-LAN and monitoring
2026-09-19:
- Release solution build succeeded with zero warnings/errors.
- 93 automated tests passed (57 Core, 36 Infrastructure). New coverage checks all magic-packet bytes, invalid MACs, real-mask routing, ambiguous/missing interfaces, configured burst sends, cancellation, timeout, already-online and unknown-baseline verification rules, and status/edit/delete races.
- Wake transport is replaced by fakes in tests. The production TCP checker is tested only against a temporary loopback listener; no Internet, LAN scan or real wake packet is required.
- WPF smoke passed with no binding errors: ten managed devices reached Online with exactly eight simultaneous probes at peak; Test Wake persisted verification; cancelling a wake restored controls; closing drained wake, monitoring and discovery. Existing theme and responsive-card checks still pass. Reviewed dark-mode verified-wake rendering.
- Locked portable publish succeeded: artifacts/portable/MythLab.exe, 66,511,936 bytes (about 63.4 MiB), one executable. Existing portable Data remains untouched.
- No new packages or license changes. App/Core/Infrastructure boundaries remain intact.
- Physical waking and target-specific BIOS/NIC/firewall settings still need manual acceptance on a known supported computer. Automated tests deliberately cannot establish real hardware support. See WAKE-ON-LAN.md.
## MythLab rename — 2026-09-20
- Display name and executable assembly name are now MythLab through build/Identity.props. Solution, projects, namespaces and WPF XAML identities were renamed.
- Portable Data remains beside MythLab.exe. AppDataId deliberately remains Homelab.RemoteManager so an older nonportable inventory can still be imported once.
- Release build: zero warnings/errors. 93 automated tests passed; WPF smoke passed with no binding errors. Published a one-file MythLab.exe; no new dependencies and no remote created.

## Pre-Milestone D repository review — 2026-09-20
- Discovery now reuses a usable MAC from the selected interface's neighbor cache instead of calling direct SendARP for that address. It still performs the live ping. Invalid cache MACs are ignored, and wrong-interface entries do not suppress ARP.
- The Discovery grid labels its timestamp “Scan read”; it is the time evidence was collected by the scan, not a cache entry's remote last-alive time. A separate live observation timestamp supplies LastSeen only after ping/local-interface evidence. Neighbor-cache evidence alone remains Discovered.
- Status checks try distinct resolved IPv4 addresses within one overall DNS/check timeout. Time is reserved for later addresses; DNS failure, check failure, timeout and caller cancellation remain distinct. Two loopback tests exercise TCP behavior locally and are tagged LocalNetwork.
- Windows CI uses windows-2022, the .NET 10 SDK selected by global.json (10.0.400 feature band), locked restore, Release build, and only offline automated tests with read-only repository permissions. WPF smoke and LocalNetwork-tagged tests remain local.
- Root project licensing remains undecided. Dependency licenses do not grant a MythLab project license.
- Local locked restore passed. Release build passed with zero warnings/errors. Complete automated suite: 97 passed (57 Core, 40 Infrastructure). CI-filtered local selection: 95 passed (57 Core, 38 Infrastructure). Local WPF smoke passed with no binding errors. No live LAN scan or WoL packet was sent during verification.
