# Dependencies and licenses

These notices describe dependencies only. MythLab has no root project license yet; the repository owner must select one if the project is intended for open-source reuse. Do not infer a project license from dependency licenses.

Exact installed versions are recorded in Directory.Packages.props and packages.lock.json. The generated docs/dependency-licenses.json inventories every resolved direct/transitive NuGet package and its package license metadata. Re-run tools/Write-DependencyLicenses.ps1 after dependency changes. Keep copyright/license files with distributed dependencies.

## Foundation choices
| Package/component | License | Reason |
| --- | --- | --- |
| .NET / WPF | MIT (runtime redistribution includes Microsoft notices) | Native Windows platform |
| CommunityToolkit.Mvvm | MIT | Tested observable properties and async commands |
| Microsoft.Extensions.DependencyInjection / Logging | MIT | Small composition root and standard structured logging |
| Microsoft.Data.Sqlite / Core | MIT | Parameterized SQLite without an ORM |
| SQLitePCLRaw dependencies | Apache-2.0 | SQLite native interop used by provider |
| SQLite engine | Public domain | Embedded database; bundled through SQLitePCLRaw |
| Serilog / Extensions.Logging / Sinks.File | Apache-2.0 | Structured JSON logs, rolling/retention |
| xUnit / runner | Apache-2.0 | Offline unit tests |
| Microsoft.NET.Test.Sdk / TestPlatform | MIT | Test infrastructure |
| Newtonsoft.Json (test transitive dependency if resolved) | MIT | Test runner dependency |

## SSH terminal dependencies — installed in Milestone D
| Component | Version | License | Reason |
| --- | --- | --- | --- |
| SSH.NET | 2026.0.0 | MIT | Managed SSH transport, key verification and PTY ShellStream |
| BouncyCastle.Cryptography | 2.7.0 | MIT | SSH.NET cryptographic dependency |
| Microsoft.Web.WebView2 SDK | 1.0.4191.47 | Microsoft proprietary SDK terms | WPF terminal host; license/notice copied from package |
| @xterm/xterm | 6.0.0 | MIT | Maintained VT terminal rendering |
| @xterm/addon-fit | 0.11.0 | MIT | Fit terminal geometry to its window |
| Evergreen WebView2 Runtime | Installed, independently serviced | Microsoft runtime terms | Detected lazily; not redistributed |

xterm assets are copied from the official npm tarballs https://registry.npmjs.org/@xterm/xterm/-/xterm-6.0.0.tgz and https://registry.npmjs.org/@xterm/addon-fit/-/addon-fit-0.11.0.tgz: package/lib/xterm.js, package/css/xterm.css, package/lib/addon-fit.js and their LICENSE files. They are embedded in the executable, never fetched at runtime. Selected-file SHA-256 hashes are in terminal-asset-hashes.json. Custom index.html, terminal.js and terminal.css are application source, not upstream assets.

SSH.NET's license is retained from its 2026.0.0 tag. Other package-provided licenses/notices and self-contained .NET/WPF redistribution notices are under docs/licenses. Publish copies these and the xterm licenses into a Notices directory; keep that directory with distributed builds. Dependency license metadata remains in dependency-licenses.json.

RealVNC and Moonlight are independently installed programs, not distributed dependencies. Paramiko 4.0.0 (LGPL-2.1) is an optional local development-only SSH test fixture dependency, not an application dependency. No application source license has been selected.
Portable publishing also resolves Microsoft.NET.ILLink.Tasks 10.0.11 (MIT) as SDK build tooling. It does not enable trimming. The self-contained executable embeds the Windows .NET runtime; preserve its redistribution notices when packaging a release for others.
