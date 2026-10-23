# 代码走查问题清单（活文档）

本文档记录历次全库代码走查发现的问题，以及每个问题的处置状态。**未修复项就是后续的修复清单**，按严重度排序；已修复项保留记录与证据，避免重复排查或被静默回退。

维护规则：

- 一轮走查对应一个 `## 第 N 轮` 小节，新的轮次追加在末尾，不覆盖历史。
- 每条必须写清：位置（`文件:行`）、现象、为什么是缺陷（追到真实调用链）、严重度、处置状态。
- 状态只有三种：**已修**（附验证证据）、**待修**（附最小修法）、**不修**（附理由，避免下次重复提出）。
- 排除掉的误报单独记录在"误报与排除"一节，注明排除依据。

严重度定义：

| 级别 | 含义 |
| --- | --- |
| 阻断 | 会导致静默的错误结果（错误数据、错误命中、错误谓词） |
| 重大 | 在特定配置下必然失败，或安全/一致性问题 |
| 中等 | 失败可被观测，但有明确错误信息 |
| 轻微 | 文档与实现不一致、可维护性问题 |

---

## 第 1 轮：2026-10-23 全 provider 走查（达梦多模式为起点）

- 触发：用户要求全库走查，重点是达梦多兼容模式下 SQL 脚本的生成方式。
- 范围：六个 provider（SqlServer、PostgreSql、MySql、Sqlite、Oracle、Dm 三模式）+ 共享运行时 + SQL 生成工具 + 文档。
- 方法：逐文件阅读；SQL Server/PostgreSQL 与 Oracle/MySQL 两组并行子代理复核，关键论断二次人工验证；能用本地方案内测试证明的先写失败测试再修；涉及引擎行为的争议一律查权威手册，不凭记忆。
- 基线：`pwsh ./scripts/verify.ps1` 全绿，**454 项通过（124 单元 + 330 集成），16 项如实跳过**（真实 SQL Server 套件）。

### 达梦多模式：脚本是一套还是多套

这是本轮的直接问题，结论如下。

`COMPATIBLE_MODE` 是**实例级**参数，代码只声明其中三种（`OpenIddictStoreDmCompatibilityMode`）：

| 声明模式 | `COMPATIBLE_MODE` | 脚本路径 | 方言类 |
| --- | --- | --- | --- |
| Native | 0 | `Sql/1/install/native/v0001.sql.template` | `OpenIddictStoreDmDialect` |
| OracleCompatible | 2 | `Sql/1/install/oracle-compatible/v0001.sql.template` | `OpenIddictStoreDmOracleCompatibleDialect` |
| MySqlCompatible | 4 | `Sql/1/install/mysql-compatible/v0001.sql.template` | `OpenIddictStoreDmMySqlCompatibleDialect` |

**当前生成三套，不是一套**：manifest 三条独立条目、三个独立 SHA-256，`UseDm(mode)` 注册对应方言，`SqlTool --mode` 选对应条目。

**但三份脚本的 DDL 正文逐字节相同**（已实测：去掉注释行后三份正文 136 行完全一致，并有测试锁定）。原因成立——达梦的类型系统（`nvarchar`/`clob`/`varbinary`/`timestamp`）不受 `COMPATIBLE_MODE` 门控；随模式变化的是**运行时 SQL**（分页子句、空串语义），不是建表语句。所以按模式分文件在工程上是正确的，只是目前差异仅体现在头部注释与方言行为上。

**真正的问题在 MySQL 兼容模式**：`OpenIddictStoreDmMySqlCompatibleDialect` 与原生方言在代码上等价（`BuildPaging` 输出逐字节相同，`EmptyStringBindsAsNull` 未覆写），且没有真库验收（`Sql/README.md` 自己写着 "remains unaccepted"）。这个模式目前既无行为差异、也无证据。见待修第 2 项。

### 已修（本轮）

