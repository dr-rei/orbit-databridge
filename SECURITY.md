# Security policy

## Reporting a vulnerability

Please do not open a public issue for a security vulnerability, credential-handling problem, or data-loss risk.

Use GitHub's private vulnerability reporting for the Orbit DataBridge repository when it is enabled. If private reporting is not available, contact the maintainer through the dr-rei GitHub profile and include a short description, affected version, reproduction steps, and impact. Do not include real database passwords or private connection strings.

## Scope

Security reports are especially useful for:

- credential disclosure or unsafe local storage;
- unintended database writes outside the documented insert-only transfer path;
- update or installer tampering;
- remote code execution or arbitrary file access;
- sensitive data appearing in logs, diagnostics, or release artifacts.

The application is currently a Windows x64 desktop product. Reports against unsupported operating systems may still be useful, but reproduction on the supported Windows build is preferred.
