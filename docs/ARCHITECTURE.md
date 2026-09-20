# Architecture

## Scope and environment
Milestones A and B provide a working Windows inventory and LAN discovery, not a v0.1 feature-complete release. Inspected 2026-09-19: Windows 11 x64 (10.0.26200), .NET SDK 10.0.400, Windows Desktop runtime 10.0.11, Git 2.55. No extra workloads required for WPF. No inherited AGENTS.md found. Local directory: D:\projects\repos\RemoteManager. No GitHub remote.

## Structure and dependencies
- App: net10.0-windows WPF, MVVM with CommunityToolkit.Mvvm, built-in WPF Fluent theme. No browser-based app shell.
- Core: domain records, validation and small service contracts; no WPF, database or native networking dependency.
- Infrastructure: Microsoft.Data.Sqlite, explicit SQL and numbered schema migrations. No ORM: the initial model is small and migrations remain reviewable.
- App composition: Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Logging via Serilog and rolling JSON file sink. No generic host needed until hosted background work exists.
- Separate Core and Infrastructure test projects use xUnit and Microsoft.NET.Test.Sdk. Tests are offline and never send network packets.
- Exact dependency versions and licenses: THIRD-PARTY-NOTICES.md and NuGet lock files.

## Naming
build/Identity.props is the authoritative location for display name, assembly prefix and stable internal identity. It supplies executable version resources and generated AppIdentity constants. Project folders, .slnx and internal namespaces remain neutral RemoteManager names; a later public name does not require changing namespaces. If internal names must also change, rename the projects/folders and replace RemoteManager namespaces in one dedicated refactor. Keep the data-directory identity stable or implement an explicit migration. No installer, logos, or splash screen yet; an eventual installer must import Identity.props.

## Domain and persistence
Device has a GUID independent of address, descriptive metadata, last observation, status-check configuration and unverified/verified WoL settings. ConnectionProfile has its own GUID and device foreign key; many profiles can belong to one device. CredentialReference is separate reusable metadata; profile rows reference its GUID. No secret-bearing property exists in serializable models. Authentication type and private-key path belong to credential metadata; saved passwords/passphrases will live only in Windows Credential Manager (Milestone D).

SQLite, settings and logs live in Data beside the executable (AppContext.BaseDirectory). The stable application ID is retained to locate pre-portable AppData for one-time migration and future credential target naming. See PORTABLE.md for publishing, migration and secure-secret portability boundaries. Tables separate devices, credentials and profiles; profile foreign keys cascade with device deletion and restrict deletion of referenced credentials. Device bodies are versioned JSON inside SQLite alongside normalized lookup columns. This keeps early configuration flexible without combining device/profile/credential identity; introduce typed columns in migrations when queries justify them. IP is a duplicate warning signal, never identity. Matching prioritizes normalized MAC, case-insensitive hostname and then current IP. Saving rejects any match to another device so DHCP collisions require deliberate editing, not accidental merging.

Repository operations run on worker threads behind a semaphore; Microsoft.Data.Sqlite async methods are synchronous, so merely awaiting them would still block WPF. Use WAL, parameters, transactions and a short busy timeout. Cancellation is honored while queued and before commit; an already committed operation remains committed. Migrations reject newer schemas, never silently overwrite them. Normal settings use atomic JSON replacement and validation. Unknown/broken settings are reported, not silently discarded.

## UI and lifetime
Shell uses navigation and device cards, with a dedicated modal editor for Add/Edit. Validation stays in Core and is displayed in the editor. Delete requires a UI confirmation. Async commands disable overlapping mutations. App startup awaits database initialization and initial load; fatal startup failure produces a safe error and exits. No passwords or raw exception messages are included in structured logs. Disk logs roll daily/at 5 MB and retain at most seven files. Diagnostics displays bounded recent operational entries. Only Unknown is shown until real monitoring exists. Future screens are clearly labeled as upcoming.

## Discovery (Milestone B)
Enumerate active IPv4 unicast interfaces and actual masks. Permit interface selection. Bound scan concurrency, constrain large ranges and allow explicit ranges rather than silently sweeping a /8. Merge ICMP, GetIpNetTable IPv4 neighbor information and local SendARP evidence, then bounded reverse DNS. ICMP failure must not exclude a neighbor. Native calls run away from UI with bounded work; cancellation stops scheduling, even where a native call cannot be interrupted. Cache observations with timestamps; stale ARP does not prove Online. OUI provider is optional and must work offline. No elevation should be required for ordinary read-only discovery; document unsupported/restricted calls. Never infer WoL support from discovery.

## WoL and monitoring (Milestone C)
Pure MAC/subnet/magic-packet functions are tested before UDP transport. Pick directed broadcast from selected interface unless explicitly configured. Device capability defaults Disabled; Enabled is unverified and Verified requires an observed successful wake. Poll with bounded concurrency and configurable intervals (default 30 seconds). Unknown, Offline, Online and Waking remain distinct. ICMP or configured TCP can provide host status, while connection readiness can use a different port.

## SSH decision record (Milestone D)
Candidate: SSH.NET -> ShellStream -> bounded terminal bridge -> locally bundled xterm.js in WebView2. SSH.NET supplies password/key auth and host-key events; ShellStream supplies a PTY but is not a terminal emulator. Plain WPF TextBox rendering is inadequate for full-screen terminal apps. Alternatives: native third-party terminal controls bring licensing/maintenance lock-in; external OpenSSH does not provide the required saved-password integrated experience; ConPTY applies to local sessions and does not replace SSH transport.

