# Roadmap

This session targets the initial task and Milestone A. Subsequent milestones are intentionally separate working increments, each requiring a clean build, offline tests, documentation update and local commit if Git is in use.

## A — Foundation (complete)
- [x] Inspect environment and verify .NET 10
- [x] Record architecture, candidate terminal approach and dependency/license choices
- [x] Three production projects and two test projects
- [x] WPF MVVM shell, DI and rolling structured logs
- [x] Stable naming configuration and per-user data/settings
- [x] Device/profile/credential-reference models, SQLite migrations
- [x] Managed-device Add/Edit/Delete, duplicate detection, metadata and WoL configuration
- [x] Build, automated persistence/domain tests and desktop smoke verification

## B — Discovery
- [ ] Interface/mask enumeration and interface selection
- [ ] Bounded cancellable IPv4 scan; handle large subnets explicitly
- [ ] Neighbor-cache merge, local ARP, reverse DNS; do not require ping
- [ ] Results with evidence/freshness and managed matching
- [ ] Add selected result with prefilled editor
- [ ] Offline tests using fake probes; optional OUI provider later

## C — WoL/status
- [ ] MAC, packet, subnet and broadcast tests
- [ ] UDP broadcast transport, selected NIC and conservative retries
- [ ] Configurable asynchronous ICMP/TCP monitoring
- [ ] Wake / Test Wake, state feedback and observed verification

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
2026-09-19: Milestone A complete. Final Release build: zero warnings/errors. 35 offline tests passed. WPF smoke harness passed with no binding errors; rendered light/dark inventory and editor reviewed. NuGet reported no known vulnerable packages. See VERIFICATION.md for reproducible commands and manual desktop checks still needed. Next: Milestone B, including subnet/broadcast unit tests before discovery transport.
