# SQL Server deployment script (Series 7 / v0001)

## Target environment

| Item | Requirement |
| --- | --- |
| Server | SQL Server 2016 (13.x) or later, including Azure SQL Database and LocalDB 2016+ |
| Driver | Any ADO.NET SQL Server driver; tests and samples use `Microsoft.Data.SqlClient` |
| Database collation | Any. Exact comparisons are enforced at query time with `COLLATE LATIN1_GENERAL_100_BIN2` plus `DATALENGTH`, so the database default collation cannot change them |
| Isolation | Install while no application instance is writing to the target schema |

## Execution order

1. Back up the target database.
2. Stop all application instances that write to the schema.
3. Generate the deployment copy: replace `__PREFIX__` with the actual prefix (default `OpenIddict`), validated by the same rules as at runtime (letters, digits and underscores, not starting with a digit).
4. Execute `install/v0001.sql`. The script is a single batch with no `GO` separator, so `sqlcmd -i`, SSMS, or an ADO.NET command loop can all run it unchanged.
5. Verify that `{prefix}SchemaVersions` contains exactly one row with `Series = N'7'` and `Version = 1`.

## Transactions and failure recovery

The script opens one explicit transaction and commits at the end. A failing statement rolls the whole installation back, leaving no partial structures, because every DDL statement in SQL Server is transactional. The first statement throws deliberately when `{prefix}Applications` already exists, so an accidental re-run on a populated schema stops instead of silently skipping objects.

## Known limitations

- `FETCH NEXT 0 ROWS` is a syntax error on SQL Server. Pagination requests with a zero count are rejected by the dialect instead of returning rows; the stores never issue one.
- A single-column unique index on `nvarchar` still compares with the column collation, so `ClientId` is created with `Latin1_General_100_BIN2`. Even so, `client` and `client ` are *not* distinguished by a unique index on some collations; the design documents this as a physical constraint limit, and the Store reports the conflict rather than trimming input.
- `datetime2(6)` stores microseconds. The dialect binds dates as UTC `DateTimeOffset` values because a plain `DateTime` parameter is inferred as `datetime` (3.33 ms) by the driver and would silently truncate the stored value.
- The auxiliary table digest columns are `binary(32)` and must be exactly SHA-256. The database enforces the fixed width.
