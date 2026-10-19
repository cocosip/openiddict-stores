# Dameng DM8 deployment script (Series 7 / v0001) — native mode only

The shipped file is `7/install/v0001.sql.template`. It is a **template, not a runnable script**: the table
prefix and the schema are configurable, so the copy that reaches the database must be rendered first with the
same values the application configures through `SetTablePrefix`/`SetSchema`.

## One mode, named explicitly

DM8 is configured with an instance-level compatibility mode (`COMPATIBLE_MODE`), and the mode changes real
behaviour: string comparison, empty-string handling, paging syntax, and some DDL. "DM8" therefore is not one
target, and a dialect cannot silently work for all of them.

**This entry declares native mode only.** `manifest.json` says so in the `server` field, the script header says
so, and a unit test pins the field, so the claim cannot quietly widen:

- Native mode: implemented here, and the only mode this manifest entry covers.
- Oracle-compatible mode: **not claimed**. It needs its own manifest entry, its own script if the syntax
  differs, and its own full acceptance run. An Oracle run does not stand in for it, and neither does a DM8 run
  in native mode (design 5).
- MySQL-compatible mode: not claimed at all.

`OpenIddictStoreDmDialect.CompatibilityMode` records the mode the dialect was written for. It is part of the
implementation's identity, not a runtime switch.

## Rendering the deployment copy

```csharp
var template = File.ReadAllText("sql/7/install/v0001.sql.template");
var sql = OpenIddictStoreIdentifiers.RenderDeploymentScript(
    template, OpenIddictStoreDmDialect.Instance, prefix: "Oidc", schema: "OIDC");
```

`__PREFIX__` becomes the validated prefix; `__SCHEMA__` becomes the quoted schema plus its trailing dot, or
nothing when the default schema is used. The schema must already exist and the connecting user needs
`CREATE TABLE`/`CREATE INDEX` permission on it.

## Target environment

| Item | Requirement |
| --- | --- |
| Server | DM8 |
| Compatibility mode | Native (`COMPATIBLE_MODE`); other modes are outside this declaration |
| Driver | `DM.DmProvider`; it has **no `net10` asset**, so real connection acceptance must run on a net8.0/net9.0 host (design 5.1). Compiling for net10 is not evidence of a working connection |
| Schema | A dedicated schema; the script does not create it |
| Isolation | Install while no application instance is writing to the schema |

## Execution order

1. Back up the target schema.
2. Stop all application instances that write to it.
3. Confirm the instance is in native mode before anything else; a different mode invalidates this entry.
4. Create the schema if needed.
5. Render the deployment copy with the configured prefix and schema.
6. Execute the rendered copy. No `/`, no `GO`, no PL/SQL block: every statement ends with a semicolon, so
   DISQL, a migration manager, or an ADO.NET loop that splits on `;` all work.
7. Verify that `{prefix}SchemaVersions` contains exactly one row with `Series = '7'` and `Version = 1`.

## Transactions and failure recovery

DM8 commits DDL implicitly, so the script has **no surrounding transaction** and cannot roll back as a whole.
A failure part-way leaves the objects already created in place.

- Diagnose with the data dictionary filtered by the prefix, plus `{prefix}SchemaVersions`.
- Either finish the remaining statements, or drop the partial objects and start from a clean schema.
- Re-running unchanged is refused by the first duplicate object, deliberately: skipping existing objects would
  hide a half-installed schema (design 10.3).

## Known limitations and open blockers

Two behaviours are mode- and instance-dependent, and both are **release blockers** rather than satisfied
requirements. They are listed here instead of being guessed at, because a wrong guess fails silently:

- **Exact string comparison (design 16.1.1).** Whether `VARCHAR` comparison in native mode distinguishes case,
  accents, and trailing spaces depends on the instance character set and case-sensitivity configuration. The
  dialect currently emits a plain `column = :p`. Before release, a real server must prove that `client`,
  `Client`, `client `, and an accented variant are mutually distinct in `FindByClientId`, `FindByName`,
  `FindByReferenceId`, `FindBySubject`, the CAS predicates, and the bulk revoke filters. If native mode turns
  out to be case-insensitive, the fix belongs in the dialect predicate, not in the stores.
- **Paging clause.** `LIMIT ... OFFSET` is used (with `int.MaxValue` as the "no count" ceiling). DM8 documents
  several paging forms and their availability varies by mode; a real server must confirm this one is accepted
  in native mode, including `LIMIT` with no `OFFSET`, `OFFSET` without a count, and `LIMIT 0`.

Further limitations:

- **Empty strings.** DM8 inherits Oracle's `''`-is-NULL behaviour in some modes. The design records that DM must
  follow the same treatment if its declared mode behaves that way (design 9.1). This is unverified, and the
  stores are unaffected either way because optional filters are skipped for empty input.
- **Identifiers are limited to 128 bytes.** `QuoteIdentifier` rejects longer names and the renderer applies the
  same limit to every name the template produces.
- **Digest columns are `VARBINARY(32)`.** The width is enforced by the column type.
- **`CLOB` is not comparable or sortable.** The design already excludes `LargeText`/JSON from comparison,
  ordering, and `IN` (design 9.2), which keeps `CLOB` out of predicates rather than truncating it.

## Verification status

The template, rendering rules, paging shape, date binding, and identifier limits are covered by unit tests that
do not need a server. **Nothing here has been executed against a real DM8 instance** (see
`docs/development.md`, E-028), and the two blockers above must be closed before this provider is declared
supported. The driver has no `net10` asset, so that acceptance must run on a net8.0/net9.0 host.
