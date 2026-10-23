# Dameng DM8 deployment scripts (Series 1 / v0001) — one entry per compatibility mode

The shipped files are `1/install/<mode>/v0001.sql.template`, one per declared compatibility mode. Each is a
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
| `DM8 (native mode)` | `0` | `LIMIT [count] [OFFSET n]` | unverified; stores unaffected | `1/install/native/v0001.sql.template` |
| `DM8 (Oracle-compatible mode)` | `2` | `OFFSET n ROWS / FETCH NEXT m ROWS ONLY` | stored as NULL | `1/install/oracle-compatible/v0001.sql.template` |
| `DM8 (MySQL-compatible mode)` | `4` | `LIMIT [count] [OFFSET n]` | distinct from NULL | `1/install/mysql-compatible/v0001.sql.template` |

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

Exact comparison emits a plain equality plus a `CHAR_LENGTH` guard (see the blockers section). DM8 compares
strings case-sensitively only on an instance initialized with the default `CASE_SENSITIVE=1`; an instance
created with `CASE_SENSITIVE=0` folds case in comparisons and would silently corrupt
`FindByClientId`/`FindByName`/`FindByReferenceId`/`FindBySubject` lookups. No compatibility mode overrides
that setting, so **the deployment must run on a `CASE_SENSITIVE=1` instance** — MySQL-compatible migrations
in particular are often initialized with `0`, which is not a supported configuration of this provider.

`CASE_SENSITIVE` is fixed at database-creation time and is not exposed through `v$dm_ini`, so no code can read
it. Instead of trusting the deployment, every DM8 dialect declares an `IOpenIddictStoreStartupProbe`: on the
first use of a database — in the same batch as the schema version check, and cached with it — the store runs

```sql
SELECT CASE WHEN 'abc' = 'ABC' THEN 1 ELSE 0 END FROM {prefix}SchemaVersions
```

and throws `OpenIddictStoreServerConfigurationException` when it returns 1, naming the setting and stating that
the store will not fall back to an inexact comparison. The probe costs one round-trip per process, never one per
request, and the version table is its `FROM` source so no catalog object has to be reachable. The acceptance
suite probes the same behaviour directly (store `abc`, look up `ABC`, expect no row), so the runtime check and
the acceptance evidence test the same thing.

## Rendering the deployment copy

```csharp
var template = File.ReadAllText("sql/1/install/native/v0001.sql.template");
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

Runtime SQL uses `:name` parameters in all three modes, including MySQL-compatible mode, following the
[DM .NET provider guide, section 5.7.4](https://eco.dameng.com/document/dm/zh-cn/pm/net-rogramming-guide.html).
The driver's connection-string `compatibleMode` and the server's `DM.INI` setting have different purposes;
neither the selected application dialect nor a server compatibility mode implies `@name` parameters.

## Execution order

1. Back up the target schema.
2. Stop all application instances that write to it.
3. Confirm the instance's `COMPATIBLE_MODE` and `CASE_SENSITIVE` match the entry you are deploying; a
   different mode invalidates the entry, and `CASE_SENSITIVE=0` invalidates the deployment outright.
4. Create the schema if needed.
5. Render that mode's template with the configured prefix and schema.
6. Execute the rendered copy. No `/`, no `GO`, no PL/SQL block: every statement ends with a semicolon, so
   DISQL or a migration manager can run it as written; an ADO.NET loop that splits on `;` should strip the
   trailing terminator first, because the semicolon is a client convention, not part of the SQL.
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

Two mode-dependent behaviours were **release blockers**. Both are closed for native and Oracle-compatible mode
by the 2026-10-20 acceptance run (E-044). For MySQL-compatible mode they are now covered by their own suite
(`DmMySqlCompatibleStoreContractTests`, the same 15 facts) but that suite has not been run yet, so they stay
open until it is:

- **Exact string comparison (design 16.1.1) — closed for native and Oracle-compatible; unverified for
  MySQL-compatible.** The run proved case and accent significance on both instances, and it surfaced one
  genuine defect: DM8 compares strings blank-padded, so `client` and `client ` compared equal. The dialects now
  guard every exact comparison with `CHAR_LENGTH` (a character count, so the guard cannot depend on either
  side's character set), and the trailing-space variant is rejected on both real instances. Every mode also
  refuses to start on an instance that folds case (see the `CASE_SENSITIVE` section above), so this blocker
  cannot silently reopen.
- **Paging clause per mode — closed for native and Oracle-compatible; unverified for MySQL-compatible.**
  Native's `LIMIT [count] [OFFSET n]` and Oracle-compatible's `OFFSET n ROWS / FETCH NEXT m ROWS ONLY` were
  both accepted by real servers, including the offset-without-count shape. MySQL-compatible renders the same
  `LIMIT [count] [OFFSET n]` as native mode, but "the same text as a verified mode" is not evidence that this
  mode's server accepts it — that is what the MySQL-compatible suite is for.

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
unit tests that do not need a server.

**Accepted on user-provided servers (2026-10-20, see `docs/development.md`, E-044):** the full 15-fact
acceptance suite ran on real DM8 instances — `DmNativeStoreContractTests` against a `COMPATIBLE_MODE=0`
instance and `DmOracleCompatibleStoreContractTests` against a `COMPATIBLE_MODE=2` instance, both on a net8.0
host — and each passed 15/15: install script (into the dedicated schema), re-run refusal, Unicode and
50 000-character `CLOB` values, microsecond `timestamp` round-trips, exact comparison including the
trailing-space guard, the unique keys treating NULLs as distinct, CAS updates, the mode's paging clause,
associated deletes, bulk revocation with a NULL status, and prune. The run also exposed and this series fixes
the templates' inline `NULL` markers, which DM8 rejects just as Oracle does (nullable columns now carry no
marker). The driver has no `net10` asset, so the acceptance ran on a net8.0 host, and a net10 host refuses
these suites outright. Per the E-028 rule the evidence comes from servers the user provided and must be
re-earned on every schema series bump.

**MySQL-compatible mode is unaccepted, and now has the suite it needs.** `DmMySqlCompatibleStoreContractTests`
declares the same 15 facts against `COMPATIBLE_MODE=4`, reading the connection string from the `DmMySql` secret
and the schema from `DmMySqlSchema`. With those two secrets unset every fact reports itself as skipped, so a
missing run stays visible instead of looking like a pass. The two blockers above stay open for this mode until
that suite is run and recorded.
