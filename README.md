# Sharp.OpenIddict.Stores

Relational database stores for OpenIddict, planned for SQL Server, PostgreSQL, MySQL/MariaDB, SQLite, Oracle and DM (Dameng). The implementation baseline is OpenIddict 7.7.2 (stable); the libraries multi-target `netstandard2.0`, `netstandard2.1`, `net8.0`, `net9.0` and `net10.0`. No preview packages are referenced; the 8.x line will be evaluated separately once an upstream stable release exists.

The [project design document](docs/design.md) is the authority for development, testing and acceptance. It covers compatible versions, package layout, public API, database model, querying, transactions, migrations, tests and samples; section 10 defines the source layout, versioning and upgrade chain, packaging and ongoing maintenance rules for the per-database SQL scripts.

The [development and progress document](docs/development.md) breaks the design into checkable implementation tasks and records phase progress, completion criteria, the per-database acceptance matrix, and commit and verification evidence; MIG-03/04 and section 5.1 track delivery, upgrade and maintenance acceptance for each database's SQL scripts. Each completed item must update its task status and completion record.

Current state: the solution layout, central package management and five-target multi-targeting are in place, and the project restores, builds, packs eight packages and passes 364 tests (85 unit + 279 integration, 16 skipped) with no database server installed. The core package provides the builder, options validation, connection factory, default entity registration, JSON/UTC converters, the four stores (Application, Scope, Authorization, Token) and the `IQueryable` callback translator.

All six dialects and their versioned install scripts are delivered: SQLite, SQL Server, PostgreSQL, MySQL/MariaDB, Oracle and Dameng (native mode only). Only **SQLite** has real-database acceptance evidence, because it needs no server — it is the existing NuGet driver plus a temporary file. SQL Server, PostgreSQL, MySQL, MariaDB, Oracle and DM are implemented and statically reviewed but **not verified against a live server**, so they are not yet within the supported range; the per-database status in the acceptance matrix records exactly what was and was not executed. Protocol-level integration with a live OpenIddict server is not implemented yet, so this is not a released package.

## Verifying locally

```pwsh
pwsh ./scripts/verify.ps1
```

Runs restore, a `--no-incremental` Release build for all target frameworks, the unit and integration tests (SQLite runs for real; the server-backed suites skip and say so), packing, and the package-content checks. Every step propagates its exit code, so a failure cannot be reported as a pass. Add `-SkipPack` to skip the packaging steps.

## Historical note

The project was previously named `Sharp.OpenIddict.Dapper.*`. The name changed because the delivered artifact is the set of OpenIddict store implementations, not a Dapper extension: Dapper's API appears in a single type (8 call sites) and is confined there by a test (design 9.3).
