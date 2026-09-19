# Dependencies and licenses

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

## Candidates only — not installed in Milestone A
| Component | License | Decision |
| --- | --- | --- |
| [SSH.NET](https://github.com/sshnet/SSH.NET/blob/develop/LICENSE) | MIT | Preferred SSH transport; audit selected version's transitives when added |
| [xterm.js and fit addon](https://github.com/xtermjs/xterm.js/blob/master/LICENSE) | MIT | Candidate terminal renderer, bundle assets and license locally |
| [Microsoft.Web.WebView2 SDK](https://www.nuget.org/packages/Microsoft.Web.WebView2) | Microsoft proprietary SDK terms | Candidate WPF host; retain package LICENSE when adopted |
| [WebView2 Runtime](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution) | Microsoft runtime distribution terms | Prefer Evergreen, document deployment prerequisite |

RealVNC and Moonlight are independently installed programs, not dependencies distributed by this project. No terminal library, browser runtime or external launcher is redistributed yet. This is an engineering inventory, not a license chosen for the new application's own source.
