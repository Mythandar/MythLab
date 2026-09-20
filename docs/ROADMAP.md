# Roadmap

Milestones A–C are implemented. The pre-D review fixed cached-MAC probing, multi-address status checks, discovery freshness wording and added Windows CI. Milestone D (credentials/integrated SSH) is next and has not started. Subsequent milestones are intentionally separate working increments, each requiring a clean build, offline tests, documentation update and local commit if Git is in use.

## A — Foundation (complete)
- [x] Inspect environment and verify .NET 10
- [x] Record architecture, candidate terminal approach and dependency/license choices
- [x] Three production projects and two test projects
- [x] WPF MVVM shell, DI and rolling structured logs
- [x] Stable naming configuration and portable data/settings
- [x] Device/profile/credential-reference models, SQLite migrations
- [x] Managed-device Add/Edit/Delete, duplicate detection, metadata and WoL configuration
- [x] Build, automated persistence/domain tests and desktop smoke verification

## B — Discovery (complete)
- [x] Interface/mask enumeration and interface selection
- [x] Bounded cancellable IPv4 scan; handle large subnets explicitly
- [x] Neighbor-cache merge, local ARP, reverse DNS; do not require ping
- [x] Results with evidence/freshness and managed matching
- [x] Add selected result with prefilled editor
- [x] Offline tests using fake probes; optional OUI provider later

## C — WoL/status (complete)
- [x] MAC normalization, subnet and broadcast calculations/tests
- [x] Magic packet generation and tests
- [x] UDP broadcast transport, selected NIC and conservative retries
- [x] Configurable asynchronous ICMP/TCP monitoring
- [x] Wake / Test Wake, state feedback and observed verification

## D — Credentials/SSH
- [ ] Windows Credential Manager, reusable metadata CRUD and secret replacement
- [ ] Persist connection profiles and host-key records through dedicated repositories
- [ ] SSH.NET transport, explicit trust/reject and changed-key protection
- [ ] Renderer spike: locally bundled xterm.js/WebView2 security and usability gates
- [ ] Functional interactive SSH terminal, password/key auth, resize, clipboard and reconnect
- [ ] License asset copies and runtime installation guidance

## E — External connections
- [ ] RealVNC / Moonlight executable configuration and optional detection
- [ ] HTTP(S), PowerShell and custom executable profiles
- [ ] Safe argument-list templates and tests; no secret substitution
- [ ] Per-profile readiness checks

## F — Wake & Connect
- [ ] Cancellable orchestration, finite timeout and precise failure stages
- [ ] Already-ready / disabled-WoL / host-ready-but-service-down paths
- [ ] Automatically open selected profile after readiness; offline state-machine tests

## Deferred
ConPTY, tabs, tray mode, scheduled wake, SFTP, jump hosts, shutdown, integrations, import/export, relay and topology. Groups/notes/tags are basic device metadata now, not a separate management subsystem. Export will use explicit safe DTOs and never read secret storage.

## Verification record
2026-09-19: Milestone A complete. Final Release build: zero warnings/errors. 35 offline tests passed. WPF smoke harness passed with no binding errors; rendered light/dark inventory and editor reviewed. NuGet reported no known vulnerable packages. See VERIFICATION.md for reproducible commands and manual desktop checks still needed. Milestone B subsequently completed; see the latest verification below.

## Portable packaging follow-up
- [x] Store normal settings, SQLite inventory and logs in Data beside the executable.
- [x] Preserve existing AppData inventory/settings through one-time SQLite backup migration.
- [x] Self-contained Windows x64 single-executable publish profile; embedded Release symbols.
- [x] Five portable-path/migration tests; 40 total tests pass.
- [x] Document/enforce portable paths, startup write access, no-secret serialization, Windows-only secrets and lazy Evergreen decision.
- [ ] Implement missing-credential recovery and terminal-only runtime detection in Milestone D (see DATA-SECURITY.md).

Milestone B verification: 72 offline tests pass (47 Core, 25 Infrastructure), WPF scan/add/cancel smoke passes, and an explicit one-host live smoke verified Windows adapter/ARP interop. Release packaging remains one self-contained executable. At that milestone, no OUI database, status monitor or wake transport was included; C adds monitoring and wake.

UI follow-up: appearance selection now previews immediately (Save persists it), and device cards reflow by viewport width with scrolling only for overflow. Verified using real appearance control bindings and narrow/wide/short-window smoke checks.

Milestone C verification: clean Release build; 93 automated tests and WPF monitoring/wake/cancellation/layout smoke passed. Portable single-executable publish succeeded. No real wake packets sent during tests. Hardware acceptance remains manual; see WAKE-ON-LAN.md and VERIFICATION.md.

Pre-D review complete: cache-aware discovery without skipping live checks, truthful scan-read timestamps, multi-address status checks and offline Windows CI. Milestone D remains unstarted. See VERIFICATION.md.
