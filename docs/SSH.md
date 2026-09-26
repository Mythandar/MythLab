# SSH connections

Milestone D adds integrated SSH using SSH.NET and locally bundled xterm.js in the installed Evergreen WebView2 Runtime.

## First connection
1. Add a managed device with a hostname or IP.
2. Open Connections / Credentials and Add credential. Supply a label, username and password, or choose PrivateKey and an existing key file. Save a passphrase only when needed.
3. Add SSH, selecting the device, credential, port and connection timeout.
4. Connect from this page or the device card. On the first connection, compare the displayed SHA-256 host-key fingerprint with a trusted independent source before choosing Trust.
5. Type in the terminal. Resize the window, select text and use Copy selection / Paste or Ctrl+Shift+C / Ctrl+Shift+V. Multiline paste requires confirmation. Reconnect creates a fresh SSH connection and reloads current metadata/secrets.

Several terminal windows can run at once. Tabs, local ConPTY, SFTP, jump hosts, agent forwarding, keyboard-interactive/MFA and SSH command actions are deferred. SSH connects directly; Wake & Connect is Milestone F.

## Credentials and portable folders
Passwords and saved key passphrases use Windows Credential Manager targets Homelab.RemoteManager/credentials/{GUID}. This stable internal ID survives the MythLab rename. Values are never read back into password fields, sent to the browser, placed in process arguments, or written to configuration/logs. Managed memory necessarily contains authentication material briefly.

The credential label, username, authentication type and optional key path are portable metadata. A copied folder can show Missing credential on another Windows machine/user. Select that credential and Edit / recreate to save its secret again: device and profile IDs stay unchanged. Access denied and store failure are distinct from missing. Private keys without passphrases require no Windows secret; a missing passphrase is shown as optional because the application does not inspect/decrypt every key during inventory loading. An encrypted key still needs its passphrase before connecting.

Private-key files are user-managed, not imported or exported. Relative paths resolve beside the executable. Keep secret key material protected; copying Data does not copy external key files. No portable encrypted vault or plaintext fallback exists.

## Server identity
Trust records live in Data/ssh/known-hosts.json. Unknown keys require explicit approval before authentication; trust is persisted after a successful authenticated connection. Changed keys fail closed, including algorithm changes. Verify a legitimate server replacement independently, then remove the old record under Trusted SSH hosts and reconnect. Refresh updates that list after a connection. Corrupt trust records block SSH instead of silently resetting trust.

## Runtime and diagnostics
Only terminals require installed Evergreen WebView2. A missing runtime is reported in the terminal with a Microsoft runtime download link; installation is never automatic. Inventory, discovery, monitoring and wake remain usable. Fixed Version is not bundled. Browser user data lives in Data/WebView2, whose write access is checked before initialization.

Terminal input/output is not logged or captured by MythLab. The bridge denies external navigation/downloads/permissions, restricts assets to its local origin, validates messages and disables terminal clipboard escape sequences. Explicit user copy/paste still uses the Windows clipboard. Output is bounded; stalled rendering or excessive backlog disconnects instead of growing without limit.

Initial functional terminal validation covers ANSI, split UTF-8, input, resize, selection, CSP, reconnect, real password/key transport and changed-host rejection. Manual acceptance on the user's servers (vim/top, IME, screen reader, DPI changes and sustained workloads) remains necessary.

## Save failures and recovery
Credential saves compensate across SQLite and Windows Credential Manager. If either step fails, MythLab attempts to restore the previous metadata and secret; a new secret is removed when the new metadata cannot be saved. Old secrets used for rollback exist only in memory, never in password fields or files. Authentication-type changes clear or replace the old authentication secret.

If rollback fails, the editor explicitly says manual credential repair may be required. Before connecting, review the credential's authentication type, username and key path and replace/recreate the correct Windows secret. These are not true cross-store transactions: an application/process crash or concurrent edits from another instance can also require repair. Do not assume a failed save succeeded.

Closing during metadata work waits for it to finish and then closes automatically. If WebView2 initialization fails, Reconnect is disabled: correct the runtime/cache-directory problem, close that terminal and open a new one. Ordinary SSH authentication/network failures can still use Reconnect.
