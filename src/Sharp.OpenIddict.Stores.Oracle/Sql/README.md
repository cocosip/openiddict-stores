# Oracle deployment script (Series 1 / v0001)

The shipped file is `7/install/v0001.sql.template`. It is a **template, not a runnable script**: the table
prefix and the schema are configurable, so the copy that reaches the database must be rendered first with the
same values the application configures through `SetTablePrefix`/`SetSchema`. Executing the template as-is fails,
because `__PREFIX__` is not a table name.

## Rendering the deployment copy

```csharp
var template = File.ReadAllText("sql/7/install/v0001.sql.template");
var sql = OpenIddictStoreIdentifiers.RenderDeploymentScript(
    template, OpenIddictStoreOracleDialect.Instance, prefix: "Oidc", schema: "OIDC");
```

`__PREFIX__` becomes the validated prefix; `__SCHEMA__` becomes the quoted schema plus its trailing dot, or
nothing when the default schema is used. Both are validated with the runtime rules, so an invalid prefix or
schema throws before a copy is produced. The schema must already exist and the connecting user needs
`CREATE TABLE`/`CREATE INDEX` plus quota on it.

## Target environment

| Item | Requirement |
| --- | --- |
| Server | Oracle Database 19c or later |
| Driver | `Oracle.ManagedDataAccess.Core`; it has `net8.0`/`netstandard2.1` assets, so a **netstandard2.0 host cannot use it** even though the extension builds for that TFM (design 5.1) |
| Character set | `AL32UTF8`, with `NVARCHAR2`/`NCLOB` for Unicode text |
| Schema | A dedicated schema; the script does not create it |
| Isolation | Install while no application instance is writing to the schema |

## Execution order

1. Back up the target schema.
2. Stop all application instances that write to it.
3. Create the schema and grant quota if it does not exist (the script does not create it).
4. Render the deployment copy from `7/install/v0001.sql.template` with the configured prefix and schema.
5. Execute the rendered copy. There is **no `/` terminator and no `GO`**: every statement ends with a
   semicolon, so SQL*Plus/SQLcl with `SET SQLBLANKLINES ON`, a migration tool, or an ADO.NET loop that splits
   on `;` all work. Do not paste it into SQL*Plus expecting `/` separators; those are client conventions, not
   part of the script (design 10.3).
6. Verify that `{prefix}SchemaVersions` contains exactly one row with `Series = '1'` and `Version = 1`.

## Transactions and failure recovery

Oracle commits DDL implicitly, so the script has **no surrounding transaction** and cannot roll back as a
whole: a failure part-way leaves the objects already created in place. This is stated rather than worked
around, because promising atomicity would be false (design 17).

- Diagnose with `USER_OBJECTS`/`USER_TABLES` filtered by the prefix, plus `{prefix}SchemaVersions`.
- Either finish the remaining statements, or drop the partial objects and start again from a clean schema.
- Re-running the script unchanged is refused by the first duplicate object, which is deliberate: silently
  skipping existing objects would hide a half-installed schema (design 10.3).

## Known limitations

- **Empty string is NULL.** Oracle stores `''` as NULL in `VARCHAR2`/`NVARCHAR2`. The stores are unaffected
  because every optional filter is skipped when the caller passes null or an empty string (design 16.1), so no
  predicate ever compares against an empty-string parameter. Empty strings *inside* JSON are unaffected too,
  since they live in the `NCLOB` text rather than in a `VARCHAR2` column. A caller that stores `""` in a
  bounded text column will read back `NULL`; this is an Oracle behaviour, not something the extension trims.
- **Exact comparison uses `NLSSORT(..., 'NLS_SORT=BINARY')` on both sides.** `VARCHAR2` comparison is
  non-padded, so trailing spaces already matter, but case and accent significance follow `NLS_COMP`/
  `NLS_SORT`. The `NLSSORT` wrapper pins the comparison to a byte-wise sort regardless of session settings
  (design 16.1.1). It is a function call, so an index on the column is not used for those predicates; the
  stores' lookups are key-based and the tables are small, but a very large deployment should be measured.
- **Identifiers are limited to 128 bytes.** `QuoteIdentifier` rejects longer names, and the renderer applies the
  same limit to every name the template produces, so a long prefix fails before DDL runs rather than producing
  a truncated name. The historical 30-byte limit does not apply: it belonged to `COMPATIBLE < 12.2`, below the
  declared 19c floor.
- **`NCLOB` is not comparable or sortable.** The design already excludes `LargeText`/JSON from comparison,
  ordering, and `IN` (design 9.2), which keeps `NCLOB` out of predicates rather than truncating it.
- **Digest columns are `RAW(32)`.** The database enforces the width, so a non-SHA-256 value cannot be stored.
- **Paging uses `OFFSET ... FETCH NEXT`**, available from 12c, so it is within the declared floor.

## Verification status

The template, rendering rules, paging, `NLSSORT` predicates, date binding, and identifier limits are covered by
unit tests that do not need a server. **Real-database installation has not been executed in this repository**
(see `docs/development.md`, E-028): the acceptance suite, including the named-binding, `NCLOB`, `RAW(32)`,
empty-string/NULL, and cancellation checks in BASE-03, is expected to run against a user-provided Oracle 19c
instance before this provider is declared supported.
