# Orbit DataBridge 0.1.0

Orbit DataBridge is the first public preview of Orbit's safe data operations workspace.

## Included

- Compare PostgreSQL, MySQL/MariaDB, SQL Server, and SQLite structures.
- Inspect bounded row differences using reliable keys.
- Preview and execute insert-only transfers with explicit confirmation.
- Preserve destination rows: no delete, truncate, update, drop, alter, or upsert statements are generated.
- Receive signed installer and update packages through the Orbit DataBridge release channel.

## Preview limitations

- Tables and columns are matched automatically by name.
- The destination schema must already exist and accept the selected rows.
- Manual mapping for renamed tables and columns is not included yet.
- This release is Windows x64 only.
