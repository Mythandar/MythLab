# Windows homelab manager — working name

Local native WPF foundation targeting .NET 10. Public product name is undecided.

## Build and run
Requires Windows and .NET SDK 10.0.400 or later 10.0 feature-band patch.

```powershell
dotnet restore RemoteManager.slnx
dotnet build RemoteManager.slnx --no-restore
dotnet test RemoteManager.slnx --no-build
dotnet run --project src/RemoteManager.App
```

Current scope: persistent managed-device CRUD, duplicate validation, groups/notes/tags, stored WoL configuration, settings and diagnostics. Discovery, actual status polling, wake, credentials and connection execution are tracked as subsequent milestones. No fabricated devices/status or working-looking connection buttons.

Normal data is stored in a Data folder beside the executable. A single-executable, self-contained Windows x64 build is available; see [portable publishing](docs/PORTABLE.md). No credentials are collected in this milestone. Change the display name in build/Identity.props. See [architecture](docs/ARCHITECTURE.md), [roadmap](docs/ROADMAP.md) and [dependencies](docs/THIRD-PARTY-NOTICES.md).

No GitHub remote or installer has been created.

Verification: Release build clean, 40 tests passing, offscreen WPF smoke checks passing. See [verification details](docs/VERIFICATION.md). Run the UI harness with `dotnet run --project tools/RemoteManager.SmokeTests -c Release`.
