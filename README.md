# Sharp.OpenIddict.Stores

[![CI/CD](https://github.com/cocosip/openiddict-stores/actions/workflows/ci-cd.yml/badge.svg)](https://github.com/cocosip/openiddict-stores/actions/workflows/ci-cd.yml)

Relational database stores for OpenIddict, for SQL Server, PostgreSQL, MySQL/MariaDB, SQLite, Oracle and DM (Dameng). The packages are versioned independently of upstream (starting at 1.0.0) and currently lock OpenIddict 7.7.2 (stable; dependency range `[7.7.2,8.0.0-0)`). They multi-target `netstandard2.0`, `netstandard2.1`, `net8.0`, `net9.0` and `net10.0`; no preview packages are referenced. Support for an OpenIddict 8.x line will be evaluated separately once an upstream stable release exists.

The [project design document](docs/design.md) is the authority for development, testing and acceptance. It covers compatible versions, package layout, public API, database model, querying, transactions, migrations, tests and samples; section 10 defines the source layout, versioning and upgrade chain, packaging and ongoing maintenance rules for the per-database SQL scripts.

The [development and progress document](docs/development.md) breaks the design into checkable implementation tasks and records phase progress, completion criteria, the per-database acceptance matrix, and commit and verification evidence; MIG-03/04 and section 5.1 track delivery, upgrade and maintenance acceptance for each database's SQL scripts. Each completed item must update its task status and completion record.

Current state: the solution layout, central package management and five-target multi-targeting are in place, and the project restores, builds, packs eight packages and passes 458 tests (126 unit + 332 integration) with no database server installed. The core package provides the builder, options validation, connection factory, default entity registration, JSON/UTC converters, the four stores (Application, Scope, Authorization, Token) and the `IQueryable` callback translator.

All six dialects and their versioned install scripts are delivered: SQLite, SQL Server, PostgreSQL, MySQL/MariaDB, Oracle and Dameng — the DM8 provider declares its three compatibility modes separately (native, Oracle-compatible, MySQL-compatible), each with its own dialect, script and acceptance obligation; the SQL92/TDS/DM6/Teradata modes are undeclared. Only **SQLite** has real-database acceptance evidence that runs anywhere, because it needs no server — it is the existing NuGet driver plus a temporary file. SQL Server, PostgreSQL, MySQL, MariaDB, Oracle and DM are implemented and statically reviewed; PostgreSQL, Oracle and DM (native and Oracle-compatible) also have recorded runs against user-provided servers, while SQL Server, MySQL/MariaDB and DM's MySQL-compatible mode are **not yet verified against a live server**, so they are not within the supported range. The per-database status in the acceptance matrix records exactly what was and was not executed. Protocol-level integration with a live OpenIddict server is not implemented yet, so this is not a released package.

## Real-database acceptance

One harness covers every supported database, and one configuration file switches the target:

```bash
cp tests/Sharp.OpenIddict.Stores.ServerAcceptanceTests/databases.json.template \
   tests/Sharp.OpenIddict.Stores.ServerAcceptanceTests/databases.json
dotnet test tests/Sharp.OpenIddict.Stores.ServerAcceptanceTests -c Release -f net8.0
```

`databases.json` selects the provider (`all`, or one of `Sqlite`/`SqlServer`/`PostgreSql`/`MySql`/`MariaDb`/`Oracle`/`DmNative`/`DmOracle`/`DmMySql`) and carries the connection strings; it is ignored by git, and the committed template shows its shape. SQLite needs nothing and always runs, so a machine with no database server still produces real evidence. A configured but unreachable server fails; an unconfigured one is reported as skipped with the exact key that is missing. The project is not in the solution and CI never touches it. See
[tests/Sharp.OpenIddict.Stores.ServerAcceptanceTests/README.md](tests/Sharp.OpenIddict.Stores.ServerAcceptanceTests/README.md).

## Generating the deployment SQL

The shipped scripts are templates (configurable table prefix and schema), so a deployment renders a copy
first. `tools/Sharp.OpenIddict.Stores.SqlTool` does that from the same embedded assets the packages ship:

```bash
dotnet run --project tools/Sharp.OpenIddict.Stores.SqlTool -- --list
dotnet run --project tools/Sharp.OpenIddict.Stores.SqlTool -- --provider Dm --mode oracle-compatible     --prefix Oidc --schema OIDC --out dm-install.sql
```

The rendered copy contains the tables, keys, indexes and the `SchemaVersions` seed record; the prefix and
schema passed here must equal what the application configures through `SetTablePrefix`/`SetSchema`. See
[tools/README.md](tools/README.md).

## Verifying locally

```pwsh
pwsh ./scripts/verify.ps1
```

Runs restore, a `--no-incremental` Release build for all target frameworks, the unit and integration tests (SQLite runs for real; nothing here needs a server), packing, and the package-content checks. Every step propagates its exit code, so a failure cannot be reported as a pass. Add `-SkipPack` to skip the packaging steps. The real-database acceptance harness is deliberately outside this gate and outside the solution.

GitHub Actions (`.github/workflows/ci-cd.yml`) runs the same gate on branch pushes and pull requests — build and test only. A version tag (`v*.*.*`) additionally packs the eight packages, publishes them to NuGet and creates a GitHub release; the publish job needs a `NUGET_API_KEY` secret.

## Historical note

The project was previously named `Sharp.OpenIddict.Dapper.*`. The name changed because the delivered artifact is the set of OpenIddict store implementations, not a Dapper extension. Dapper was later removed as a dependency entirely: the execution layer is hand-written ADO.NET, and tests pin that no Dapper reference can come back (design 9.3).
