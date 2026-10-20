# tools/

Repository utilities. These are not part of the eight shipped packages; `dotnet pack` skips them.

## Sharp.OpenIddict.Stores.SqlTool

Renders a provider's install-script template into the deployment copy a database receives: it selects the
template from the same embedded assets the packages ship (digests recorded in each provider's
`Sql/manifest.json`), applies the configured table prefix and schema, and emits complete SQL — tables, keys,
indexes, and the `{prefix}SchemaVersions` seed record the runtime's version guard requires.

The tool never connects to a database. Executing the rendered copy, with the transaction caveats recorded in
the provider's `Sql/README.md`, stays with the deployment.

```bash
# Every provider and manifest entry
dotnet run --project tools/Sharp.OpenIddict.Stores.SqlTool -- --list

# SQL Server with the default prefix, to standard output
dotnet run --project tools/Sharp.OpenIddict.Stores.SqlTool -- --provider SqlServer

# Dameng in the Oracle-compatible mode, custom prefix and schema, to a file
dotnet run --project tools/Sharp.OpenIddict.Stores.SqlTool -- --provider Dm --mode oracle-compatible \
    --prefix Oidc --schema OIDC --out dm-oracle-install.sql
```

Options: `--provider` (SqlServer | PostgreSql | MySql | Sqlite | Oracle | Dm), `--mode` (Dameng only:
`native`, `oracle-compatible`, `mysql-compatible` — must match the target instance's `COMPATIBLE_MODE`),
`--prefix` (default `OpenIddict`), `--schema` (default: provider default schema; SQLite/MySQL reject one),
`--server` (disambiguates providers that ship different scripts per server), `--out` (UTF-8 without BOM).

The prefix and schema passed here must equal what the application configures through `SetTablePrefix`/
`SetSchema`; the runtime's schema version guard refuses to operate against a database installed with a
different prefix.