| # | 问题 | 位置 | 严重度 | 证据 |
| --- | --- | --- | --- | --- |
| 1 | 反向日期比较生成相反谓词 | `Queries/OpenIddictStorePredicateTranslator.cs:244` | 阻断 | 先写失败测试，修复后通过 |
| 2 | 条件写入用裸 `=` 比较 Id | 四个 Store 共 8 处 | 阻断 | 回退一处代码确认测试失败后恢复 |
| 3 | `DisplayName` 列类型声明与 DDL 不符 | `Queries/OpenIddictStoreColumnMap.cs:66,92` | 重大 | `SchemaConsistencyTests` 通过 |
| 4 | SQL Server 契约测试查系列 7 | `SqlServerStoreContractTests.cs:179,222` | 重大 | 改为引用常量 |
| 5 | SQL Server 精确比较列缺列级二进制排序规则 | `SqlServer/Sql/1/install/v0001.sql.template` | 重大 | 新增 `ExactComparedColumns_DeclareTheBinaryCollation` |
| 6 | 事务提交失败后跳过回滚 | `OpenIddictStoreSession.cs:170` | 重大 | 编译与既有事务测试通过 |
| 7 | SQL Server 安装脚本不能保证回滚 | 同上模板 | 中等 | 加 `SET XACT_ABORT ON` |
| 8 | SQLite `ReadDate` 缺 NULL 守卫 | `Sqlite/OpenIddictStoreSqliteDialect.cs:88` | 中等 | 安装脚本测试通过 |
| 9 | SQLite 日期解析把 `Z` 当本地时间 | 同上 `ParseDateTime` | 中等 | 新增断言 `DateTimeKind.Utc` |
| 10 | SQLite 种子日期用了与方言不同的格式 | `Sqlite/Sql/1/install/v0001.sql.template:154` | 轻微 | 摘要同步更新 |
| 11 | 三个模板标题仍写系列 7 | SqlServer/PostgreSql/Sqlite 模板第 1 行 | 轻微 | 新增 `TemplateHeader_StatesTheSeriesTheScriptInstalls` |
| 12 | SQLite 模板头部要求渲染 `__SCHEMA__`，正文 0 处使用 | `Sqlite/.../v0001.sql.template:2` | 轻微 | 新增 `TemplateHeader_DoesNotDemandAPlaceholderTheBodyLacks` |
| 13 | SqlTool：`--prefix`/`--out` 无值时静默用默认值 | `SqlTool/Program.cs:Value` | 重大 | 改为抛 `ArgumentException` |
| 14 | SqlTool：`--schema` 对 MySql/Sqlite 被静默丢弃 | `SqlTool/DeploymentScriptFactory.cs:Render` | 重大 | 新增 `Render_SchemaOnAProviderWithoutSchemaSupport_IsRejected` |
| 15 | SqlTool：忽略 manifest 的 `type`，会把 upgrade 条目当安装脚本 | 同上 `ReadManifest` | 重大 | 新增 `ListProviders_ExposesOnlyInstallEntries` |
| 16 | SqlTool：`--list` 把三个达梦模式全报成原生方言 | `SqlTool/Program.cs` + `DeploymentScriptFactory` | 轻微 | 断言三条目的方言类型名 |

逐条说明：

**1. 反向日期比较生成相反谓词（阻断）。** 谓词翻译器总是把列放在 SQL 左侧，但取操作数时没有相应镜像运算符：`cutoff > t.CreationDate` 被翻译成 `CreationDate > @cutoff`，语义完全反转，静默返回错误的行集合。原有测试全部把列写在左边，所以从未暴露。修法：`flipped = !ReferenceEquals(column, left)`，同时镜像运算符（`<`↔`>`、`<=`↔`>=`），日期与普通列两条路径共用。测试覆盖两个方向（`cutoff > CreationDate` 应为 1 行，`cutoff < CreationDate` 应为 0 行）。

