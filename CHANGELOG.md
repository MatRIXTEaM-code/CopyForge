# Changelog

## 3.11.0
- LocalSend window (tray menu or Settings > LocalSend...): receive settings (folder, device name, PIN), an accept/decline prompt in the main window for every incoming transfer (no prompt when a PIN is set), and a device list with Find devices / Send for the files and folders in the main window's source list.
- `cfcopy lsfind` lists LocalSend devices; `cfcopy lssend SOURCE... --to DEVICE [--pin PIN]` sends files or folders (SHA-256 included, device certificate checked against the announced fingerprint).

## 3.10.0
- `cfcopy localsend FOLDER`: receive files from the LocalSend app (protocol v2, HTTPS, multicast discovery). Asks before accepting each transfer; `--pin` makes senders enter a PIN and `--auto-accept` (needs a PIN) skips the question. File names are cleaned, nothing is overwritten, only the accepted sender can upload, SHA-256 is checked when the sender gives it.
- Tray icon (always present while CopyForge runs): save the current job as a preset, run or remove presets from its menu. Running a preset asks the same confirmation as a normal job.
- Settings: "Check a plugged-in drive against its repair data" - when a drive with a `.cf_parity` folder is plugged in, CopyForge checks it in the background and shows a notification.

## 3.9.0
- Settings: "Repair data after copy" percentage (copy, move and sync write repair data to every destination after a clean job) and a "Check / repair a folder..." button.
- `cfcopy schedule` can now run `protect` and `repair` (for example a weekly `repair FOLDER --check`).
- Release workflow puts the SHA-256 checksums into the GitHub release notes.

## 3.8.0
- Repair data: `cfcopy protect FOLDER [--redundancy 5]` writes Reed-Solomon repair files into `FOLDER\.cf_parity`; `cfcopy repair FOLDER [--check]` finds damaged files and rebuilds them in place. `--parity N` on copy/move/sync does it after a clean job.
- Damaged stretches are spread over many groups, so a bad patch of disk (a few MB) is repairable at 5 percent. Too much damage is reported, never hidden. Repair data protects itself with a checksum.
- Sync and prune never delete `.cf_parity`.

## 3.7.0
- Settings: new controls for keep-old-versions, resume size and a run-after-job script (the GUI now runs it too, result in `CF_EXITCODE`), plus a keep-window-on-top option.
- Settings window is now two columns (1080 x 560) so it fits 768-px screens.
- Window title shows the live speed and percentage while a job runs.

## 3.6.0
- BLAKE3 verify algorithm (Settings segment / `--verify-algo blake3`); own managed implementation, checked against the reference on 18 input sizes.
- `--versions`: old copies of replaced files and sync-deleted files are kept in `DEST\.cf_versions\date_time\` (copy and sync; the folder is never touched by sync).
- `--on-finish SCRIPT`: run a .ps1 / .cmd / .bat / .exe when a CLI job ends (result in `CF_EXITCODE`).
- Fix: sync no longer deletes a resumable `.cfpart` file that the source will recreate.

## 3.5.0
- Resume: an interrupted copy of a big file (256 MB or more, `--resume-mb N`, `--no-resume`) keeps its `.cfpart` file; the next run compares it block by block with the source and only rewrites what differs. Also works after a retry inside the same job.
- `cfcopy schedule add|list|remove`: run a saved job from Windows Task Scheduler (as you, while you are logged on; no password stored; no delete/wipe/shutdown jobs).
- Windows notification when a job finishes while CopyForge is minimised or in the background.

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