Do not commit a terminal renderer dependency until a Windows spike validates: UTF-8 chunk boundaries, ANSI/vim/top behavior, input/resize, copy/paste, reconnect/disposal, simultaneous streams, throughput/backpressure, keyboard/IME/accessibility and WebView2 DPI/airspace behavior. Keep a small session boundary (input, output, resize, close) usable by future ConPTY sessions; do not invent a plugin framework.

Security gate: package assets locally with license copies; never load a CDN. WebView2 needs an installed Evergreen runtime or documented installer bootstrap; fixed runtime increases deployment size and patch responsibility. Restrict virtual host/content origin, navigation, new windows, downloads and permissions. Enforce CSP and validate origin/type/size of every bridge message. No generic host-object exposure, eval of remote text, or arbitrary URL handlers. Treat terminal output and clipboard escape sequences as hostile. Never log input/output because users may type secrets. Disable automatic clipboard writes.

Known hosts must key by normalized host + port, key algorithm and SHA-256 fingerprint/public key. Unknown keys require an explicit fingerprint trust decision before authentication; changed keys fail closed and require deliberate replacement. Do not offer an invisible accept-all mode. Credential Manager targets use stable GUID references; secrets are fetched only for authentication, never displayed back to UI. Cleanup is best effort for managed memory; do not promise zero plaintext in process memory.

## Launchers and orchestration (Milestones E/F)
Allowlisted placeholders: {ip}, {host}, {hostname}, {port}. Arguments are a list passed via ProcessStartInfo.ArgumentList, never assembled shell command text. Require explicit executable configuration; reject secret placeholders. Web permits only http/https URLs. RealVNC/Moonlight executables are detected or selected, not bundled. Connection readiness may be TCP; web status can evolve later.
Wake & Connect orchestrates host and service readiness with one cancellation token, finite timeout and explicit stages. Already-ready services connect immediately. Wake verification only after observing offline -> wake -> reachable; do not claim packets prove remote support.

## Sources
- [SQLite async limitations](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async)
- [SSH.NET](https://github.com/sshnet/SSH.NET) and [ShellStream API](https://sshnet.github.io/SSH.NET/api/Renci.SshNet.ShellStream.html)
- [WPF WebView2](https://learn.microsoft.com/en-us/microsoft-edge/webview2/get-started/wpf)
- [WebView2 distribution](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)
- [xterm.js security](https://xtermjs.org/docs/guides/security/)

## Implementation notes after Milestone A
WPF Fluent dictionaries are explicitly merged and customized controls derive from explicit Fluent base styles. ThemeManager isolates the WPF0001 runtime-theme API opt-in; the SDK still marks it experimental. Dark/light rendering was checked with an offscreen WPF harness. Core IPv4 validation normalizes decimal octets explicitly so leading zeros cannot invoke legacy octal parsing. 72 automated tests cover the foundation, portable paths and discovery, including subnet/broadcast calculations. Packet/template tests will accompany their implementations in C/E.

## Portable data / secure secrets — binding v0.1 decision
Portable Data is the default architecture. All inventory, device and connection definitions, usernames/non-secret metadata, credential references, WoL configuration, MAC/IP/hostname, groups/tags/notes and settings stay in Data. SSH trust records use Data/ssh/known-hosts.json; logs use Data/logs; a future terminal uses Data/WebView2 for browser user data/cache. AppPaths owns these paths. Startup actively probes Data and logs for write access and reports failures without an AppData fallback.
Secrets remain exclusively in Windows Credential Manager. A copied folder can have valid profiles with missing local credentials. Milestone D must show Missing credential and recreate the secret under the same GUID reference, preserving every device/profile. It must distinguish missing from access denied/store errors and must never fall back to plaintext SQLite/JSON. No portable encrypted vault in v0.1. See DATA-SECURITY.md for the full policy and acceptance gates.
WebView2 is lazy and terminal-only: prefer installed Evergreen, do not bundle Fixed Version by default, detect missing runtime before terminal creation and provide a useful terminal diagnostic. Explicitly select AppPaths.WebView2UserData and check writability. Non-terminal features must run without any WebView2 runtime. No WebView2 dependency is introduced in Milestone B; detection/creation tests belong to the terminal spike.

## Discovery implementation decision (Milestone B)
Core owns IPv4 subnet/range rules and discovery evidence models. Infrastructure owns the bounded scan engine and ILanProbe Windows implementation. GetIpNetTable suffices for IPv4; native row/table layouts use Marshal offsets and strides. Cache data is evidence only, never proof of Online. Ping/DNS use async cancellation; SendARP has a process-wide eight-call cap retained until native calls return. Scan ranges are explicitly limited to 4096 addresses on the selected subnet. Interface availability is rechecked, and IPv6-only/disappearing interfaces are skipped. See DISCOVERY.md for routing, cache-age and timeout limitations.
The Discovery view model streams rows, tracks managed matches, opens the normal editor with discovered fields, and leaves WoL Disabled by default. The main window cancels and drains discovery before disposing shared services. No terminal/browser environment is initialized for discovery. Microsoft.Extensions.Logging.Abstractions is now referenced by Infrastructure for structured network diagnostics (MIT; already a resolved dependency).