**2. 条件写入用裸 `=` 比较 Id（阻断）。** `FindByIdAsync` 等读路径走 `Dialect.BuildExactStringEquals`，但 CAS 更新/删除共 8 处用的是 `{Column("Id")} = {Parameter("Id")}`。在大小写不敏感的库上（SQL Server 默认排序规则），对 `ABC` 的更新会静默命中存成 `abc` 的行，而同一字符串的读取返回 null——读写语义分裂。`docs/design.md:254` 明确要求"查询**和条件写入**都必须遵守 16.1.1"。修法：8 处统一改走精确谓词，与同语句里已有的 `ConcurrencyToken` 谓词一致。新增 `ConditionalWrites_CompareTheKeyExactlyLikeReadsDo`，并回退一处代码确认该测试确实会失败。

**3. `DisplayName` 列类型声明错误（重大）。** 列映射把 Application/Scope 的 `DisplayName` 标成可比较/可排序的有界字符串，但六个 provider 的模板全部声明为 LOB（`clob`/`nclob`/`longtext`/`nvarchar(max)`/`text`）。Oracle 与达梦上 `NLSSORT`/`CHAR_LENGTH` 作用于 CLOB 会直接报错。`docs/design.md:523,525` 与 `SchemaConsistencyTests` 早就声明为 `LargeText`，是列映射这一侧写错了。已确认没有生产代码对 `DisplayName` 做过滤或排序。

**4. SQL Server 契约测试查系列 7（重大）。** 两条用例写死 `Series = N'7'`，而模板种子写 `'1'`，永远查不到行。根因是 `ServerAcceptanceTests` 项目**故意不入 solution**（见该项目 csproj 第 4 行注释），从未被编译，字面量因此腐烂。修法：改为引用 `OpenIddictStoreSchema.RequiredSeries/RequiredVersion` 常量，下次重编号不会再有同类残留。

**5. SQL Server 精确比较列缺列级二进制排序规则（重大）。** 只有 3 个唯一键（`ClientId`、`Scopes.Name`、`Tokens.ReferenceId`）带 `COLLATE Latin1_General_100_BIN2`，其余 20 个精确比较列（`Id`、`ConcurrencyToken`、`ApplicationId`、`AuthorizationId`、`Status`、`Subject`、`Type` 及辅助表外键列）都用库默认排序规则。两个后果：谓词里强制的 `COLLATE` 与列声明不一致，使主键查找退化为扫描（最热路径）；且第 2 项在没有列级排序规则时仍不安全。修法：全部补齐，并新增 `ExactComparedColumns_DeclareTheBinaryCollation` 锁定；同步修正模板头与 `Sql/README.md`。

**6. 事务提交失败后跳过回滚（重大）。** `_committed = true` 原先在 `await CommitAsync` **之前**置位，提交抛异常时 dispose 不会回滚。修法：移到 await 之后。

**7. SQL Server 安装脚本不能保证回滚（中等）。** `THROW` 在 `XACT_ABORT OFF` 下不会回滚事务，而 README 承诺"任何失败都回滚"。修法：`SET XACT_ABORT ON;` 放在 `BEGIN TRANSACTION;` 之前，README 同步说明为什么需要它。

**8–10. SQLite 日期处理三处（中等/轻微）。** `ReadDate` 是六个方言里唯一缺 `IsDBNull` 守卫的；`ParseDateTime` 用 `DateTimeStyles.None` 会把带 `Z` 的值当本地时间再打上 UTC 标记，偏移一个时区；种子记录用了 `'%Y-%m-%dT%H:%M:%fZ'`，与本方言写入的固定格式不同，同一列出现两种表示。三处都改掉，manifest 摘要同步。

**11–12. 模板头部与正文不一致（轻微）。** 三个模板标题残留重编号前的系列 `7`（正文第 4 行与种子记录都是 `1`）；SQLite 模板头部要求渲染 `__SCHEMA__`，但 SQLite 没有 schema、正文 0 处使用。两者都是"给 DBA 看的说明与实际不符"。各新增一条防退化测试。

