# Security Policy

CopyForge copies files and can send them to another PC over the network, so the network
path, the shared-secret handling and path handling in received files are the security surface.

## Reporting a vulnerability

Please report problems privately, not in a public issue: use the **Security** tab →
**Report a vulnerability** on this repository.

Include the smallest steps or input that reproduce the problem. You can expect an
acknowledgement within a few days; fixes are released as patch versions.

## Verifying a download

Each release lists SHA-256 checksums in its notes and in `SHA256SUMS.txt`. Check a download with:

    Get-FileHash CopyForge.exe -Algorithm SHA256
