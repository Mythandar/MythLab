# Portable data and Windows secrets

This distinction is a v0.1 requirement, not a selectable security mode.

| Data | Storage |
| --- | --- |
| Devices, connections, username/authentication metadata, credential GUID references, WoL, MAC/IP/hostname, groups/tags/notes | Data/inventory.db beside the executable |
| Application settings | Data/settings.json |
| SSH known-host identities and trust decisions | Data/ssh/known-hosts.json |
| Logs | Data/logs |
| Embedded terminal WebView2 user data/cache | Data/WebView2 (created only when a terminal needs it) |
| Passwords, private-key passphrases and other reusable authentication secrets | Windows Credential Manager only (Milestone D) |

Copy/backup the executable and Data together while the app is closed. AppPaths is the authoritative path provider. Runtime-native extraction in Windows temporary storage is not application data.

CredentialReference contains reusable metadata only; ConnectionProfile holds a nullable credential GUID. Never add a secret-bearing field to these serializable types. There is no SQLite/JSON secret fallback. Do not persist machine-local credential availability as though it were portable truth. After a move, resolve each reference against the current Windows credential store. A missing entry means **Missing credential**, not a corrupt device/profile and not a reason to delete/recreate either.

## Milestone D implementation
- Target Windows Credential Manager using the stable internal application ID and credential GUID.
- Distinguish available, missing, access denied and store failure. ERROR_NOT_FOUND is the missing case; do not misreport every failure as missing.
- Show the credential label/username and a replace/recreate-secret action for a missing reference.
- Write the replacement secret to the same reference target; retain device IDs, profile IDs and associations.
- Never read a saved secret back into a UI field; never put it in command arguments, logs, exports or browser storage.
- Test copying portable metadata with an empty fake Windows store, then recreating the same reference. Test access-denied behavior and absence of plaintext fallback.
- A portable encrypted vault is a possible future optional feature and is explicitly out of v0.1.

## WebView2 behavior
Prefer an installed Evergreen runtime. Do not distribute Fixed Version by default. Only when opening the first embedded terminal, check runtime availability through the WebView2 SDK and create the environment with browserExecutableFolder unset and userDataFolder = AppPaths.WebView2UserData. Report a missing runtime as a terminal-specific diagnostic with a link to Microsoft's Evergreen installer; do not install it automatically. Inventory, discovery, monitoring and wake must work without WebView2.

Do not register a WebView2 environment that starts at app startup. Validate Data/WebView2 write access at terminal creation, check environment creation failures and keep failure local to that terminal. Test unavailable runtime, unwritable data folder and normal lazy startup. Do not allow cookies, browser password saving, localStorage or IndexedDB to become an alternate credential store; terminal authentication remains in native code and Windows Credential Manager.

Milestone D implements these storage and lazy-initialization boundaries. Unit tests cover copied metadata and missing/denied secrets; native and browser smoke tests remain local. Missing-runtime and access-policy behavior still require manual acceptance on a machine configured for those cases. See SSH.md.

Sources: [WebView2 distribution](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution), [WebView2 user data](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/user-data-folder), [Windows credentials](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-credreadw).
