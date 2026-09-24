# Orbit · DataBridge

Orbit DataBridge is a Windows desktop workspace for understanding two relational systems and moving only rows that are missing from the destination. It is the first product in the reusable Orbit application suite: the outer brand is designed to work for future operational tools, while DataBridge owns the database-specific workflow. It is built with C#/.NET 10, Avalonia UI, MVVM, ADO.NET provider adapters, and local Windows user-scoped credential protection.

The product identity and reusable visual language are documented in [BRANDING.md](BRANDING.md).

## What works

- Connection profiles for PostgreSQL, MySQL/MariaDB, SQL Server, and SQLite.
- Connection testing without logging passwords or full connection strings.
- Read-only structure comparison for tables, columns, nullability, primary/unique/foreign keys, and indexes exposed by each provider.
- Bounded row comparison using shared primary or unique keys, with source-only, destination-only, and different-value samples.
- Automatic exact-name table and column matching with a visible source-to-destination mapping preview.
- Insert-only transfer plans with row counts, conflict messages, bounded batches, destination transactions, progress, cancellation, and local history.
- Destination safety guards for missing tables, required unmapped columns, incompatible type families, missing reliable keys, and unsafe generated/identity key mappings.
- SQLite integration tests proving destination rows are preserved and only missing rows are inserted.

The transfer path generates parameterized `INSERT INTO` commands only. It does not generate or execute `DELETE`, `TRUNCATE`, `UPDATE`, `DROP`, `ALTER`, or upsert statements, and it never creates destination tables automatically.

## Laragon demo workspace

Debug builds, and Release builds started with `ORBIT_DATABRIDGE_DEMO=1`, open with a local Laragon pair prefilled so the first comparison has a concrete starting point:

| Role | Engine | Host | Port | Database | User |
| --- | --- | --- | ---: | --- | --- |
| Source | MySQL/MariaDB | `127.0.0.1` | `3306` | `bms` | `root` |
| Destination | MySQL/MariaDB | `127.0.0.1` | `3306` | `booking` | `root` |

The password is intentionally left blank and is never assumed to be blank: enter the password configured for your local MySQL installation, then use **Test source** and **Test destination** before comparing. The default workflow remains read-only until an explicit insert-only transfer is reviewed and authorized.

## Build and run

The repository pins the .NET SDK in `global.json`.

```powershell
dotnet restore .\Dbms.slnx
dotnet build .\Dbms.slnx -c Debug
dotnet run --project .\src\Dbms.App\Orbit.DataBridge.csproj
```

Run the automated tests with:

```powershell
dotnet test .\tests\Dbms.Core.Tests\Dbms.Core.Tests.csproj -c Release
```

If the SDK is installed in a non-standard location, set `DOTNET_ROOT` for the current PowerShell session before running these commands. Do not commit that machine-specific path.

Normal installed releases start with neutral source and destination profiles. To opt into the Laragon demo defaults while developing:

```powershell
$env:ORBIT_DATABRIDGE_DEMO = '1'
dotnet run --project .\src\Dbms.App\Orbit.DataBridge.csproj
```

The application stores profile metadata and job history under `%LOCALAPPDATA%\DbmsTransfer` for compatibility with the current technical release name. Passwords are not written to the profile JSON. When enabled, they are encrypted with Windows DPAPI for the current Windows user.

## Public Windows release package

The public installer is built with the checked-in Velopack tool manifest and the packaging script:

```powershell
dotnet tool restore
.\scripts\package-win.ps1 -Version 0.1.0
```

The package is written to `artifacts\release` and contains:

- `Orbit.DataBridge-stable-Setup.exe` — the per-user Windows installer.
- `Orbit.DataBridge-stable-Portable.zip` — a portable build for users who do not want an installation.
- `Orbit.DataBridge-0.1.0-stable-full.nupkg` — the full Velopack package used by the release feed.
- `releases.stable.json` and `RELEASES-stable` — update metadata for the stable channel.
- `SHA256SUMS.txt` — SHA-256 checksums for the generated release assets.

