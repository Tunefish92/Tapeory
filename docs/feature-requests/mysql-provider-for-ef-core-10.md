# Feature request: a MySQL provider for EF Core 10 (replacing Pomelo)

**Status:** planned, not started · **Raised:** 2026-09-30, during a dependency check ·
**Target:** before EF Core 9's end of support (10 November 2026), e.g. 0.6.0

## Why

Tapeory reaches MySQL and MariaDB through EF Core with the provider
`Pomelo.EntityFrameworkCore.MySql`. Pomelo's last release is 9.0.0, and its repository has had no
activity since August 2025. Pomelo 9 only works with EF Core 9, so Tapeory can't move to EF Core
10:

| Package | Used | Latest | Held back by |
|---|---|---|---|
| Microsoft.EntityFrameworkCore.Sqlite / .Design | 9.0.20 | 10.0.12 | Pomelo 9 |
| SQLitePCLRaw.bundle_e_sqlite3 | 2.1.13 | 3.0.5 | EF Core 9 |

EF Core 9 still gets security fixes until 10 November 2026, together with .NET 9. After that,
Tapeory's database layer would run on an unsupported version.

## What depends on the provider

- `Data/DatabaseContexts.cs` and `Setup/DatabaseSetupService.cs`: `UseMySql(…)` with a fixed
  `MySqlServerVersion(8.0.0)`, which is used for MariaDB too.
- `Data/Migrations/` (MySQL; SQLite has its own `SqliteMigrations/`): 8 applied migrations with
  Pomelo's annotations (`MySql:CharSet`, `MySql:ValueGenerationStrategy`) and column types
  (`datetime(6)`, `tinyint(1)`, `longtext`, …). **Applied migrations must never change.**
- The MySqlConnector driver, used directly:
  - `Backups/DatabaseBackupService.cs`: backups and restores through MySqlBackup.NET's
    MySqlConnector edition;
  - `Setup/DatabaseSetupService.cs` and `Setup/DatabaseConnectionSettings.cs`: connection test and
    connection string;
  - `Auth/UserDirectory.cs`: `MySqlException` when the users table is missing.
- Tests: `TapeoryWebApplicationFactory` runs the integration tests against `mysql:8.4` with
  Testcontainers. **MariaDB isn't tested yet**, although the README and the Unraid setup
  (Unraid's `mariadb` template) promise it.

## Options

### A. Microting's fork of Pomelo (recommended to try first)

`Microting.EntityFrameworkCore.MySql` 10.0.12, from
[microting/Pomelo.EntityFrameworkCore.MySql](https://github.com/microting/Pomelo.EntityFrameworkCore.MySql).
It's an active fork of Pomelo (updated 2026-09-30) that tracks EF Core 10 and already has a preview
for EF Core 11.

- Keeps MySqlConnector, so backups, the connection test and the error handling stay as they are.
- Probably a drop-in replacement with the same API and migration annotations. **To verify:**
  namespaces, whether generating a migration shows no changes, and MariaDB support.
- Risk: it's maintained by one company, with a much smaller community than Pomelo had (about 1
  million downloads, against Pomelo's 139 million).

### B. Oracle's provider

`MySql.EntityFrameworkCore` 10.0.9, Oracle's official provider.

- Built on Oracle's MySql.Data driver instead of MySqlConnector. That means switching backups to
  MySqlBackup.NET's MySql.Data edition, and rewriting the connection test, the connection string
  and the error handling.
- **Supports MySQL only; MariaDB isn't supported by Oracle.** That breaks the Unraid setup unless
  MariaDB is dropped or tested extensively.
- Different annotations and type mappings: it would generate a migration that changes existing
  columns, and each change would need checking against production data.

### C. Stay on Pomelo and EF Core 9

This is only acceptable for a short time after November 2026, if Pomelo releases a version for EF
Core 10 soon.

Meanwhile, the MySqlConnector driver can be updated on its own: Pomelo pulls in 2.5.0, and the
latest is 2.6.2. Adding a direct package reference does that.

## Plan (for option A; B would add the driver switch)

1. **Spike on a branch:**
   - swap the package;
   - move EF Core Sqlite and Design to 10.0.x, and SQLitePCLRaw to 3.x if EF Core 10 allows it;
   - build.
2. **Migration check:**
   - `dotnet ef migrations add ProviderCheck` for MySQL and for SQLite: both must come out
     empty, then delete them;
   - if the MySQL one isn't empty, review every change before going further.
3. **Fresh databases:** all migrations on empty MySQL 8.4, MySQL 8.0 and MariaDB (current LTS),
   and on SQLite.
4. **Upgrade test with real data:**
   - make a backup of the production database from Tapeory's Settings page (a dump, read only);
   - restore it into a test container;
   - start the new version against the container. It must not apply any new migration, and
     templates, printers, users and history must all be there.
   - Never point a local run at the production database (see `local-storage/config/database.json`).
5. **Backups:**
   - a backup made by the new version restores into the new version;
   - a backup made by 0.5.0 restores into the new version.
6. **Tests:**
   - add a MariaDB container to the integration tests (at least the database, backup and user
     tests), so MariaDB is covered from now on;
   - run the full suite (currently 564 tests) on MySQL, MariaDB and SQLite, plus the desktop
     engine's real-life test (`Tapeory.Desktop/tests/engine_realtest.py`).
7. **Docs:** CHANGELOG, and the tech stack section of the README.

## Decisions (to confirm when starting)

- Option A unless the migration check or MariaDB tests fail; then decide between B (and possibly
  dropping MariaDB) and C.
- Pin the fixed server version (`MySqlServerVersion(8.0.0)`) as it is, unless the new provider
  needs `MariaDbServerVersion` for MariaDB. That would mean detecting the server type at
  connection time.

## Open questions

- Has Pomelo released anything for EF Core 10 in the meantime? Check before starting; if so,
  staying on Pomelo is the least work.
- Does Microting's fork keep Pomelo's namespaces and annotation names exactly?
