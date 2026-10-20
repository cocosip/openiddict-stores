# Dameng DM8 deployment scripts (Series 1 / v0001) — one entry per compatibility mode

The shipped files are `7/install/<mode>/v0001.sql.template`, one per declared compatibility mode. Each is a
**template, not a runnable script**: the table prefix and the schema are configurable, so the copy that reaches
the database must be rendered first with the same values the application configures through
`SetTablePrefix`/`SetSchema`.

## Three modes, declared separately

DM8 is configured with an instance-level compatibility mode (`COMPATIBLE_MODE`), and the mode changes real
behaviour: string comparison prerequisites, empty-string handling, and paging syntax. "DM8" therefore is not
one target, and a dialect cannot silently work for all of them. **Each declared mode is its own manifest
entry, its own script file, and its own acceptance run** (design 5, 10.1).

| Entry (`server` field) | `COMPATIBLE_MODE` | Runtime paging | Empty string | Script |
| --- | --- | --- | --- | --- |
| `DM8 (native mode)` | `0` | `LIMIT [count] [OFFSET n]` | unverified; stores unaffected | `7/install/native/v0001.sql.template` |
| `DM8 (Oracle-compatible mode)` | `2` | `OFFSET n ROWS / FETCH NEXT m ROWS ONLY` | stored as NULL | `7/install/oracle-compatible/v0001.sql.template` |
| `DM8 (MySQL-compatible mode)` | `4` | `LIMIT [count] [OFFSET n]` | distinct from NULL | `7/install/mysql-compatible/v0001.sql.template` |

`OpenIddictStoreDmCompatibilityMode` (and the `UseDm(mode)` registration) selects the matching dialect, and
each dialect records its mode in its `CompatibilityMode` constant. A unit test pins the manifest to exactly
these three entries, so the claim cannot quietly widen.

The three scripts share one physical body on purpose: DM8's type system (`nvarchar`, `clob`, `varbinary`,
`timestamp`) is not gated by `COMPATIBLE_MODE`, and a test enforces that the bodies stay identical. If a real
acceptance run proves a mode needs different DDL, that mode's file changes alone — and its manifest digest
with it.

Modes DM8 documents that are **not claimed**: SQL92 (`1`), TDS (`3`), DM6 (`5`), Teradata (`6`). Claiming one
needs its own entry, its own script if the syntax differs, and its own full acceptance run.

## One prerequisite in every mode: CASE_SENSITIVE

Exact comparison emits a plain equality. DM8 compares strings case-sensitively only on an instance
initialized with the default `CASE_SENSITIVE=1`; an instance created with `CASE_SENSITIVE=0` folds case in
comparisons and silently corrupts `FindByClientId`/`FindByName`/`FindByReferenceId`/`FindBySubject` lookups.
No compatibility mode overrides that setting, so **the deployment must run on a `CASE_SENSITIVE=1`
instance** — MySQL-compatible migrations in particular are often initialized with `0`, which is not a
supported configuration of this provider.

## Rendering the deployment copy

```csharp
var template = File.ReadAllText("sql/7/install/native/v0001.sql.template");
var sql = OpenIddictStoreIdentifiers.RenderDeploymentScript(
    template, OpenIddictStoreDmDialect.Instance, prefix: "Oidc", schema: "OIDC");
```

For the other modes, load that mode's template and pass its dialect
(`OpenIddictStoreDmOracleCompatibleDialect.Instance` / `OpenIddictStoreDmMySqlCompatibleDialect.Instance`).
`__PREFIX__` becomes the validated prefix; `__SCHEMA__` becomes the quoted schema plus its trailing dot, or
nothing when the default schema is used. The schema must already exist and the connecting user needs
`CREATE TABLE`/`CREATE INDEX` permission on it. The rendered copy contains tables, keys, indexes, and the
seed record in `{prefix}SchemaVersions` — the one row the runtime's version guard requires before first use.