The package script is intentionally allowed to create an unsigned local package for development. A public package must be signed:

```powershell
$env:VPK_SIGN_PARAMS = '/fd SHA256 /f "C:\secure\orbit-signing.pfx" /p "<certificate-password>" /tr "http://timestamp.digicert.com" /td SHA256'
.\scripts\package-win.ps1 -Version 0.1.0 -RequireSigning
```

Keep signing material outside the repository. The `-RequireSigning` switch fails closed when `VPK_SIGN_PARAMS` is missing. For the hosted release workflow, the certificate and password are supplied as GitHub Actions secrets rather than stored in this project.

The older `scripts\publish-win.ps1` command remains useful for a fast raw developer executable. It is not an installed Velopack package and does not participate in automatic updates.

## Updates

The installed application uses Velopack to check the public stable feed at:

`https://github.com/dr-rei/orbit-databridge`

After a user installs `Orbit.DataBridge-stable-Setup.exe`, the application checks for a newer stable release and exposes **Check for updates** and **Install update** in the footer. The update feed is public so a normal user can download releases without a GitHub account.

The update check is disabled for raw `dotnet run` and raw publish output. This avoids confusing development builds with installed releases and prevents local builds from unexpectedly replacing themselves.

## GitHub release workflow

The repository includes two workflows:

- `CI` builds the solution, runs the test project, and checks NuGet dependencies on pushes to `main` and on pull requests.
- `Release` runs for a `v*` tag, packages the signed Windows release, and creates a GitHub release with the installer, portable archive, update feed, and checksums.

Before creating the first public release, configure these repository secrets:

- `ORBIT_SIGNING_CERT_BASE64` — base64 contents of the code-signing certificate file.
- `ORBIT_SIGNING_CERT_PASSWORD` — password for that certificate.

Then push a semantic-version tag:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

The workflow deliberately refuses to publish when signing secrets are absent. This keeps an accidental unsigned executable from becoming the public release users receive.

## Private versus public distribution

A GitHub repository can be private while its source and releases remain restricted to authenticated collaborators. That is appropriate for private testing, but anonymous users cannot reliably download a private GitHub release or use this application's unauthenticated GitHub update feed. For this first public product release, the repository and stable release feed are therefore public. A future private distribution can use an authenticated update source or a separate authenticated artifact host without changing the product brand.

## Current boundaries

- Tables and columns are matched automatically by name. The transfer preview exposes the generated mappings; a manual mapping editor is the next UI increment for renamed tables/columns.
- Row comparison is bounded to 10,000 inspected rows and 250 displayed differences. Row counts are still queried separately; a truncated comparison is labelled as such.
- Generated/identity key transfers are blocked unless the provider exposes a safe mapping. This avoids silently creating different destination keys or requiring provider-specific identity-insert modes.
- Foreign-key metadata is compared and selected tables are ordered by their known destination foreign-key dependencies. Dependency cycles are blocked and reported; destination dependencies outside the selected set may still require the user to include or prepare those parent rows first.
- The local Laragon MySQL instance was read-only preflight checked for the demo pair (`bms` and `booking`); no database writes were made during development. SQLite was tested end to end. A real transfer against the Laragon databases still requires the user to review the generated plan and explicitly authorize it.
- The application does not create or alter destination schema. The destination must already be structurally able to accept the selected rows.

## Project layout

| Project | Responsibility |
| --- | --- |
| `src/Dbms.Core` | Models, provider contracts, schema/data comparison, transfer safety and execution rules |
| `src/Dbms.Infrastructure` | PostgreSQL, MySQL/MariaDB, SQL Server and SQLite ADO.NET adapters; secure profiles; history |
| `src/Dbms.App/Orbit.DataBridge.csproj` | Orbit DataBridge Avalonia desktop window, MVVM view model, connection/compare/plan/progress/history screens |
| `tests/Dbms.Core.Tests` | Schema comparison and SQLite insert-only transfer regression tests |
