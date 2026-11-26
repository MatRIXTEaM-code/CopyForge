# CopyForge

A fast, verifying file copy tool for Windows, in the spirit of FastCopy. Clean-room C# / .NET 10, WinForms GUI (`CopyForge.exe`) plus a command-line tool (`cfcopy.exe`).

## Features
- Copy, move, sync (mirror), delete, watch, compare, scripted job files
- Copy strategies: pipeline, Windows native, ReFS block cloning, delta update for big files, VSS for locked files
- Verify after copy (xxHash3 / xxHash128 / SHA-256) bypassing the Windows cache
- USB-friendly: direct-write of small files, bus speed detection (USB 2.0 warning), safe eject with retry
- SHA-256 manifest (`cfmanifest.json`), optional ECDSA signing, `audit` with key pinning
- LAN `send` / `receive` over TLS plus an AES-256-GCM layer keyed from a shared secret, pinned certificate fingerprint
- `fakecheck`: fake-capacity drive test
- Job history, diagnostics report

## Requirements
Windows 10/11 x64 and the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). Builds are framework-dependent single-file executables.

## Build
```
dotnet build CopyForge.slnx -c Release
dotnet run --project tests/CopyForge.SmokeTest -c Release
dotnet publish src/CopyForge.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None
dotnet publish src/CopyForge.Cli -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None
```

## Command line
Run `cfcopy --help` for the full list of commands and options.

## Licence
Proprietary, all rights reserved. See [LICENSE](LICENSE).
