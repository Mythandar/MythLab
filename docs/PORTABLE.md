# Portable build

## Use
Publish from the repository root:

```powershell
dotnet publish src/RemoteManager.App -p:PublishProfile=Portable
```

The Windows x64 output is artifacts/portable/RemoteManager.exe. Copy it into a writable folder and run it. The public display name remains configurable in build/Identity.props.

The shipped application is one executable, with .NET 10 and native SQLite bundled. No SDK or separately installed .NET runtime is required. Normal builds under bin still contain developer dependency files; distribute the publish output, not bin. Release debugging symbols are embedded in the assemblies.

After use, the folder looks like:

```text
RemoteManager.exe
Data/
  inventory.db
  settings.json
  logs/
```

SQLite may create inventory.db-wal and inventory.db-shm inside Data while the application is running. settings.json is created when settings are saved. Close the application before copying/backing up the whole folder. Update by replacing the executable while closed and keeping Data. An empty Data folder deliberately starts a fresh portable inventory.

Keep this folder somewhere writable, such as a personal tools folder or removable drive. Read-only media and Program Files are not supported storage locations for portable mode. A write failure is reported; the app never silently redirects data to AppData or requests administrator privileges.

## Existing inventory
If Data does not yet exist and the original %LOCALAPPDATA%/Homelab.RemoteManager contains an inventory or settings, startup copies them once. It uses SQLite's backup API to include committed WAL content, stages the result, and renames the staging folder into place. Existing portable Data always wins. The original inventory/settings remain untouched; historical logs stay in the original location, while new logs are portable. If import fails, startup reports an error and does not start with a partial or empty replacement.

## Portability boundaries
- All ordinary application data uses AppContext.BaseDirectory/Data, never the current working directory or a native extraction directory.
- .NET's bundled native libraries extract into the Windows temporary cache at runtime. This is executable/runtime material, not inventory or settings. This is a no-install portable app, not a promise of zero filesystem traces.
- Future Windows Credential Manager secrets belong to Windows' secure user store and do not automatically move with Data. No passwords are saved in this milestone. Any future portable encrypted vault or credential transfer needs an explicit design; portable mode must not introduce plaintext password storage.
- Future private-key/executable paths may need updating on another PC. Prefer relative paths for intentionally bundled user assets.
- Future WebView2 user data/cache uses Data/WebView2. Prefer installed Evergreen with lazy terminal-only detection. Do not bundle Fixed Version by default, and do not require WebView2 for inventory/discovery/wake. See DATA-SECURITY.md.
- Self-contained .NET runtime updates require rebuilding/replacing the executable. Publish trimming is disabled for WPF.
- When distributing beyond local development, ship the required runtime and third-party notices (a notices subfolder is acceptable); the dependency inventory remains in docs.

Sources: [Microsoft single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview), [Windows credential handling](https://learn.microsoft.com/en-us/windows/win32/secbp/handling-passwords).

Portable data and nonportable secrets are a fixed v0.1 boundary: a moved profile with a missing Windows credential must be repaired by recreating the same credential reference, not by deleting the profile or falling back to plaintext. Known-host records are reserved under Data/ssh/known-hosts.json. Startup tests Data and log-folder write access explicitly.
