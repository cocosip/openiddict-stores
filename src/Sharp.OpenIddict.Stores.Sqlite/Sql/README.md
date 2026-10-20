# SQLite deployment script (Series 1 / v0001)

The shipped file is `7/install/v0001.sql.template`. It is a **template, not a runnable script**: the table
prefix is configurable, so the copy that reaches the database must be rendered first with the same prefix the
application configures through `SetTablePrefix`. Executing the template as-is fails, because `__PREFIX__` is
not a table name.

## Rendering the deployment copy

Use the library entry point so the copy matches the runtime configuration exactly:

```csharp
var template = File.ReadAllText("sql/7/install/v0001.sql.template");
var sql = OpenIddictStoreIdentifiers.RenderDeploymentScript(
    template, OpenIddictStoreSqliteDialect.Instance, prefix: "Oidc");
```

The dialect is only needed to quote the schema placeholder; SQLite has none, but passing its dialect keeps
one entry point for every provider. `__PREFIX__` is replaced by the validated prefix (letters, digits,
underscores; must not start with a digit). An invalid prefix throws before any copy is produced. SQLite has
no schemas, so `SetSchema` is rejected by the options validator and the schema placeholder is absent from this
template.

## Target environment

| Item | Requirement |
| --- | --- |
| Server | SQLite 3.35.0 or later (raise the minimum if you rely on window-function or upsert capabilities beyond `ALTER TABLE ... DROP COLUMN`) |
| Driver | Any ADO.NET SQLite driver; tests and samples use `Microsoft.Data.Sqlite` |
| Encoding | UTF-8; do not enable connection-string modes that rewrite trailing spaces |
| Foreign keys | `PRAGMA foreign_keys = ON` must be executed on every connection, otherwise restrict-on-delete foreign keys have no effect |

## Execution order

1. Back up the target database file (SQLite is a single file, so a plain copy is enough).
2. Stop all writing processes; SQLite does not support rolling upgrades with multiple writers.
3. Render the deployment copy from `7/install/v0001.sql.template` (see above) with the configured prefix.
4. Execute the rendered copy with a SQLite client in a single transaction; the script itself contains `BEGIN TRANSACTION` / `COMMIT`.
5. Verify that `{prefix}SchemaVersions` contains exactly one row with `Series='1'` and `Version=1`.

## Transactions and failure recovery

SQLite DDL participates in transactions, so any failing statement rolls back the whole installation and leaves no partial structures. After a failure, fix the cause and rerun from an empty database; there is no need to clean up half-built tables. If some objects already exist, the script fails on duplicate object names — in that case use the error message to confirm whether they are leftovers, and do not skip errors manually and continue.

## Known limitations

- The parenthesized length in `TEXT(n)` is a type-name decoration in SQLite and does **not** enforce a length constraint. Length and uniqueness semantics are carried by the Store and the unique indexes; values exceeding the design length are not rejected by the database.
- Unique indexes compare using SQLite's `BINARY` collation, which distinguishes case and trailing spaces; this matches the exact comparison of design 16.1.1.
- The auxiliary table digest columns are `BLOB` and must be 32 bytes (SHA-256). The database does not validate the length; the Store guarantees it.
- Dates are stored as fixed-format UTC `TEXT` (`yyyy-MM-dd HH:mm:ss.ffffff`); sorting and comparison rely on that fixed-width format.