The repository also ships a command-line generator that renders the correct template for a provider and mode
without writing code; see `tools/` in the repository root.

## Target environment

| Item | Requirement |
| --- | --- |
| Server | DM8 |
| Compatibility mode | The mode of the manifest entry you deploy; the application's `UseDm(mode)` must match it |
| Instance initialization | `CASE_SENSITIVE=1` (the default) in every mode |
| Driver | `DM.DmProvider`; it has **no `net10` asset**, so real connection acceptance must run on a net8.0/net9.0 host (design 5.1). Compiling for net10 is not evidence of a working connection |
| Schema | A dedicated schema; the script does not create it |
| Isolation | Install while no application instance is writing to the schema |

## Execution order

1. Back up the target schema.
2. Stop all application instances that write to it.
3. Confirm the instance's `COMPATIBLE_MODE` and `CASE_SENSITIVE` match the entry you are deploying; a
   different mode invalidates the entry, and `CASE_SENSITIVE=0` invalidates the deployment outright.
4. Create the schema if needed.
5. Render that mode's template with the configured prefix and schema.
6. Execute the rendered copy. No `/`, no `GO`, no PL/SQL block: every statement ends with a semicolon, so
   DISQL, a migration manager, or an ADO.NET loop that splits on `;` all work.
7. Verify that `{prefix}SchemaVersions` contains exactly one row with `Series = '1'` and `Version = 1`
   (the script inserts it; this step only confirms it landed).

## Transactions and failure recovery

DM8 commits DDL implicitly, so the script has **no surrounding transaction** and cannot roll back as a whole.
A failure part-way leaves the objects already created in place.

- Diagnose with the data dictionary filtered by the prefix, plus `{prefix}SchemaVersions`.
- Either finish the remaining statements, or drop the partial objects and start from a clean schema.
- Re-running unchanged is refused by the first duplicate object, deliberately: skipping existing objects would
  hide a half-installed schema (design 10.3).

## Known limitations and open blockers

Mode-dependent behaviours are **release blockers** rather than satisfied requirements. They are listed here
instead of being guessed at, because a wrong guess fails silently:

- **Exact string comparison (design 16.1.1).** A real server in each declared mode must prove that `client`,
  `Client`, `client `, and an accented variant are mutually distinct in `FindByClientId`, `FindByName`,
  `FindByReferenceId`, `FindBySubject`, the CAS predicates, and the bulk revoke filters. If a mode turns out
  to compare case-insensitively even under `CASE_SENSITIVE=1`, the fix belongs in that mode's dialect
  predicate, not in the stores.
- **Paging clause per mode.** Native and MySQL-compatible use `LIMIT ... OFFSET`, Oracle-compatible uses
  `OFFSET ... ROWS / FETCH NEXT ... ROWS ONLY`. A real server in each mode must confirm its declared form is
  accepted, including the no-count, offset-only, and zero-count shapes.

Further limitations:

- **Empty strings.** The Oracle-compatible mode stores `''` as NULL and the MySQL-compatible mode does not;
  native mode is unverified. The stores are unaffected either way because optional filters are skipped for
  empty input (design 9.1).
- **Identifiers are limited to 128 bytes.** `QuoteIdentifier` rejects longer names and the renderer applies
  the same limit to every name the template produces.
- **Digest columns are `VARBINARY(32)`.** The width is enforced by the column type.
- **`CLOB` is not comparable or sortable.** The design already excludes `LargeText`/JSON from comparison,
  ordering, and `IN` (design 9.2), which keeps `CLOB` out of predicates rather than truncating it.

## Verification status

The templates, rendering rules, paging shapes per mode, date binding, and identifier limits are covered by
unit tests that do not need a server. **Nothing here has been executed against a real DM8 instance in any
mode** (see `docs/development.md`, E-028), and the blockers above must be closed per mode before that mode is
declared supported. The driver has no `net10` asset, so that acceptance must run on a net8.0/net9.0 host.