**13–16. SqlTool 四处静默行为（重大/轻微）。** `--prefix`/`--out` 作为最后一个参数时静默回退默认值——会用错误前缀建出应用永远查不到的表；`--schema` 对 MySql/Sqlite 被静默丢弃（运行时是明确拒绝的）；manifest 的 `type` 字段被忽略，第一个 upgrade 条目会被当成安装脚本渲染；`--list` 把所有达梦模式报成原生方言。全部改为显式拒绝或按条目解析。

### 待修（后续修复清单，按严重度）

#### 待修 1：达梦 `CASE_SENSITIVE=0` 导致静默腐化（阻断）

- 位置：`src/Sharp.OpenIddict.Stores.Dm/Sql/README.md:33-42`（部署前提）；`src/Sharp.OpenIddict.Stores/OpenIddictStoreSchemaVersionGuard.cs`（建议的探针位置）。
- 现象：其余五个方言都把精确比较语义强制进谓词（`COLLATE BIN2`、`COLLATE "C"`、`BINARY`、`NLSSORT(...BINARY)`），只有达梦是裸 `=` 加 `CHAR_LENGTH` 长度守卫。达梦的 `CASE_SENSITIVE` 是**建库时固定**、建库后不可改的实例级参数；`CASE_SENSITIVE=0` 的实例上 `'abc' = 'ABC'` 成立，`FindByClientId` 会静默命中错误的行。
- 为什么是阻断：这是本轮唯一仍可能导致静默错误数据的点，且代码目前无法探测——`v$dm_ini` 不暴露该参数。
- 最小修法：在既有的 `OpenIddictStoreSchemaVersionGuard` 里加一条一次性探针，与版本校验同批执行：

  ```sql
  SELECT CASE WHEN 'abc' = 'ABC' THEN 1 ELSE 0 END FROM DUAL
  ```

  返回 1 即在启动时抛出带明确指引的异常（"该实例以 CASE_SENSITIVE=0 初始化，精确比较无法保证"），把静默腐化变成明确报错。探针结果随版本校验一起缓存，不增加每请求往返。
- 备注：`Dm` 的验收套件已经用"存 `abc` 再查 `ABC`"的方式行为化验证过这一点，可以直接复用该断言作为探针的测试。

#### 待修 2：达梦 MySQL 兼容模式无行为差异、无验收证据（重大）

- 位置：`src/Sharp.OpenIddict.Stores.Dm/OpenIddictStoreDmMySqlCompatibleDialect.cs`；`src/Sharp.OpenIddict.Stores.Dm/Sql/1/install/mysql-compatible/`。
- 现象：该方言与原生方言在代码上等价——`BuildPaging` 输出逐字节相同（`LIMIT`/`OFFSET`），`EmptyStringBindsAsNull` 未覆写（继承 `false`），DDL 正文与另两份逐字节相同。`Sql/README.md` 自己标注 "remains unaccepted"，且 `tests/Sharp.OpenIddict.Stores.ServerAcceptanceTests/` 下没有对应的 MySQL 兼容模式套件。
- 处置需要产品决策，二选一：
  - **补证据**：加一套 `DmMySqlAcceptanceFact` 与对应 secrets 键（`DmMySql`/`DmMySqlSchema`），跑通后再声明支持。
  - **砍掉**：若达梦 MySQL 兼容模式没有真实用户，删除该模式、manifest 条目与模板，把声明面从三模式收回到两模式。
- 不建议保留现状：一个既不区分行为、也无证据的模式，只会让人误以为它被验证过。

#### 待修 3：PostgreSQL 摘要列无宽度约束（中等）

- 位置：`src/Sharp.OpenIddict.Stores.PostgreSql/Sql/1/install/v0001.sql.template:92,102,112,122`。
- 现象：四张辅助表的摘要列声明为 `bytea`（任意长度），SQL Server 同名列是 `binary(32)`，达梦/Oracle 是 `varbinary(32)`/`RAW(32)`——数据库层强制 SHA-256 宽度。PostgreSQL 不强制，一个非 32 字节的摘要行会永久不可达：`FindByCollectionAsync` 用 32 字节 SHA-256 做候选查找（`i."UriHash" = @hash`），辅助行与权威 JSON 会静默不一致。
- 最小修法：四个列各加一个检查约束，与其余 provider 对齐：

  ```sql
  "UriHash" bytea NOT NULL CHECK (octet_length("UriHash") = 32),
  ```

  改完更新 `manifest.json` 的 `sha256`。

