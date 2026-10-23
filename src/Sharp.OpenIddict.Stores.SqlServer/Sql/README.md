# SQL Server deployment script (Series 1 / v0001)

The shipped file is `1/install/v0001.sql.template`. It is a **template, not a runnable script**: the table
prefix and the schema are configurable, so the copy that reaches the database must be rendered first with the
same values the application configures through `SetTablePrefix`/`SetSchema`. Executing the template as-is
fails, because `__PREFIX__` is not a table name.

## Rendering the deployment copy

```csharp
var template = File.ReadAllText("sql/1/install/v0001.sql.template");
var sql = OpenIddictStoreIdentifiers.RenderDeploymentScript(
    template, OpenIddictStoreSqlServerDialect.Instance, prefix: "Oidc", schema: "auth");
```

`__PREFIX__` becomes the validated prefix; `__SCHEMA__` becomes the quoted schema plus its trailing dot, or
nothing when the default schema is used. Both are validated with the runtime rules, so an invalid prefix or
schema throws before a copy is produced. The schema must already exist; the script does not create it.

## Target environment

| Item | Requirement |
| --- | --- |
| Server | SQL Server 2016 (13.x) or later, including Azure SQL Database and LocalDB 2016+ |
| Driver | Any ADO.NET SQL Server driver; tests and samples use `Microsoft.Data.SqlClient` |
| Database collation | Any. Every exactly-compared column is declared `COLLATE LATIN1_GENERAL_100_BIN2` in the DDL and the Store also forces that collation plus a `DATALENGTH` check in the predicate, so the database default cannot change exact comparisons. The matching column collation keeps those predicates seekable; a hand-created table with a different column collation still compares correctly, only without the index seek |
| Isolation | Install while no application instance is writing to the target schema |

## Execution order

1. Back up the target database.
2. Stop all application instances that write to the schema.
3. Create the dedicated schema if one is configured (the script does not create it).
4. Render the deployment copy from `1/install/v0001.sql.template` (see above) with the configured prefix and schema.
5. Execute the rendered copy. The script is a single batch with no `GO` separator, so `sqlcmd -i`, SSMS, or an ADO.NET command loop can all run it unchanged.
6. Verify that `{prefix}SchemaVersions` contains exactly one row with `Series = N'1'` and `Version = 1`.

## Transactions and failure recovery

The script issues `SET XACT_ABORT ON` before opening one explicit transaction and commits at the end. A failing statement dooms and rolls back the whole installation, leaving no partial structures, because every DDL statement in SQL Server is transactional. `XACT_ABORT` matters: `THROW` alone does not roll back, so without it the rollback would depend on the client dropping the connection, and a client that caught the error and kept the connection open would be left with an open transaction. The first statement throws deliberately when `{prefix}Applications` already exists, so an accidental re-run on a populated schema stops instead of silently skipping objects.

## Known limitations

- `FETCH NEXT 0 ROWS` is a syntax error on SQL Server. Pagination requests with a zero count are rejected by the dialect instead of returning rows; the stores never issue one.
- A single-column unique index on `nvarchar` compares with the column collation, so `ClientId` is created with `Latin1_General_100_BIN2`. Even so, a `nvarchar` unique index ignores trailing spaces under any collation: `client ` collides with `client` at insert time while the Store's length-sensitive lookup can never find `client `. The design documents this as a physical constraint limit, and the store surfaces the driver's unique-key error rather than trimming input.
- `datetime2(6)` stores microseconds. The dialect binds dates as UTC `DateTimeOffset` values because a plain `DateTime` parameter is inferred as `datetime` (3.33 ms) by the driver and would silently truncate the stored value.
- The auxiliary table digest columns are `binary(32)` and must be exactly SHA-256. The database enforces the fixed width.
