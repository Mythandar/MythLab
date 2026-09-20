# MythLab

Portable native Windows homelab manager built with WPF and .NET 10.

## Build and run
Requires Windows and .NET SDK 10.0.400 or later 10.0 feature-band patch.

```powershell
dotnet restore MythLab.slnx
dotnet build MythLab.slnx --no-restore
dotnet test MythLab.slnx --no-build
dotnet run --project src/MythLab.App
```

Current scope: persistent managed-device CRUD, duplicate validation, groups/notes/tags, live ICMP/TCP status, cancellable Wake/Test Wake, settings/diagnostics, and bounded cancellable LAN discovery with add-to-inventory. Credentials/SSH and connection execution are subsequent milestones. No fabricated devices/status or working-looking connection buttons.

Normal data is stored in a Data folder beside the executable. A single-executable, self-contained Windows x64 build is available; see [portable publishing](docs/PORTABLE.md). No credentials are collected in this milestone. Change the display name in build/Identity.props. See [architecture](docs/ARCHITECTURE.md), [roadmap](docs/ROADMAP.md) and [dependencies](docs/THIRD-PARTY-NOTICES.md).

No GitHub remote or installer has been created.

Verification: Release build clean, 93 tests passing, offscreen WPF smoke checks passing. See [verification details](docs/VERIFICATION.md). Run the UI harness with `dotnet run --project tools/MythLab.SmokeTests -c Release`.

See [Wake-on-LAN/status](docs/WAKE-ON-LAN.md), [LAN discovery](docs/DISCOVERY.md) for scan behavior and limitations, and [data/security policy](docs/DATA-SECURITY.md) for portable metadata versus Windows-secured secrets. No WebView2 runtime is needed for current features.
