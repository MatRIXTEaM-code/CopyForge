# Changelog

## 3.4.1
- Verify can also compare files skipped as "identical" byte for byte and copy them again if they differ: Settings checkbox / `--verify-skipped`. Catches silent corruption that same-size-and-time checks miss.

## 3.4.0
- Windows is kept awake while a job runs (can be turned off in the CLI with `--no-keep-awake`).
- Speed limit: Settings "Speed limit" / `--limit-mb N` (uses the CopyForge pipeline so the limit applies).
- Per-file CSV log: Settings checkbox (saved in your AppData folder) / `--log FILE`.
- Shut down or sleep the PC when a job finishes without errors: Settings checkbox / `--after shutdown|sleep` (shutdown gives 60 s to cancel with `shutdown /a`).
- New `cfcopy wipe PATH --yes [--passes N]` secure overwrite-and-delete (not reliable on SSD / USB flash).

## 3.3.12
- Settings: "Sign the manifest" label aligned with the other checkboxes.

## 3.3.11
- Send/receive: extra AES-256-GCM layer (keys derived from the shared secret) on top of TLS; tamper, replay and wrong-key detection. Both ends need 3.3.11 or newer.
- Receiver: limit of 2 simultaneous connections per address.
- New `cfcopy fakecheck` fake-capacity drive test.
- Crash log rotates at 1 MB.
- Audit explains when a valid signature is not pinned (`--pin`).

## 3.3.10
- Security hardening: DLL search restricted to System32, private-key/secret files restricted to the current user, manifest path validation, full path for PowerShell in VSS, modern certificate loading.
- Diagnostics report shows post-quantum crypto support.

## 3.3.9
- Eject wait can be cancelled; status shows "EJECTING - COPY FINISHED" instead of "TRANSFER ACTIVE".

## 3.3.8 - 3.3.6
- Eject retry button; eject waits up to 60 s for the volume to be released.

## 3.3.7
- Verify on a removable drive forces a flush first (fixes spurious first-read mismatches).

## 3.3.5 - 3.3.1
- Direct write of small files on USB sticks (much faster); setting "USB sticks: write small files straight to their final name".
- GUI layout fixes: history/pause buttons, status moved to the header strip.
- USB version detection (warns on USB 2.0).

## 3.3.0 - 3.2.0
- Earlier feature work: verify algorithms, manifests and signing, delta update, VSS, LAN send/receive, job history, watch mode.
