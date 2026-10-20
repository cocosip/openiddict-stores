# MySQL / MariaDB deployment script (Series 1 / v0001)

The shipped file is `1/install/v0001.sql.template`. It is a **template, not a runnable script**: the table
prefix is configurable, so the copy that reaches the database must be rendered first with the same value the
application configures through `SetTablePrefix`. Executing the template as-is fails, because `__PREFIX__` is
not a table name.

One physical script serves both servers. `manifest.json` lists MySQL 8.0 and MariaDB 10.6 as separate entries
pointing at the same file, and **the two are accepted separately**: a MySQL run does not stand in for MariaDB
(design 5, 10.1).

## Rendering the deployment copy

```csharp
var template = File.ReadAllText("sql/1/install/v0001.sql.template");
var sql = OpenIddictStoreIdentifiers.RenderDeploymentScript(
    template, OpenIddictStoreMySqlDialect.Instance, prefix: "Oidc");
```

`__PREFIX__` becomes the validated prefix. There is no schema placeholder: MySQL has no dedicated-schema
semantics, and `SetSchema` is rejected by the options validator rather than silently ignored (design 14.2).

## Target environment

| Item | Requirement |
| --- | --- |
| Server | MySQL 8.0+ or MariaDB 10.6+ |
| Engine | InnoDB, `ROW_FORMAT=DYNAMIC` (stated explicitly so the 3072-byte index key limit applies no matter what `innodb_default_row_format` is set to) |
| Charset / collation | `utf8mb4` / `utf8mb4_bin` on every table |
| Driver | Any ADO.NET MySQL driver; tests and samples are intended to use `MySqlConnector` |
| Isolation | Install while no application instance is writing to the target database |

## Execution order

1. Back up the target database.
2. Stop all application instances that write to it.
3. Render the deployment copy from `1/install/v0001.sql.template` with the configured prefix.
4. Execute the rendered copy. It contains no `DELIMITER` block and no stored programs, so it can run through
   the `mysql` client with `--show-warnings --batch`, a migration runner, or an ADO.NET command loop that
   splits on `;`. Stop on the first error; do not continue past it.
5. Verify that `{prefix}SchemaVersions` contains exactly one row with `Series = '1'` and `Version = 1`.

## Transactions, failure recovery, and why this differs from the other providers

MySQL and MariaDB **commit DDL implicitly**: there is no surrounding transaction in this script and it cannot
roll back as a whole. A failure stops the run part-way and the objects already created stay in place. Record this honestly rather than promising atomicity:

- Diagnose by listing the objects that exist under the prefix and reading `{prefix}SchemaVersions`.
- Finish the remaining statements, or drop the partial objects and start again from an empty target.
- Re-running the script unchanged is refused (the first duplicate table fails), which is deliberate: silently
  skipping existing objects would hide a half-installed state (design 10.3).

Oracle and DM behave the same way, and their READMEs say so too. Only SQL Server, PostgreSQL, and SQLite can
promise all-or-nothing installation.

## Known limitations

- **Trailing spaces in unique indexes.** `utf8mb4_bin` is the one binary collation both servers provide, and it
  is PAD SPACE: a `UNIQUE` index on `varchar` therefore treats `client` and `client ` as the same value and
  rejects the second row. Query predicates are still exact, because the dialect adds the `BINARY` operator to
  every string comparison, so a lookup for `client` never returns a row stored as `client `. The index limit is
  reported as a uniqueness conflict and the value is never trimmed to hide it (design 16.1.1). Applications
  that must store both values should use a case- and space-distinct representation at the application level.
- **Case sensitivity.** The tables use `utf8mb4_bin`, not a `_ci` collation, so an accidental query without
  `BINARY` would still be case-sensitive. The dialect adds `BINARY` anyway: it is what makes trailing spaces
  significant, which the collation alone does not.
- **Identifier length is 64 characters.** `QuoteIdentifier` rejects anything longer instead of letting the
  server truncate it, and the renderer applies the same limit to every name the template produces. With
  `ApplicationPostLogoutRedirectUris` (33 characters) as the longest suffix, the longest usable prefix is 31.
- **Index key length.** Under `utf8mb4` every character costs 4 bytes, so `Subject varchar(400)` occupies 1600
  bytes and the four-column `(ApplicationId, Status, Subject, Type)` index 2400 bytes; both fit the 3072-byte
  limit of `ROW_FORMAT=DYNAMIC`. Without that row format the limit drops to 767 bytes and the script would fail,
  which is why the format is stated per table rather than inherited.
- **`datetime(6)` stores microseconds** and has no time zone. The dialect binds the UTC value unchanged and
  restores `DateTimeKind.Utc` on read; it never relies on the server time zone or on `CURRENT_TIMESTAMP`.
- **Digest columns are `binary(32)`.** The database enforces the width, so a value that is not a SHA-256 digest
  cannot be stored.

## Verification status

The template, rendering rules, paging, the `BINARY` predicates, date binding, and identifier limits are covered
by unit tests (`MySqlDialectTests`) that do not need a server. **Real-database installation has not been executed
in this repository** (see `docs/development.md`, E-028): the acceptance suite is expected to be run against
separate MySQL 8.0 and MariaDB 10.6 instances before this provider is declared supported.