#### 待修 4：标识符上限按字节计数，但两处注释写的是"字符"（轻微）

- 位置：`src/Sharp.OpenIddict.Stores/OpenIddictStoreIdentifiers.cs:51`（实际按 `Encoding.UTF8.GetByteCount` 计字节）；`src/Sharp.OpenIddict.Stores.MySql/OpenIddictStoreMySqlDialect.cs:35` 与 SQL Server 方言的同类注释写 "characters"。
- 现象：SQL Server 的 `sysname` 与 MySQL 的标识符上限都是 **64 个字符**，而校验按 UTF-8 字节算。对非 ASCII 前缀（例如中文）会**提前拒绝**合法名称。
- 为什么只是轻微：失败方向是安全的（拒绝而非截断），不会产生错误对象；只影响可用性。
- 最小修法：要么把两处注释改成"字节"并说明这是保守上限，要么按 provider 区分字符/字节两种计量。前者一行注释即可，后者需要给方言接口加一个计量单位枚举——除非真有非 ASCII 前缀的用户，否则建议只改注释。

#### 待修 5：SqlTool 未实现架构升级路径（中等）

- 位置：`tools/Sharp.OpenIddict.Stores.SqlTool/DeploymentScriptFactory.cs`；`Sql/manifest.json` 的 `sourceSeries`/`sourceVersion`/`type` 字段。
- 现象：manifest 格式已声明 upgrade 条目（`type: "upgrade"` + `sourceSeries`/`sourceVersion`），但工具只渲染安装脚本，没有 `--type upgrade`、没有源/目标版本选择。本轮已让它**跳过**非 install 条目（避免把升级脚本当安装脚本），但这只是不误用，不等于支持升级。
- 当前无实际影响：系列 1 是首个系列，不存在升级脚本，`SqlAssetTests` 也已断言"没有 upgrade 条目"。
- 最小修法：等到真的发布系列 2 时再实现；届时工具需要 `--type`、`--from`/`--to`，并按 `sourceSeries` 过滤。现在不动是对的。

#### 待修 6：`BuildPaging` 的 count=0 例外未写进接口契约（轻微）

- 位置：`src/Sharp.OpenIddict.Stores/IOpenIddictStoreDialect.cs:41`（接口注释）；`src/Sharp.OpenIddict.Stores.SqlServer/OpenIddictStoreSqlServerDialect.cs:65-68`（实现抛 `NotSupportedException`）。
- 现象：接口注释只说"offset 存在而 count 为 null 时仍须返回正确行数"，没提 SQL Server 对 `count: 0` 会抛异常。新写一个方言或调用方只能靠读 SQL Server 实现的 remarks 才知道。
- 最小修法：接口注释补一句"count 为 0 时允许抛 `NotSupportedException`（SQL Server 的 `FETCH NEXT 0` 是语法错误），调用方须在构造 SQL 前处理 `Take(0)`"。一行注释。

#### 待修 7：`docs/development.md` §9.2 仍指向 `Sql/7`（轻微）——本轮已顺手修掉

- 位置：`docs/development.md:961`（原 §9.2）。
- 现象：该节是**给新会话的前瞻指引**，却写着 `Sql/7/install/v0001.sql.template`，而实际目录早已重命名为 `Sql/1/`。
- 已改为 `Sql/1/install/v0001.sql.template`。
- 明确不改的部分：`docs/development.md` 里其余 `Sql/7` 与 `OpenIddictDapper*` 出现在 E-0xx 历史记录中（E-029/E-032/E-040/E-043 等），它们记录的是当时的事实，**不改写历史记录**；只有前瞻性的 §9 需要与现状一致。

