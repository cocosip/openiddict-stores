# Sharp.OpenIddict.Stores

Relational database stores for OpenIddict, planned for SQL Server, PostgreSQL, MySQL/MariaDB, SQLite, Oracle and DM (Dameng). The implementation baseline is OpenIddict 7.7.2 (stable); the libraries multi-target `netstandard2.0`, `netstandard2.1`, `net8.0`, `net9.0` and `net10.0`. No preview packages are referenced; the 8.x line will be evaluated separately once an upstream stable release exists.

The [project design document](docs/design.md) is the authority for development, testing and acceptance. It covers compatible versions, package layout, public API, database model, querying, transactions, migrations, tests and samples; section 10 defines the source layout, versioning and upgrade chain, packaging and ongoing maintenance rules for the per-database SQL scripts.

The [development and progress document](docs/development.md) breaks the design into checkable implementation tasks and records phase progress, completion criteria, the per-database acceptance matrix, and commit and verification evidence; MIG-03/04 and section 5.1 track delivery, upgrade and maintenance acceptance for each database's SQL scripts. Each completed item must update its task status and completion record.

Current state: the solution layout, central package management and five-target multi-targeting are in place, and the project restores, builds, packs and passes 254 tests (43 unit + 211 integration). The core package provides the builder, options validation, connection factory, default entity registration, JSON/UTC converters, the four stores (Application, Scope, Authorization, Token) and the `IQueryable` callback translator. SQLite is the only provider with an executable `v0001` install script and full real-database acceptance; the remaining five dialects and the protocol-level integration with a live OpenIddict server are not implemented yet, so this is not a released package.

The project was previously named `Sharp.OpenIddict.Dapper.*`. The name changed because the delivered artifact is the set of OpenIddict store implementations, not a Dapper extension: Dapper's API appears in a single type (8 call sites) and is confined there by a test (design 9.3).
