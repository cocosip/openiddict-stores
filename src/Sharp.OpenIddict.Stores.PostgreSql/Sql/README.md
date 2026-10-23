# PostgreSQL deployment script (Series 1 / v0001)

The shipped file is `1/install/v0001.sql.template`. It is a **template, not a runnable script**: the table
prefix and the schema are configurable, so the copy that reaches the database must be rendered first with the
same values the application configures through `SetTablePrefix`/`SetSchema`. Executing the template as-is
fails, because `__PREFIX__` is not a table name.

## Rendering the deployment copy

```csharp
var template = File.ReadAllText("sql/1/install/v0001.sql.template");
var sql = OpenIddictStoreIdentifiers.RenderDeploymentScript(
    template, OpenIddictStorePostgreSqlDialect.Instance, prefix: "Oidc", schema: "auth");
```

`__PREFIX__` becomes the validated prefix; `__SCHEMA__` becomes the quoted schema plus its trailing dot, or
nothing when the default schema is used. Both are validated with the runtime rules, so an invalid prefix or
schema throws before a copy is produced. The schema must already exist; the script does not create it.

## Target environment

| Item | Requirement |
| --- | --- |
| Server | PostgreSQL 14 or later |
| Driver | Any ADO.NET PostgreSQL driver; tests and samples are intended to use `Npgsql` |
| Database collation | Any. Every column the stores compare exactly is declared `COLLATE "C"`, which is byte-wise and available in every database, so the database default cannot change the result |
| Isolation | Install while no application instance is writing to the target schema |

## Execution order

1. Back up the target database.
2. Stop all application instances that write to the schema.
3. Create the dedicated schema if one is configured (the script does not create it).
4. Render the deployment copy from `1/install/v0001.sql.template` (see above) with the configured prefix and schema.
5. Execute the rendered copy. It contains an explicit `BEGIN`/`COMMIT` pair and no `$$` blocks, so it can run through `psql -f`, a migration runner, or an ADO.NET command loop that splits on `;`.
6. Verify that `{prefix}SchemaVersions` contains exactly one row with `Series = '1'` and `Version = 1`.

## Transactions and failure recovery

PostgreSQL DDL is transactional, so the script opens one transaction and commits at the end; a failing
statement rolls the whole installation back and leaves no partial structures. There is no `IF NOT EXISTS`
skip: a second run fails on the first duplicate relation and the transaction rolls back, which is how an
accidental re-run on a populated schema is refused.

## Known limitations

- **Identifiers are limited to 63 bytes.** PostgreSQL truncates longer names silently, which would create
  tables the stores can never find again. The dialect therefore reports `MaximumIdentifierLength = 63` and
  `QuoteIdentifier` rejects anything longer, and the renderer rejects a table prefix whose rendered table or
  index names would not fit. With `ApplicationPostLogoutRedirectUris` as the longest suffix (33 bytes), the
  longest usable prefix is 30 characters.
- **Names are always quoted.** PostgreSQL folds unquoted identifiers to lower case, while the runtime quotes
  every table and column name, so unquoted DDL would create `openiddictapplications` and the stores would fail
  to find it. Table names, column names, and constraint names are all double-quoted in the template.
- **`timestamp without time zone` and UTC.** Npgsql 6 and later reject a `DateTime` with
  `Kind = Utc` for this type and require `Kind = Unspecified`; values read back are `Unspecified` as well. The
  dialect drops the annotation on write (the value is already normalized to UTC) and restores
  `DateTimeKind.Utc` on read.
- **No filtered unique index.** Unlike SQL Server, PostgreSQL treats NULLs as distinct in a unique index, so
  many NULL `ClientId`/`Name`/`ReferenceId` values are allowed without a `WHERE ... IS NOT NULL` clause.
- **Digest columns are `bytea` with a `CHECK (octet_length(...) = 32)`.** PostgreSQL has no fixed-width binary
  type, so the SHA-256 width that `binary(32)`/`varbinary(32)`/`raw(32)` enforce on the other providers is
  enforced here by a check constraint. Without it a manually inserted row of any other length would be
  permanently unreachable: the candidate lookup always compares against a 32-byte hash, so the auxiliary index
  and the authoritative JSON could disagree with no error anywhere.

## Verification status

The template, rendering rules, paging, date binding, and identifier limits are covered by unit tests
(`PostgreSqlDialectTests`) that do not need a server.

**Accepted on a user-provided server (2026-10-20, see `docs/development.md`, E-044):** the full 15-fact
acceptance suite (`tests/Sharp.OpenIddict.Stores.ServerAcceptanceTests/PostgreSqlStoreContractTests`) passed
on PostgreSQL (server version verified 14+ by the suite itself, on net8.0/net9.0/net10.0 hosts): install
script, re-run refusal, Unicode and 50 000-character values, microsecond `timestamp(6)` round-trips,
`COLLATE "C"` exact comparison including case, accents, and trailing spaces, the unique keys treating NULLs
as distinct, CAS updates, `LIMIT`/`OFFSET` paging including an offset without a count, associated deletes,
bulk revocation with a NULL status, and prune. Per the E-028 rule this evidence comes from a server the user
provided and must be re-earned on every schema series bump; it is never carried over.