#### 待修 8：Oracle / MySQL 本轮无真库验收（记录，非缺陷）

- 本轮对 Oracle 与 MySQL 只做静态走查加权威手册核对，没有运行真库套件（本地约束：不连接数据库服务端）。
- 需要在用户环境补跑的两项：
  - **Oracle**：`NLSSORT(..., 'NLS_SORT=BINARY')` 作用于谓词会使索引失效（`Sql/README.md` 已承认）。功能正确性有 E-044 的历史证据，但大表性能需实测。
  - **MySQL/MariaDB**：`LIMIT 18446744073709551615` 作为"无界哨兵"在 MariaDB 10.6 上的接受度未验证；`utf8mb4_bin` 是 PAD SPACE，唯一索引会把 `client` 与 `client ` 视为重复（模板已如实记录），但该冲突路径没有真库用例。

### 误报与排除（避免下次重复提出）

本轮由子代理提出、经核实**不成立**的 4 条，均不改：

| 提出的问题 | 排除依据 |
| --- | --- |
| SQLite `GetInt32` 读 INTEGER 列会抛 `InvalidCastException` | 本地实测：`Microsoft.Data.Sqlite` 内部转换正常，`GetInt32` 返回 1，字段类型为 `Int64` |
| Oracle `OFFSET n ROWS` 单独使用（无 `FETCH`）是语法错误 | [Oracle 19c row_limiting_clause 语法](https://docs.oracle.com/en/database/oracle/oracle-database/19/sqlrf/img_text/row_limiting_clause.html)：`[OFFSET ...]` 与 `[FETCH ...]` 是两个各自可选子句 |
| MySQL `BINARY @p` 不能保证字节级精确 | [MySQL 手册](http://uw714doc.sco.com/cgi-bin/info2html?(mysql.info)charset-binary-op&lang=de)：`BINARY 'a' = 'a '` 返回 0，`BINARY 'a' = 'A'` 返回 0，尾随空格与大小写均敏感 |
| 唯一键冲突未翻译成 store 异常，裸驱动异常外泄 | 现有契约即如此：`SqliteApplicationStoreTests.DuplicateClientId_IsRejectedByUniqueIndex` 明确断言抛 `SqliteException` |

另有一条**按设计接受的风险**，记录在此以免被当成缺陷反复提出：

- `tests/Sharp.OpenIddict.Stores.ServerAcceptanceTests/` 故意不入 `Sharp.OpenIddict.Stores.slnx`（该项目 csproj 注释写明），因此 CI 不编译它，套件有腐烂风险——第 4 项就是这种腐烂的实例。缓解措施是让套件里的断言引用运行时常量而非字面量（已做），而不是把项目加进 solution（那会让本地门禁依赖驱动包）。

### 复验方式

```powershell
pwsh ./scripts/verify.ps1
```

预期：Restore → Build（全 TFM，非增量）→ Pack 8 包 → 测试全绿，**454 项通过（124 单元 + 330 集成），16 项跳过**，退出码 0。任何改动的底线是不低于这组数字，且跳过项不得减少。

只跑受影响的子集时，与本轮改动相关的过滤器：

```powershell
dotnet test tests\Sharp.OpenIddict.Stores.IntegrationTests -c Release --filter "FullyQualifiedName~SqlAssetTests"
dotnet test tests\Sharp.OpenIddict.Stores.IntegrationTests -c Release --filter "FullyQualifiedName~SchemaConsistencyTests"
dotnet test tests\Sharp.OpenIddict.Stores.IntegrationTests -c Release --filter "FullyQualifiedName~ParameterBindingContractTests"
dotnet test tests\Sharp.OpenIddict.Stores.Tests -c Release --filter "FullyQualifiedName~SqlToolTests"
```

注意：改动任何 `Sql/**/*.sql.template` 后必须同步该 provider `manifest.json` 里的 `sha256`，否则 `SqlAssetTests` 会失败；`scripts/verify.ps1` 的打包先于测试，正是为了让包资产测试看到本次改动后的字节。
