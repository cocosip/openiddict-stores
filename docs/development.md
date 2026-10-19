# Sharp.OpenIddict.Stores 开发与进度文档

文档版本：1.7。创建日期：2026-10-19。修订日期：2026-10-19。

技术基线：[项目设计文档 v1.9](design.md)。本文是固定维护的开发任务、进度和完成记录，设计中的 API、数据结构和行为合同以设计文档为准。实现需要改变这些合同，应同步修订设计、本文和测试，不能只修改任务验收条件。

## 1. 当前进度

截至 2026-10-19，已完成源码调研、正式设计编写及走查问题修订，已提交的设计基线为 `982c156`（v1.2）；此后补齐精确比较、查询 NULL 语义、任务依赖、逐数据库 SQL 脚本的交付、版本与持续维护验收，依赖中央包管理与逐 TFM 版本分线规则，并将实施基线改为唯一稳定版 OpenIddict 7.7.2（不再依赖任何预览包），任务清单相应收敛为四类实体、四类 Store 和单系列多目标验证，设计收敛为 v1.9。代码与测试已整体纳入版本控制（E-025），工作区不再积压未跟踪文件。当前共 50 项任务，25 项完成（3 项文档、BASE-01 依赖锁定、BASE-02 合同清单、ENG-01 工程布局、ENG-02 四类模型、COMPAT-01 五目标工程、STORE-01 SQLite 方言与安装脚本、STORE-03 Application/Scope Store、STORE-04 Authorization/Token Store、STORE-05 集合索引、STORE-06 CAS 与原子回滚、LIFE-03 批量撤销、LIFE-04 独立清理、LIFE-01/02 关联删除与竞态、QUERY-01～03 查询翻译、STORE-02 访问器收尾、COMPAT-02～04 包资产与兼容性、DB-02 SQL Server 方言与真库验收），25 项实现与验证任务未完成（ENG-03、STORE-02 部分完成）。设计文档状态仍为“设计评审稿”，完成编写不代表用户已经确认设计。

当前仓库已有 `.slnx`、集中构建与包管理文件、8 个产品工程、3 个测试工程和 2 个示例骨架（不提交 `global.json`，见 E-027），可还原、构建、打包并通过 285 项测试（45 项单元：工程约束、Store 合同冻结、模型列映射、注册与选项校验、JSON/UTC 转换；240 项集成：SQLite 安装脚本真库执行、六 provider 脚本资产检查、四类 Store 真库合同、集合索引与 CAS 合同、批量撤销与清理合同、关联删除矩阵与竞态、查询回调翻译、访问器与无截断、包资产与低 TFM 加载、原生 Manager 端到端、自定义前缀/schema 的安装副本）。核心包已具备 Builder/选项/连接工厂/默认实体注册、转换器与四类 Store，四类 Store 已全部注册进 DI，并已验证可经原生 Manager（含非泛型接口）创建、查找与更新；Store 的读写、删除、撤销、清理与 IQueryable 查询回调均已可用；SQLite 与 SQL Server 各有一份以模板交付的 v0001 安装脚本，渲染后通过真库验收。其余四种方言尚未实现，协议联调按约束暂不实施。以下未完成项均为计划，不代表已有可调用功能。

| 阶段 | 任务编号 | 当前状态 | 阶段完成条件 |
| --- | --- | --- | --- |
| P0 文档基线 | DOC-01～DOC-03 | 文档整理完成，设计待确认 | 正式设计、修订和可维护开发清单齐备 |
| P1 依赖与驱动验证 | BASE-01～BASE-04 | BASE-01/02 完成；BASE-03/04 未开始 | 锁定依赖/接口，Oracle 与达梦最小真库验证成功 |
| P2 工程与基础设施 | ENG-01～ENG-05 | ENG-01/02 完成；ENG-03 已完成配置/DI/Store 注册；ENG-04/05 未开始 | 7.x 五目标工程、模型、DI、连接与测试设施可用 |
| P3 SQLite 基础读写 | STORE-01～STORE-06 | 全部完成 | 四类实体的基础读写、Token/Scope 简单删除、集合索引及 Update CAS/事务基础通过；完整关联删除在 P4 验收 |
| P4 查询与生命周期 | QUERY-01～QUERY-03、LIFE-01～LIFE-05 | QUERY-01～03 查询与流式读取、LIFE-01～04 删除/撤销/清理完成；LIFE-05 未开始（需服务端联调） | 查询、流式读取、删除、撤销和清理合同通过 |
| P5 数据库验收 | DB-01～DB-07 | DB-02 完成（§5 记“部分通过”）；其余未开始 | 七种服务器及声明的 DM 模式分别通过完整验收 |
| P6 多目标与包资产 | COMPAT-01～COMPAT-04 | 全部完成 | 五 TFM 构建、包资产与驱动兼容性记录通过 |
| P7 脚本维护、升级、协议与示例 | MIG-01～MIG-04、APP-01～APP-04 | 未开始 | SQL 资产维护、协议回归和两个 net10 示例完成 |
| P8 CI 与交付候选 | CI-01～CI-03、REL-01～REL-02 | 未开始 | 门禁通过，具备可审阅的包和证据 |

这是按交付依赖组织的阶段，不是固定工期。迁移用例、Manager 测试和使用说明应随功能持续补充，P7/P8 负责最终联验，不能将测试统一推迟到最后。

## 2. 进度维护规则

- `- [ ]` 表示未完成；`- [x]` 表示达到该任务全部完成条件。任务编号稳定保留，不因排序变化重编。
- 未勾选任务可在条目下标注“进行中”或“阻塞”，附当前产物、剩余工作、具体阻塞原因。没有开始时无需重复写状态。
- 只有部分实现时保留未勾选，并记录已完成的部分。需要独立验收的新增子任务使用原编号的后缀，例如 `STORE-01a`。
- 勾选时必须补充第 8 节完成记录：适用系列、实际提交/工作区、验证命令、运行环境、结果及缺口。未提交可注明工作区，提交后补上真实提交号，不预填将来提交号。
- 同系列各 provider 共用的功能任务，以 SQLite 和共享合同测试作为开发基线；这不代表全部数据库已验证。数据库正式验收以第 5 节逐系列记录为准。
- 跳过、未执行、只生成 SQL、只模拟驱动、只编译通过均不能记作真库验收通过。测试失败后重新打开对应复选框，保留历史证据与失败原因。
- 更新任务时同时维护阶段表、数据库矩阵和完成记录；任何支持声明都必须能追溯到对应版本的证据。依赖、SQL 或行为改变后，重新验证受影响部分。
- 数据库结构/SQL 变更同时更新设计第 10 节要求的脚本、清单和第 5.1 节维护矩阵；涉及已完成任务时重新打开受影响的 MIG/DB/CI 项。不能只勾选 Store 功能而省略安装与升级路径验证，也不能修改已发布 SQL 来补齐验收。

一般任务的完成条件是：产物已实现、相关验证通过、必要文档已同步、没有该任务范围内未处理的失败。公共源码、测试或跨系列移植分别验收，不自动勾选另一系列的任务。

## 3. 实施时必须保持的边界

| 项目 | 当前实施（7.7.2 系列） | 8.x 主线（暂不实施） |
| --- | --- | --- |
| OpenIddict 设计基线 | 7.7.2 稳定版；发布依赖范围 `[7.7.2,8.0.0-0)` | 待 8.x 稳定版发布后确定；不锁定预览包 |
| 发布类库 TFM | netstandard2.0、netstandard2.1、net8.0、net9.0、net10.0 | net10.0 |
| Store | Application、Authorization、Scope、Token | 前述四类，加 Resource、Session |
| 测试、示例、可运行工具 | 全部 net10.0 | 全部 net10.0 |
| 建议分支 | release/7.x | main |

分支名是设计建议，不表示已创建。上游 8.x 预览标签仅为参考，不自动替换锁定的 7.7.2。SDK、Dapper、测试框架、驱动及 Microsoft.Extensions 的精确版本由 BASE-01 核实并记录；设计中出现的本机包版本不等于最终选型。

依赖版本只有中央包管理一个来源：根 `Directory.Packages.props` 集中登记，工程文件不写 `Version`；产品自身版本在根 `Directory.Build.props` 统一。7.x 五目标必须按设计 4.1.1 逐 TFM 分线引用 Microsoft.Extensions（netstandard2.0/2.1 与 net8.0 用 8.0.x、net9.0 用 9.0.x、net10.0 用 10.0.x），不能把 net10 依赖喂给低 TFM 资产；8.x 主线只有 net10.0，统一 10.0.x。驱动只进测试/示例，缺失 TFM 资产时如实标注不支持。

所有八个产品包使用 `Sharp.OpenIddict.Stores` 命名体系，同系列同版本；provider → 核心包 → Models/OpenIddict.Core/Dapper。保持原生 Manager/Descriptor/Store 扩展点，禁用实体缓存，保留原生附加过滤。产品不引入 EF、ABP、其他 ORM 或强制传递的 ADO.NET 驱动。

JSON 是集合权威数据；四张辅助表同事务维护，摘要仅筛选候选，最终做完整值 Ordinal 复核。普通 Delete、七种批量 Revoke、Prune 各自遵守设计第 16 节，不能共享一套简化的级联/状态规则。

普通有界字符串的等值/不等值/成员匹配统一按设计 16.1.1 精确比较；批量写入不能依赖 Manager 的读取附加过滤。查询回调按设计 9.1 的 C# NULL 真值表补偿，不能直接用 SQL unknown 或含 NULL 的 NOT IN 决定结果；Oracle/DM 的空串参数也不得误当作显式 NULL 条件。

数据库由显式 SQL 安装升级，运行服务不自动 DDL。真库测试只使用专用测试库/schema。五 TFM 构建和在 net10 加载低 TFM 资产，均不能表述为已在 net8/net9 原生运行。

各库部署 SQL 唯一源码在对应 provider 的 `Sql/`，按设计第 10 节维护安装、增量/跨系列升级、manifest 和执行说明，并随 provider 包交付。脚本维护是持续任务，模型/索引变化时同一变更完成升级路径与回归；测试、示例和 CI 不复制一套建表语句。

## 4. 开发任务清单

### P0：文档基线

- [x] **DOC-01 源码调研与正式设计。** 产物：`docs/design.md`。完成：明确 7.7.2 基线、八包布局、六类数据库和原生接入方式；依据可定位到上游标签及源码。证据：`982c156`。
- [x] **DOC-02 走查问题与维护合同修订。** 产物：设计 v1.4。完成：补齐 EF Core 对照、关联删除矩阵、逐方法撤销、Prune、集合边界、普通字符串精确比较、查询 NULL 语义、SQL 脚本持续维护与验收追踪。证据：`982c156` 的历史基线及 E-003/E-004 的工作区修订；不表示运行验证已完成。
- [x] **DOC-03 开发与进度清单。** 产物：本文及 README 入口。完成：任务、依赖、验收矩阵和完成记录可持续更新。证据：本次工作区及第 8 节记录。

### P1：锁定版本、接口与驱动风险

前置：设计基线；对应设计第 2～5、13、18 节。此阶段的最小验证工程也必须使用 net10 并纳入同一 `.slnx`，后续复用为集成测试。

- [x] **BASE-01 锁定可复现依赖。** 产物：`Directory.Packages.props` 与本文完成记录（`global.json` 已按 E-027 移除）。
  验收：核实 OpenIddict 7.7.2 的实际发布和依赖资产，记录 SDK、Dapper、Microsoft.Extensions、测试工具及七种服务器驱动组合的精确版本、来源、TFM 与许可证限制；从干净环境可还原，依赖范围排除 8.x 及任何预览版。中央包管理为唯一版本源（工程文件无 `Version` 属性、传递依赖提升在中央清单登记），并按设计 4.1.1 记录每个 TFM 的 Microsoft.Extensions 版本线及上游同 TFM 依赖下限，验证低 TFM 组不引入高代际依赖。SDK 只要求 .NET 10，不在仓库固定补丁版本。
- [x] **BASE-02 建立逐版本 Store 合同清单。** 产物：`docs/contracts/7x-store-contracts.md` 与 `tests/Sharp.OpenIddict.Stores.Tests/StoreContractTests.cs`。由 E-007 完成。
  验收：按 7.7.2 真实接口列出全部成员、签名、返回/取消语义、Manager 调用和 EF 行为来源；识别 `CountAsync` 无状态参数重载、Authorization/Token 的 `FindAsync` 位置参数等 7.x 差异，列明本项目第 9.1/16.1.1 节的 NULL 补偿与精确字符串比较保证。未实现成员明确留空缺，不能以统一默认返回值冒充实现。
- [ ] **BASE-03 Oracle 最小真实驱动验证。** 产物：`tests/Sharp.OpenIddict.Stores.IntegrationTests/` 的 Oracle fixture/验证用例。
  验收：专用 Oracle 19c+ schema 上验证按名称绑定、NULL/空串、Unicode、大于 4 KB/32 KB 的 LOB、UTC 微秒、分页、事务回滚和取消/释放；额外验证有界字符串的大小写/重音/尾空格精确等值不受会话排序配置影响，以及空串与 NULL 参数在谓词中的区分。记录实际驱动/服务器版本及参数适配限制，不用 SQL 文本测试代替。
- [ ] **BASE-04 达梦最小真实驱动验证。** 产物：同一集成工程的 DM fixture/验证用例。
  验收：DM8 原生模式执行上述验证；记录实际字符集、模式与驱动的异步能力。声明 Oracle 兼容模式时单独验证，不因 Oracle 成功复用其结论；后续 DB-07 仍须完整合同验收。

### P2：7.x 工程、模型与基础设施

前置：BASE-01/02；公共方言与参数合同定稿须吸收 BASE-03/04 的实测结果。对应设计第 4、6、7、14、15、17 节。

- [x] **ENG-01 完整工程布局与集中构建。** 产物：`Sharp.OpenIddict.Stores.slnx`、`Directory.Build.props`、八个 `src/` 工程、三个 `tests/` 工程和两个 `samples/` 工程。
  验收：统一 net10 SDK、Nullable、集中版本和包元数据；所有工程进入 slnx；八个产品类库按设计 4.1.1 多目标 `netstandard2.0;netstandard2.1;net8.0;net9.0;net10.0`，测试、示例和工具使用 net10；产品引用方向、Sharp 名称和不传递驱动的边界正确，测试/示例/工具不参与产品打包。空工程仅代表此任务完成，不代表实现完成。所有工程使用中央包管理，工程文件不出现 `Version` 属性，产品包依赖组不含 ADO.NET 驱动。
- [x] **ENG-02 四类默认模型与列映射。** 产物：Models 工程及 `tests/Sharp.OpenIddict.Stores.Tests/ModelContractTests.cs`。由 E-008 完成。
  验收：逐项覆盖设计第 15 节字段，string GUID 主键/并发标记、显式 nullable FK、UTC 日期、JSON 字符串与继承扩展点正确；无 EF 导航属性或额外列自动映射承诺；无 7.7.2 合同之外的实体或字段（不预置 Resource、Session、SessionId）。
- [ ] **ENG-03 Builder、Options 与原生 DI。** 产物：核心及六个 provider 的注册扩展。
  验收：原生默认实体/Store 注册、重复注册幂等、生命周期正确；多 provider 冲突、缺工厂、非法 schema/prefix/timeout 在使用前拒绝；无论配置顺序如何，最终重新启用实体缓存都被拒绝，附加过滤仍开启。
  进行中（E-009、E-012）：已实现 `UseDapper`/`UseProvider`/六个 provider 的 `Use*` 扩展/`UseConnectionFactory` 两种形态/`SetSchema`/`SetTablePrefix`/`SetCommandTimeout`、`OpenIddictStoreOptions` 校验器、原生 `OpenIddictCoreOptions` 缓存校验器、默认实体注册、幂等，以及已实现的 Store 注册（当前为 Application）；**未完成** Scope/Authorization/Token 三类 Store 的注册与其余五种方言，它们尚未实现，因此不预置占位注册。ENG-04/05 另行负责连接执行细节与共享测试设施。
- [ ] **ENG-04 连接、执行与错误处理。** 产物：核心连接工厂/执行代码及 provider 参数适配。
  验收：每操作获取并拥有新连接，负责 OpenAsync 和释放；异步枚举例外由 QUERY-03 验证；不释放外部 DataSource，不接收共享事务；无静态当前数据库。方言统一提供普通有界字符串非空精确等值谓词，供查询/CAS/批量写入复用，SQL Server 同时覆盖 BIN2 与 DATALENGTH，其他 provider 按真库验证选择二进制比较。超时/取消传递，原生并发异常、驱动异常链和无敏感信息的 ILogger 日志符合设计。
- [ ] **ENG-05 共享测试与隔离设施。** 产物：三个测试工程的合同基类、provider fixture 和专用环境配置。
  验收：用同一组断言覆盖不同 provider，Oracle/DM 早期用例归入 fixture；缺环境明确跳过且输出缺口，默认配置不能清理用户已有库；具备回滚故障注入、并发时序控制和强制摘要碰撞用例所需的局部测试手段。

### P3：SQLite 基础读写与集合索引

前置：ENG-01～ENG-05。对应设计第 7、8、14～17 节。本阶段只完成 SQLite 基础读写与事务能力；Application/Authorization 的关联 DeleteAsync 归 LIFE-01/02，查询回调/流式读取归 QUERY-01～03，Revoke/Prune 归 LIFE-03/04。未完成这些后续任务时不得宣称四类 Store 的全部合同已通过，P5 再按完整合同进行每个真实数据库验收。

- [x] **STORE-01 SQLite 方言与安装 SQL。** 产物：Sqlite provider、版本化 SQL 和架构版本检查。由 E-010 完成。
  验收：四主表、四辅助表、限制删除外键、唯一键/索引及 SchemaVersions 正确；SQL 按设计第 10 节存入 provider/Sql，附清单/摘要/执行说明，测试复用同一安装脚本；显式安装，schema 不符拒绝使用；标识符校验、参数化、分页、超时及 SQLite 外键启用策略可验证；取消连接后不泄漏资源。MIG-03/04 负责后续跨库汇总与升级链验收，不作为本任务完成前置。
- [x] **STORE-02 JSON、字典与 Getter/Setter。** 产物：`OpenIddictStoreJson.cs`、`OpenIddictStoreJsonWebKey.cs` 与四类实体的全部访问成员。由 E-011/E-020 完成。
  验收：ImmutableArray/ImmutableDictionary、CultureInfo、JWK、本地化、null/空集合与非法 JSON 均符合原生合同；UTC 规范化到微秒；密钥/引用标识不重复散列；Unicode 与长 Payload 无截断。
  进行中（E-011）：集合、本地化字典、属性/设置字典、UTC 微秒规范化与 SQLite 日期文本已实现并有用例；**未完成** JWK 集合转换（需要 `Microsoft.IdentityModel.Tokens` 的 `JsonWebKeySet`，随 STORE-03 的 Application 访问器一起落地）、长 Payload/Unicode 无截断的真库断言，以及全部 Getter/Setter（依赖 Store 实现）。
- [x] **STORE-03 Application、Scope Store。** 产物：`Stores/OpenIddictStoreApplicationStore.cs`、`Stores/OpenIddictStoreScopeStore.cs` 与两份真库合同测试。由 E-012/E-013 完成。
  验收：该两类实体的创建、读取、更新、固定查找及唯一性通过；ClientId/Name 的精确等值、长度及可空唯一值符合设计；Scope 删除同步删除 ScopeResources 且不改动 Scope.Resources 的权威 JSON，具备根 CAS。Application 关联删除统一归 LIFE-01/02；集合查询、查询回调及流式读取分别归 STORE-05、QUERY-01～03，不作为本任务完成前置。
  完成（E-012/E-013）：Application 与 Scope Store 各覆盖原生接口全部成员；26 项真库合同测试 + 6 项原生 Manager 端到端测试。查询回调明确拒绝（归 QUERY-01～03），Application 关联删除的竞态细节归 LIFE-01/02。
- [x] **STORE-04 Authorization、Token Store。** 产物：`Stores/OpenIddictStoreAuthorizationStore.cs`、`Stores/OpenIddictStoreTokenStore.cs` 与两份真库合同测试。由 E-014 完成。
  验收：该两类实体的创建、读取、更新、固定筛选及 Token 单体删除通过；指定键拒绝空值、可选条件空值不筛选、外键可空语义正确。Authorization 关联删除统一归 LIFE-01/02，查询回调及流式读取归 QUERY-01～03，批量撤销与清理归 LIFE-03/04；这些后续功能不作为本任务完成前置，也不能用占位实现声称已可用。
  完成（E-014）：Authorization 与 Token Store 各覆盖原生接口全部成员（32/43）；查询回调、七种批量撤销与 Prune 一律抛 `NotSupportedException` 并说明归属任务，测试已锁定该行为，不返回假的 0 计数。
- [x] **STORE-05 四张集合辅助索引。** 产物：`Stores/` 内的集合索引维护与 URI/RequiredScopes/Resource 查找。由 E-015 完成。
  验收：JSON 与索引同事务，owner+Position 主键及非唯一 SHA-256 候选索引正确；完整值 Ordinal 去重/复核、一个实体只返回一次；长于 200 字符、大小写、尾空格、强制摘要碰撞以及全部 RequiredScopes 匹配均通过，无新增长度限制或摘要唯一约束。
- [x] **STORE-06 CAS 写入与原子回滚。** 产物：`OpenIddictStoreSession`/`OpenIddictStoreTransaction` 共享事务操作及并发测试。由 E-015 完成。
  验收：Update 与 Token/Scope 简单删除按 Id/旧标记精确 CAS，0 行转换原生 ConcurrencyException，键或标记的大小写/尾空格变体不得命中；关联创建/更新在同一事务精确确认父键；提交成功才改内存标记，索引写失败、取消、根版本冲突回滚全部变更。提供后续关联删除可复用的内部事务/CAS 基础，不递归调用会独立提交的 public Store；Application/Authorization 的完整删除及竞态由 LIFE-01/02 验收，不作为本任务前置。

### P4：查询、删除、撤销与清理

前置：STORE-01～STORE-06 的上述基础读写范围；不要求关联删除、查询回调、Revoke 或 Prune 在 P3 已完成。QUERY、LIFE 可分别推进；LIFE-02 依赖 LIFE-01，LIFE-05 依赖 QUERY-01～03、LIFE-01～04，Manager/Server 联验覆盖相应已实现功能。对应设计第 8、9、11、16 节。

- [x] **QUERY-01 单表表达式白名单与参数化。** 产物：`Queries/` 下的列映射、谓词翻译器与记录式 `IQueryable`。由 E-019 完成。
  验收：有界列/日期比较、闭包常量、布尔组合、Contains→IN/等价谓词通过；普通字符串使用设计 16.1.1 的精确比较。逐项通过设计 9.1 两张真值表，包括列/常量、同表两列、NULL 集合报错、空集合、只含 NULL/混合集合、Contains 取反、可空日期及 NOT/AND/OR；不得依赖含 NULL 的 NOT IN。Oracle/DM 按实际空串语义在绑定前化简，不将显式空串比较变成 IS NULL；SQL Server 覆盖尾空格和 BIN2 长度补偿。LargeText/JSON 仅允许 NULL 判断及投影；不支持的表达式在 SQL 前拒绝，无整表内存回退。
- [x] **QUERY-02 算子顺序、投影与原生 Manager 查询。** 产物：`Queries/OpenIddictStoreQueryBuilder.cs` 的算子分解与投影，四个 Store 的查询回调已接入。由 E-019 完成。
  验收：依赖 QUERY-01；OrderBy/ThenBy、Skip/Take、实体/标量/简单 DTO/匿名投影、Count/Get/List 保持算子顺序；覆盖 Take 后 Where、分页后 Count、Get 首项、object/实体转换，并在泛型/非泛型 Manager 的查询中复用精确比较与 NULL 合同；默认稳定 Id 排序、count=0、负参数、offset 无 count 和参数上限均有断言，不能正确翻译时明确拒绝。
- [x] **QUERY-03 真正逐行异步枚举。** 产物：`OpenIddictStoreSession.StreamAsync` 逐行读取；查询回调经同一路径流式返回。由 E-012/E-019 完成。
  验收：大结果逐行消费，未在 QueryAsync 后包装缓冲集合；首行后提前退出、取消、读取/映射异常均释放 reader/连接；读取期间无连接复用，低 TFM 路径不靠 Task.Run 假装异步。
- [x] **LIFE-01 普通删除完整关联矩阵。** 产物：Application/Authorization 的关联 `DeleteAsync` 与 `DeleteAssociationContractTests.cs`。由 E-018 完成。
  验收：在 P3 基础读写上实现 Application/Authorization 的关联 DeleteAsync，覆盖设计 16.2.1 全部入口，包含 P3 已完成的简单删除回归；Application 按 A0→A/T 处理，含 Token.ApplicationId 为空但 AuthorizationId 属于 A、Token 同时绑定另一 Authorization、跨应用授权等情形；Authorization 删除清除其全部直接 Token，不反向删除 Token 引用的其他 Authorization；集合外父实体保留。
- [x] **LIFE-02 删除竞态与保留行并发标记。** 产物：根记录写入锁 + CAS 回滚实现与竞态测试。由 E-018 完成。
  验收：根及间接 Authorization 锁防止关联写入穿过窗口；关联删除中途失败或根 CAS 冲突，数据、FK、辅助行及标记整笔回滚，无孤儿或假成功；旧快照更新抛原生并发异常。
- [x] **LIFE-03 七种批量撤销合同。** 产物：`Stores/` 内 Token/Authorization 的 Revoke 实现及逐方法测试。由 E-016 完成。
  验收：严格执行设计 16.2.2，client 为 Application 主键，键/Subject/Status/Type 使用精确谓词；仅三个 Token.RevokeBy* 隐含排除精确 revoked，NULL 分支仍受指定键约束；通用 Revoke/Authorization.RevokeBy* 不额外排除。valid+revoked 重复计数分别为 1→0、2→2，显式 valid 为 1→0；覆盖 NULL、不匹配行及 `alice`/`Alice`/`alice ` 主体隔离、`revoked ` 不被错当已撤销，断言命中行标记轮换、排除行不变；无跨 Store 隐式撤销，不依赖 Manager 二次过滤。
- [x] **LIFE-04 独立 Prune 路径。** 产物：`OpenIddictStoreBase.PruneCoreAsync` 与 Token/Authorization 的清理谓词及合同测试。由 E-017 完成。
  验收：一次捕获 UTC now，CreationDate 严格早于阈值且非 NULL；状态/类型使用精确比较，NULL 补偿、过期/ad-hoc/关联授权状态均符合设计，覆盖大小写/尾空格状态边界；批次≤500、最多 1000 批并独立提交；锁下重检完整谓词及 NOT EXISTS，不以 CAS 代替。候选失格不提交中间修改，不调用普通 Delete 或删除新 Token；失败/取消如实传播。
- [ ] **LIFE-05 原生 Manager 状态与协议回归。** 产物：Server.Tests 的 Manager/Server 专项。
  验收：同作用域先读再撤销后重读、新作用域重读均得到已提交状态，旧 POCO 不自动刷新且旧标记更新失败；T.AuthorizationId=A、T.ApplicationId=NULL 且均 valid 时，普通删除 A 后 T 不存在，启用对应存储校验的 Server 拒绝原令牌；不宣称中止已完成校验的在途请求。

### P5：其他 provider 与逐数据库验收

前置：P3/P4 的共享合同基线；Oracle/DM 还依赖 BASE-03/04。对应设计第 5、10、11、15～18 节。各任务的产物是对应 provider 方言、参数适配、安装 SQL、fixture 与运行记录。安装 SQL 同时交付设计第 10 节规定的清单、摘要和执行说明，填入第 5.1 节对应状态；MIG-03/04 是各库脚本的最终维护/升级联验，不作为 P5 的反向前置。

- [ ] **DB-01 SQLite 3.35+ 完整验收。** 验收：临时文件上通过第 5 节完整合同，验证实际 SQLite 版本、外键、UTC 格式、读写锁/并发限制与分页；单元模拟不替代文件数据库运行。
- [x] **DB-02 SQL Server 2016+ 实现与验收。** 验收：Microsoft.Data.SqlClient 实测；覆盖可空过滤唯一索引、尾随空格、Unicode、datetime2(6)、摘要索引、分页和关联写入锁；不以新版专属 SQL 实现所声明的最低版本。
- [ ] **DB-03 PostgreSQL 14+ 实现与验收。** 验收：Npgsql 实测；覆盖标识符/schema、bytea 摘要、timestamp(6) UTC 转换、分页和事务/锁；驱动参数适配不污染其他 provider。
- [ ] **DB-04 MySQL 8.0+ 实现与验收。** 验收：MySqlConnector 实测；覆盖 utf8mb4、排序/尾空格、有界索引总键长、longtext、datetime(6)、影响行数和锁；记录实际服务器配置。
- [ ] **DB-05 MariaDB 10.6+ 独立验收。** 验收：同一 MySql provider 在 MariaDB 单独通过全部合同和版本化 SQL，差异限制有明确处理；不得复用 MySQL 的通过结论。
- [ ] **DB-06 Oracle 19c+ 实现与完整验收。** 验收：在 BASE-03 之上完成全部 Store/集合/查询/生命周期合同，含 NCLOB/RAW32、唯一 NULL、名称上限、IN 参数保护和删除锁；驱动适配符合公开属性及局部作用域边界，DDL 失败如实报告已提交部分。
- [ ] **DB-07 DM8 实现与完整验收。** 验收：独立 Dm provider，原生模式通过全部合同，含 CLOB/Unicode、二进制摘要、UTC、标识符及锁；如声明 Oracle 兼容模式，须另行完整运行并记录。记录实际模式，不能把“Oracle SQL 可执行”视为达梦验收。

### P6：五目标构建与包资产验证

前置：ENG-01/E-006 的工程与依赖分线已完成；真实数据库证据来自 P5。对应设计第 2～4、18 节。首版只有 7.7.2 一个系列，本阶段不再创建兼容分支，只验证多目标构建、包资产和低 TFM 消费者行为。

- [x] **COMPAT-01 五目标工程与逐 TFM 依赖分线。** 产物：八个产品类库的五 TFM 目标、中央清单条件依赖组。由 E-006 完成。
  验收：独立对接 OpenIddict 7.7.2，发布依赖排除 8.x 及任何预览版；四实体模型不含 Resource/Session/Token.SessionId 及其注册，所有八个产品类库为五 TFM，测试/示例/工具仍为 net10。按设计 4.1.1 逐 TFM 分线引用 Microsoft.Extensions（netstandard2.0/2.1、net8.0 用 8.0.x；net9.0 用 9.0.x；net10.0 用 10.0.x），在中央清单用条件 `ItemGroup` 登记，且每 TFM 不低于上游同 TFM 依赖下限。
- [x] **COMPAT-02 五 TFM Release 构建与包资产。** 产物：`PackageAssetTests`（43 项）逐包核对资产与 nuspec。由 E-021 完成。
  验收：八个包均含五种正确资产及匹配的分 TFM 依赖组，低 TFM 组出现高代际依赖即不通过；无预览版本、无工程级版本例外，消费者还原无版本冲突。
- [x] **COMPAT-03 低 TFM 资产独立加载验证。** 产物：`LowTargetFrameworkAssetTests`（4 项）在隔离 `AssemblyLoadContext` 中加载并调用 ns2.0/2.1 资产。由 E-022 完成。
  验收：独立加载并调用 netstandard2.0/2.1 资产，实际走到其读取/释放/异步路径，不能误选 net10 资产。结果注明宿主仍为 net10，不声称旧运行时实测通过。
- [x] **COMPAT-04 驱动与 TFM 兼容性记录。** 产物：设计 §5.1 实测矩阵（6 驱动 × 5 TFM）。由 E-023 完成。
  验收：逐驱动记录实测可用的 TFM（如 Oracle 仅 net8.0/netstandard2.1、达梦无 net10 资产），未实测组合显式标记，结论同步 APP-04 的支持矩阵。

### P7：SQL 脚本维护、架构升级、协议与示例

前置：相关系列 Store/provider 可用。对应设计第 10～12、17 节。MIG-03 汇总 STORE-01、DB-01～07 的脚本资产；MIG-04 依赖 MIG-01～03，负责最终升级链及变更维护联验。目录与维护规则从首次编写 SQL 起就执行，不能等到 P7 再补一套脚本。

- [ ] **MIG-01 安装脚本与版本误用检查。** 产物：各 provider 包内版本化 SQL 模板及迁移合同用例。
  验收：空库安装、schema/prefix 下主表/辅助表/版本记录完整；模板经 `RenderDeploymentScript` 渲染后执行，默认与自定义前缀各一次；错误系列/版本在存储操作前拒绝，无自动 DDL，检查缓存仅限当前存储配置；模板随包交付且约束名称不因前缀截断重名。
- [ ] **MIG-02 跨系列升级（暂不适用）。** 产物：8.x 稳定版实施后补 Resource/Session 升级 SQL、fixture 和操作说明。
  验收：当前版本不生成 7→8 脚本，任务保持未完成状态并在第 5.1 节注明不适用；等 8.x 稳定版发布并实施后，要求保留 ID、令牌、JSON、集合索引，不从任意长 Scope.Resources 值自动创建/截断 Resource.Name，先备份停旧写入再升级。Oracle/DM 部分 DDL 提交可诊断，不伪装全事务回滚，不承诺混合写入/自动降级/既有 EF 库直接复用。
- [ ] **MIG-03 各数据库 SQL 资产与维护清单。** 产物：六 provider 的 `Sql/`、清单/执行说明、包内 `sql/` 文件及第 5.1 节矩阵。
  验收：安装/同系列升级模板来源唯一，目标服务器/模式、精确源/目标版本、路径、SHA-256 与包文件可追溯；UTF-8/LF、发布历史不可变及包提取一致性检查通过。文件以 `.sql.template` 交付，清单声明 `rendersTokens`，渲染入口与运行时校验同源；直接执行模板必须失败而不是装出半套表。MySQL/MariaDB 共用与差异文件有明确映射；DM 仅列实测声明模式。执行说明包含客户端/批次/遇错停止、schema/prefix 替换、权限和恢复；不增加运行时自动 DDL。首版无同系列前序版本时如实记录不适用，不生成空升级脚本。
- [ ] **MIG-04 SQL 持续变更与升级链回归。** 产物：集成测试中的脚本/结构比较、升级数据 fixture 和维护验证记录。
  验收：设计 10.4 的空库安装、每个仍支持起点的完整升级链及新装/升级结构一致性通过，覆盖约束/索引/排序规则/辅助表/版本与保留数据；默认/定制标识符、错版本、重复执行和部分失败可诊断。测试使用正式脚本，不临时补 DDL；七种服务器及声明的 DM 模式独立记录。新增架构版本时同时新增完整安装与增量脚本，历史字节不改；跨系列保留 MIG-02 证据，同系列无前序版本时单独注明不适用。结果同步第 5.1 节，后续结构变化必须重新验收。
- [ ] **APP-01 完整协议联验。** 产物：`tests/Sharp.OpenIddict.Stores.Server.Tests/`。
  验收：在原生 Manager/Server 路径通过 Client Credentials、Code+PKCE、刷新、撤销及关联失效，覆盖 LIFE-05；通过第 5 节声明组合，不靠手工改库证明协议集成。
- [ ] **APP-02 Sample.Server。** 产物：`samples/Sharp.OpenIddict.Stores.Sample.Server/` 与 `.http`。
  验收：net10、默认 SQLite、配置切换六 provider；先按文档显式建库，原生 Manager 种子初始化 Application/Scope；演示身份/证书标注仅限开发，凭据用环境变量或 user-secrets。
- [ ] **APP-03 Sample.Client。** 产物：`samples/Sharp.OpenIddict.Stores.Sample.Client/`。
  验收：net10，与 Server 实际完成 Client Credentials、Code+PKCE、刷新和撤销，失败结果可诊断，不硬编码生产凭据。
- [ ] **APP-04 使用说明与支持矩阵。** 产物：README、正式设计/开发文档和示例运行说明。
  验收：说明各库驱动/SQL/运行方式、脚本源码与包内路径、版本选择/升级顺序及维护规则、实际验证版本、查询支持与拒绝示例、禁用缓存和旧快照行为、普通删除与 Prune 的区别、升级恢复、生产证书/身份要求；未验证组合显式标记，支持范围与第 5 节一致。

### P8：持续集成与交付候选

前置：目标系列功能、文档和验证可用；CI 可随前面阶段逐步落地，正式候选依赖其全部必需结果。对应设计第 18、19 节。

- [ ] **CI-01 构建、单元、SQLite 与包资产阶段。** 产物：仓库所选 CI 配置及必要的 `scripts/`。
  验收：还原/Release 构建各 TFM，执行单元及 SQLite 合同、公开 API/依赖/消费者检查；执行 MIG-03 的 SQL 清单/摘要、发布历史与包提取一致性检查，验证包可先作为门禁输入打包但不标为发布候选；脚本可重复执行，所有可运行辅助项目保持 net10，失败不能吞掉退出码。
- [ ] **CI-02 真库与协议、升级阶段。** 产物：服务器任务配置与测试结果。
  验收：七种服务器及 DM 声明模式、Manager/Server、MIG-01～04 的安装/升级链/结构一致性/数据保留和并发结果齐备；Oracle/DM 自托管环境使用专用 schema 与 secrets，缺环境时明确阻塞发布门禁，跳过不能当成功。
- [ ] **CI-03 受门禁约束的候选打包。** 产物：目标系列的 nupkg/snupkg 与构建证据。
  验收：只有 CI-01/02 全部成功才产出发布候选，八包同系列同版且精确依赖同版核心/Models，资产、SQL 资源、源链接/符号和许可证可检查；不在此任务中自动发布 NuGet。
- [ ] **REL-01 7.x 正式候选验收。** 产物：7.x 候选及第 8 节发布验收记录。
  验收：该系列全部适用任务、第 5 节数据库矩阵及设计第 19 节需求证据闭合，版本/依赖/五 TFM 正确，无将跳过计为通过的缺口。
- [ ] **REL-02 8.x 系列验收（暂不适用）。** 产物：8.x 稳定版实施后的独立验收记录。
  验收：等 8.x 稳定版发布并立项后，六 Store、七种服务器及 DM 声明模式、协议/升级证据齐备；当前不实施，不产出预览候选。

## 5. 按数据库记录验收

每个单元格分别维护，不能以公共代码完成推断某数据库已经通过。P5 复选框表示对应 provider 的首次完整验收；下表追踪实际结果与驱动/TFM 兼容性。表中版本是计划下限，实际通过的服务器/驱动/宿主版本必须另有运行证据；只测试较新服务器不能证明最低版本可用。

| 服务器/模式 | Provider | 实现与完整合同 | 驱动/TFM 支持 | 证据编号 |
| --- | --- | --- | --- | --- |
| SQL Server 2016+ | SqlServer | 部分通过（16 项真库合同，见 E-026、E-027） | 已实测：SqlClient 7.1.1 经 runtimes/win/lib/net9.0 在 net10 宿主加载 | E-026、E-027 |
| PostgreSQL 14+ | PostgreSql | 未开始 | 待实测 | — |
| MySQL 8.0+ | MySql | 未开始 | 待实测 | — |
| MariaDB 10.6+ | MySql | 未开始 | 待实测 | — |
| SQLite 3.35+ | Sqlite | 部分通过（8 项安装脚本真库用例含自定义前缀，见 E-027） | 已实测：Microsoft.Data.Sqlite 10.0.13 在 net10 宿主加载 | E-027 |
| Oracle 19c+ | Oracle | 未开始 | 驱动仅 net8.0/netstandard2.1，待实测 | — |
| DM8 原生模式 | Dm | 未开始 | 驱动无 net10 资产，待实测 | — |
| DM8 Oracle 兼容模式 | Dm | 未验证，不作支持声明 | 待实测 | — |

“完整合同通过”需要以下检查全部通过，记录中列出结果文件和实际运行版本：

- 全部 Store 成员、Getter/Setter、固定查询、唯一性/外键/可空值与原生 Manager 接入。
- JSON/本地化/JWK、Unicode、长 Payload/LOB、UTC 微秒及各库空串/NULL 差异。
- 四集合索引、长值、大小写/尾空格、摘要碰撞、RequiredScopes 全部匹配。
- 普通字符串的精确查找/批量变更隔离、大小写/重音/尾空格/规范化形式、键/CAS 变体拒绝、物理唯一键限制说明及精确父键验证。
- 有限 IQueryable 支持/拒绝、NULL/Contains 两张真值表与取反、可空日期和布尔组合、Oracle/DM 空串边界、算子顺序、参数上限、稳定分页及逐行读取/提前释放。
- CAS、事务故障回滚、完整普通删除矩阵、关联写入竞态和保留行标记轮换。
- 七方法 Revoke 的状态/计数/标记合同、独立 Prune 谓词/边界/竞态/批次部分提交。
- 该版本 Manager/Server 的查询、撤销后重读及删除后的关联失效测试。

安装和升级结果另列在记录中，不能用 BASE-03/04 的驱动小验证填充完整验收列。若未能取得计划下限环境，记录真实测试版本并保留缺口；调整正式支持下限时同步设计，不能默默改表。

### 5.1 SQL 脚本维护矩阵

独立记录脚本交付与验证，不能由上表的 Store 合同结果推定完成。每格可标注未开始/进行中/通过/失败/阻塞，并关联第 8 节证据；通过时注明实际架构版本和脚本摘要。同系列没有前序架构时，增量子项单独写“不适用：首版无前序架构”，安装仍须实测；以后增加版本时重新打开相应任务和状态。DM Oracle 兼容模式未声明前保持未验证。

| 服务器/模式 | 安装/增量 | 跨系列升级 | 清单/包/执行说明 | 证据编号 |
| --- | --- | --- | --- | --- |
| SQL Server 2016+ | 通过（E-026 默认 schema；E-027 自定义前缀 + schema） | 不适用：首版无前序架构 | 通过（E-026、E-027） | E-026、E-027 |
| PostgreSQL 14+ | 未开始 | 不适用 | 未开始 | — |
| MySQL 8.0+ | 未开始 | 不适用 | 未开始 | — |
| MariaDB 10.6+ | 未开始 | 不适用 | 未开始 | — |
| SQLite 3.35+ | 通过（E-026 默认前缀；E-027 自定义前缀） | 不适用：首版无前序架构 | 通过（E-026、E-027） | E-026、E-027 |
| Oracle 19c+ | 未开始 | 不适用 | 未开始 | — |
| DM8 原生模式 | 未开始 | 不适用 | 未开始 | — |
| DM8 Oracle 兼容模式 | 未验证，不作支持声明 | 不适用 | 未验证 | — |

证据另列脚本路径、清单/SQL SHA-256、源/目标 Series/Version、执行工具/服务器/模式、schema/prefix、安装与各段升级结果、结构比较和保留数据断言。结构或索引变更的完成记录必须同时覆盖受影响 provider 的新装与升级；只改代码或只补一份最新建表 SQL 不能关闭任务。

## 6. 需求到开发任务的追踪

| 需求/设计章节 | 实施与验收任务 |
| --- | --- |
| Sharp 包名、八包结构，第 4/14 节 | ENG-01、CI-01/03、REL-01/02 |
| 依赖中央管理与逐 TFM 分线，第 4.1/4.1.1 节 | BASE-01、ENG-01、COMPAT-01/02、CI-01/03 |
| 7.7.2 基线与五 TFM，第 2/3/18 节 | BASE-01/02、COMPAT-01～04 |
| 六类数据库、七种服务器，第 5 节 | BASE-03/04、DB-01～07、第 5 节矩阵 |
| 原生 DI/Manager 与缓存，第 6/14 节 | ENG-03/04、LIFE-05、APP-01 |
| 完整实体/Store，第 7/14/15 节 | ENG-02、STORE-01～06、QUERY-01～03、LIFE-01～05 |
| 集合原生语义与无内存回退，第 7/9/16.1 节 | STORE-05、QUERY-01～03 |
| 普通字符串精确比较，第 16.1.1 节 | BASE-03/04、ENG-04、STORE-03/04/06、QUERY-01/02、LIFE-01～04、DB-01～07 |
| 查询 NULL 合同，第 9.1 节 | QUERY-01/02、DB-01～07 |
| 事务、CAS、删除/撤销/Prune，第 8/16 节 | STORE-06、LIFE-01～05、DB-01～07 |
| 显式 SQL 与升级，第 10/17 节 | STORE-01、DB-01～07、MIG-01/02 |
| 各库 SQL 版本与持续维护，第 10/17/18 节 | STORE-01、DB-01～07、MIG-03/04、CI-01～03、第 5.1 节矩阵 |
| net10 测试/示例、slnx，第 11/12/18 节 | ENG-01/05、COMPAT-03、APP-01～04 |
| 可验证的发布候选，第 18/19 节 | CI-01～03、REL-01/02 |

## 7. 开发与验证入口

以下命令是工程建立后的常规入口。所有命令在仓库根目录使用 .NET 10 SDK；数据库连接凭据通过环境配置提供，不写入命令记录或仓库。测试框架/runner 已在 BASE-01 锁定，命令语法变化时应同步本节。

```powershell
dotnet --info
dotnet restore Sharp.OpenIddict.Stores.slnx
dotnet build Sharp.OpenIddict.Stores.slnx --configuration Release --no-restore
dotnet test tests/Sharp.OpenIddict.Stores.Tests/Sharp.OpenIddict.Stores.Tests.csproj --configuration Release --no-build
dotnet test tests/Sharp.OpenIddict.Stores.IntegrationTests/Sharp.OpenIddict.Stores.IntegrationTests.csproj --configuration Release --no-build
dotnet test tests/Sharp.OpenIddict.Stores.Server.Tests/Sharp.OpenIddict.Stores.Server.Tests.csproj --configuration Release --no-build
git diff --check
```

Release 构建必须实际覆盖八个类库的五种 TFM，另执行 COMPAT-02/03 的资产/消费者验证。集成测试未配置部分数据库时，即使命令退出成功，也要读取结果中的跳过项并记录缺口；不能据此勾选整个数据库阶段。CI 应将必需数据库缺失作为发布门禁失败。

CI-03 在前置门禁全部通过后执行打包，例如：

```powershell
dotnet pack Sharp.OpenIddict.Stores.slnx --configuration Release --no-build --output artifacts/packages -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg
```

本地为检查包资产而打包时，应标注“本地验证产物”；它不自动取得发布候选资格。数据库筛选参数、秘密变量名和可复现消费者命令随 ENG-05/COMPAT-03/CI 实现补充，尚未实现时不提供虚构命令。

## 8. 完成记录与阻塞记录

每条记录使用稳定的证据编号，例如 `E-001`。运行日志/TRX/CI 链接可作为附件证据，不在本文复制整段输出。环境中只记录版本、模式和专用 schema 的逻辑说明，禁止写入连接串、令牌或密钥。

### E-001：已提交的设计基线

- 任务：DOC-01、DOC-02；系列：7.7.2 设计。
- 日期：2026-10-19；提交：`982c156`。
- 产物：`docs/design.md` v1.2（当前已修订为 v1.9，见后续记录）和原 README 入口。
- 验证：设计记录了上游标签、Store/Manager/EF Core 源码依据及走查后确定的合同；该记录证明文档基线已提交。
- 结果与缺口：设计编写/修订完成，状态仍为评审稿；不含代码、协议或数据库运行通过结论。

### E-002：开发与进度文档

- 任务：DOC-03；系列：7.7.2 开发计划。
- 日期：2026-10-19；提交：本次未提交的文档工作区，提交后补充真实提交号。
- 产物：`docs/development.md`、README 文档入口及设计维护入口。
- 验证：与设计 v1.2 对照任务/需求覆盖；`git diff --check` 检查已跟踪文件，`git diff --no-index --check -- NUL docs/development.md` 检查新增文件，均无空白错误；一次性文档自查通过编号唯一、本地链接、Markdown 表格/代码块和行尾检查。
- 结果与缺口：共 48 项任务，3 项文档工作已完成、45 项后续任务未完成；所有实现及真库验证仍为未开始。新增文件的 no-index 检查退出码 1 表示存在文件差异，不代表测试通过或失败。

### E-003：走查三项问题的文档修订

- 任务：DOC-02、DOC-03；系列：7.7.2 设计/开发计划。
- 日期：2026-10-19；提交：本次未提交的文档工作区。
- 产物：设计 v1.3 第 9.1/16.1.1 节、相关测试/撤销合同及追踪；开发 v1.1 的 P3/P4 范围、任务依赖、验收条件和矩阵。
- 验证：对照三个走查问题检查正文与任务追踪；`git diff --check` 及新增文件的 no-index 空白检查通过，文档自查覆盖编号、链接、版本引用和 Markdown 结构；PowerShell Add-Type 的 C# 真值表检查通过 91 组值组合，Python sqlite3 内存表达式检查通过 95 例等值/Contains/可空日期取反。这些检查不作为产品实现或真实驱动测试。
- 结果与缺口：普通字符串比较和查询 NULL 规则明确；P3 不再以前置实现 LIFE-01 为完成条件，完整删除归 LIFE-01/02；仍为 48 项任务、3 项文档完成、45 项未完成。未新增或验证产品实现，真库状态不变。

### E-004：数据库 SQL 脚本持续维护约定

- 任务：DOC-02、DOC-03；系列：7.7.2 设计/开发计划。
- 日期：2026-10-19；提交：本次未提交的文档工作区。
- 产物：设计 v1.4 第 10 节及测试/版本/CI/需求追踪；开发 v1.2 的 MIG-03/04、第 5.1 节矩阵和关联任务；README。
- 验证：一次性 Python 文档静态检查通过版本引用、任务编号/引用、链接、表格/代码块、编码/行尾与空白检查；`git diff --check` 和新增文件的 `git diff --no-index --check -- NUL docs/development.md` 无空白错误。核对脚本唯一来源、发布历史不可变、逐库差异、升级链、包交付和任务依赖的对应关系。此记录不作为 SQL 执行或真库验收证据。
- 结果与缺口：共 50 项任务，3 项文档完成、47 项实现与验证任务未完成；新增脚本维护任务与矩阵均未开始。尚未生成或执行任何数据库 SQL，也未构建包。

### E-005：依赖锁定与中央包管理（8.x 预览骨架部分已被 E-006 取代）

- 任务：BASE-01、ENG-01。
- 日期：2026-10-19；实现：本次未提交的工作区。
- 产物：`global.json`、`Directory.Build.props`、`Directory.Packages.props`、`.gitattributes`、`Sharp.OpenIddict.Dapper.slnx`、8 个 `src/` 工程、3 个 `tests/` 工程、2 个 `samples/` 工程、`tests/Sharp.OpenIddict.Dapper.Tests/DependencyConventionTests.cs`。
- 验证命令与结果：`dotnet restore Sharp.OpenIddict.Dapper.slnx --force --no-cache` 成功（13 工程）；`dotnet build ... --configuration Release` 0 警告 0 错误；`dotnet test tests/Sharp.OpenIddict.Dapper.Tests` 通过 3 项（中央包管理约束、产品工程不引驱动、版本集中在清单）。
- SDK/宿主：SDK 10.0.402；当时全部工程 net10.0。
- 已核实的上游资产：`OpenIddict.Core` 7.7.2 有 netstandard2.0/2.1、net8.0、net9.0、net10.0、net46x/47x/48，且其依赖组本身按代际分线。
- 完成条件结论：BASE-01 的版本来源、TFM 与许可证已记录（全部 Apache-2.0/MIT/PostgreSQL，无禁止项）；ENG-01 的布局、引用方向与打包边界已通过还原/构建/打包验证。
- 历史缺口与后续修订：当时基线为 `8.0.0-preview.4`、扩展版本 `8.0.0-preview.1`、全部工程 `net10.0`，且 7.x 五目标分线仅写入文档。该选择违反“不依赖预览包”，已由 E-006 整体取代；本节保留为历史记录，不再代表当前状态。

### E-006：改用稳定版 7.7.2 基线并落地五目标分线

- 任务：BASE-01、ENG-01、ENG-02（模型部分）、COMPAT-01；系列：7.7.2。
- 日期：2026-10-19；实现：本次未提交的工作区。
- 变更：`Directory.Packages.props` 中五个 OpenIddict 包由 `8.0.0-preview.4.26456.66` 改为 `7.7.2`，Microsoft.Extensions.* 拆为按 TFM 的条件组（ns2.0/ns2.1/net8.0 → 8.0.2；net9.0 → 9.0.21；net10.0 → 10.0.13），`System.Security.Cryptography.Pkcs` 同线锁定（8.0.1 / 9.0.21 / 10.0.13）；`Directory.Build.props` 版本改为 `7.0.0` 并移除 `VersionSuffix`；8 个 `src/` 工程改为 `netstandard2.0;netstandard2.1;net8.0;net9.0;net10.0`；删除 7.7.2 不存在的 `OpenIddictDapperResource`、`OpenIddictDapperSession` 模型与 `OpenIddictDapperToken.SessionId` 属性。
- 验证命令与结果：`dotnet build Sharp.OpenIddict.Dapper.slnx -c Release` 成功，0 警告 0 错误（8 个产品工程各产出 5 个 TFM 资产）；`dotnet test Sharp.OpenIddict.Dapper.slnx -c Release` 通过 5 项；`dotnet pack src/Sharp.OpenIddict.Dapper` 产出 `7.0.0`，nuspec 依赖组实测为 `.NETStandard2.0`/`.NETStandard2.1`/`net8.0` → Microsoft.Extensions 8.0.2、`net9.0` → 9.0.21、`net10.0` → 10.0.13，OpenIddict 依赖统一为 7.7.2，五组均不含 ADO.NET 驱动。
- 新增检查：`CentralPackageManagement_UsesOnlyStableVersions` 断言中央清单无预发布版本；`ProductProjects_TargetFiveFrameworks` 断言 8 个产品工程均为五目标。
- 修复的构建错误：把 `Microsoft.Extensions.DependencyInjection` 登记在全局组时，传递依赖提升会把 10.0.13 注入低 TFM 并触发 `NU1109` 降级错误（12 处）；改为不提升后五组依赖完全对称。
- 合同事实：7.7.2 只有 4 个 Store（Application 42、Authorization 32、Scope 28、Token 43 个成员，合计 145），`CountAsync` 无状态参数重载，Authorization/Token 的 `FindAsync` 为位置字符串参数，无 Resource/Session/`Token.SessionId`。
- 剩余缺口：BASE-02 合同清单文档尚未产出；Store、Builder、连接、方言等实现仍为空骨架，不代表任何 Store 功能可用；Oracle/DM 真库验证未开始。8.x 主线因无稳定版而暂不实施。
- 文档同步：设计 v1.7 与本文 v1.5 已按 7.7.2 收敛为四类实体、四类 Store、单系列多目标验证；ENG-02/STORE/QUERY/LIFE/MIG/APP/CI/REL 的验收条件中不再包含 Session/Resource 或 7→8 升级，MIG-02 标注“暂不适用”。

### E-007：7.7.2 Store 合同清单与冻结用例

- 任务：BASE-02；系列：7.7.2。
- 日期：2026-10-19；实现：本次未提交的工作区。
- 产物：`docs/contracts/7x-store-contracts.md`（四接口 145 个成员的逐行签名清单、8.x 差异说明、本项目附加合同）、`tests/Sharp.OpenIddict.Dapper.Tests/StoreContractTests.cs`（8 项断言）。
- 来源：对 `%USERPROFILE%\.nuget\packages\openiddict.abstractions\7.7.2\lib\net10.0\OpenIddict.Abstractions.dll` 反射导出全部成员签名；行为依据取自上游标签 `7.7.2`（`950cb13b`）的 EF Core Store：`PruneAsync` 的 `CreationDate < threshold` 与三选一条件、`1_000` 批上限、`RepeatableRead` 事务；`RevokeBy*` 的 `Status != Revoked` 只出现在 Token 的三个方法。
- 验证命令与结果：`dotnet test tests/Sharp.OpenIddict.Dapper.Tests -c Release` 通过 13 项（原 5 项工程约束 + 8 项合同断言），0 警告 0 错误。
- 合同事实：Application 42、Authorization 32、Scope 28、Token 43，合计 145；`CountAsync` 只有 1 参数与 2 参数两个重载；Authorization 的 `FindAsync` 为 `(subject, client, status, type, scopes, token)`，Token 为 `(subject, client, status, type, token)`；`RevokeByApplicationIdAsync`/`RevokeBySubjectAsync` 的 `CancellationToken` 带 `= default`。
- 覆盖方式：成员数量与总数、无 Resource/Session 接口、`CountAsync` 无 `TState`、`FindAsync` 位置参数、Scope 以 string 表达 resource、可选取消参数，以及文档与反射逐成员比对。
- 结果与缺口：清单已冻结，Store 实现仍为空白，因此清单中每一行状态都是“未实现”；Oracle/DM 真库验证与其余实现任务未开始。

### E-008：四类默认模型与列映射

- 任务：ENG-02；系列：7.7.2。
- 日期：2026-10-19；实现：本次未提交的工作区。
- 产物：`src/Sharp.OpenIddict.Dapper.Models/`（四实体，18/9/9/13 个属性）及 `tests/Sharp.OpenIddict.Dapper.Tests/ModelContractTests.cs`（6 项断言）。
- 验证命令与结果：`dotnet test tests/Sharp.OpenIddict.Dapper.Tests -c Release` 通过 19 项，0 警告 0 错误；`dotnet build Sharp.OpenIddict.Dapper.slnx -c Release` 0 警告 0 错误。
- 覆盖方式：属性集合与设计 15.2 逐列相等（Applications 14 列、Authorizations 7、Scopes 7、Tokens 11，另加 Id/ConcurrencyToken）；每个原生 Get/Set 访问器都有同名列（数量等于 2×业务列+1）；实体公开、非 sealed、属性全为 virtual；属性类型只有 `string` 与 `DateTime?`，无导航属性；`Id`/`ConcurrencyToken` 默认新 GUID；Models 程序集只导出这四个类型。
- 结果与缺口：模型与列映射已冻结，但转换器、Store 与安装脚本仍未实现，因此“模型有列”不代表任何 Store 功能可用；第 15.5 节物理类型未在真实数据库上验证。
- 文档同步：设计 15.2 补充“实体属性名即列名、无第二套映射元数据、新增属性须同时改本节/Store SQL/安装脚本、`ConcurrencyToken` 无原生访问器”。

### E-009：Builder、选项校验与原生 DI 注册（ENG-03 部分完成）

- 任务：ENG-03（部分）；系列：7.7.2。
- 日期：2026-10-19；实现：本次未提交的工作区。
- 产物：`src/Sharp.OpenIddict.Dapper/` 的 `OpenIddictDapperProvider.cs`、`IOpenIddictDapperConnectionFactory.cs`、`OpenIddictDapperOptions.cs`、`OpenIddictDapperBuilder.cs`、`OpenIddictDapperCoreBuilderExtensions.cs`；`tests/Sharp.OpenIddict.Dapper.Tests/RegistrationTests.cs`（11 项断言）。
- 验证命令与结果：`dotnet build src/Sharp.OpenIddict.Dapper -c Release` 五个 TFM 全部成功，0 警告 0 错误；`dotnet test tests/Sharp.OpenIddict.Dapper.Tests -c Release` 通过 30 项。
- 已实现：`UseDapper()` 与 `UseDapper(Action<OpenIddictDapperBuilder>)`；`UseSqlServer/UsePostgreSql/UseMySql/UseSqlite/UseOracle/UseDm`；`UseConnectionFactory(Func<IServiceProvider, DbConnection>)` 与 `UseConnectionFactory<TFactory>()`；`SetSchema/SetTablePrefix/SetCommandTimeout`；四类默认实体注册；连接工厂 Scoped；`DisableEntityCaching()`。
- 已拒绝的配置（均在使用前抛 `OptionsValidationException`）：未选 provider、多选 provider、缺连接工厂、委托与类型同时配置、工厂类型未实现接口、空/非法表前缀、非正超时、非法 schema、SQLite/MySQL 配置 schema、以及最终生效的实体缓存被重新启用。
- 关键取舍：缓存校验实现为 `IValidateOptions<OpenIddictCoreOptions>`，因此判定依据是**最终生效值**而非调用顺序；实测“UseDapper 后关闭禁用”和“禁用→UseDapper→重新启用”两种顺序都被拒绝，先禁用再 UseDapper 则通过。
- 剩余缺口：Store 与方言注册未实现（依赖 STORE-01/03/04）。ENG-03 因此保持未勾选——现在调用 `UseDapper` 能通过配置校验并注册实体，但解析管理器时仍会因缺少 Store 实现而失败，不能宣称 DI 接入可用。
- 交叉验证：`SetDefault*Entity` 以工厂委托替换原生 `IOpenIddict*Manager`，测试按该原生形状断言；`DisableAdditionalFiltering` 未被本扩展改动。

### E-010：SQLite 方言与 v0001 安装脚本

- 任务：STORE-01；系列：7.7.2 / 架构 Series 7 Version 1。
- 日期：2026-10-19；实现：本次未提交的工作区。
- 产物：`src/Sharp.OpenIddict.Dapper.Sqlite/Sql/7/install/v0001.sql`、`Sql/manifest.json`、`Sql/README.md`、`OpenIddictDapperSqliteDialect.cs`、`OpenIddictDapperSqliteBuilderExtensions.cs`；`tests/Sharp.OpenIddict.Dapper.IntegrationTests/SqliteInstallScriptTests.cs`（8 项）、`SqlAssetTests.cs`（19 项，含六 provider 的参数化检查）。
- 验证命令与结果：`dotnet test tests/Sharp.OpenIddict.Dapper.IntegrationTests -c Release` 通过 27 项；`dotnet pack src/Sharp.OpenIddict.Dapper.Sqlite -c Release` 产出 `7.0.0`，包内实测含五个 TFM 的 `lib/` 资产与 `sql/7/install/v0001.sql`、`sql/manifest.json`、`sql/README.md`。
- 真库证据：在 `Microsoft.Data.Sqlite` 临时文件库上执行随包脚本（不是测试内临时 DDL），断言九张表（四主表 + 四辅助表 + SchemaVersions）、`Series=7/Version=1` 单行、外键拒绝孤儿 Authorization、ClientId 唯一冲突、`client` 与 `client ` 并存（BINARY 排序规则区分尾随空格）、关键索引存在、重复安装失败。
- 脚本约定：UTF-8 无 BOM、LF；`__PREFIX__` 占位符由 `ApplyPrefix` 按运行时同一规则替换，非法前缀在生成部署副本前抛出；`SplitStatements` 忽略整行注释。
- 方言合同：`Provider=Sqlite`、`@` 参数前缀、`MaximumParameters=900`、双引号标识符引用、`BuildExactStringEquals` 直接用 `=`（SQLite TEXT 为 BINARY，本身区分大小写/重音/尾随空格）、分页无 count 时 `LIMIT -1 OFFSET n`、UTC 文本固定格式 `yyyy-MM-dd HH:mm:ss.ffffff`。
- 结果与缺口：STORE-01 的“SQLite 方言与安装 SQL”范围完成；架构版本检查的运行时读取（首次使用拒绝错版本）尚未接入 Store，归 STORE-03 与 MIG-01；其余五个 provider 的脚本与方言未开始。
- 已知限制（写入 Sqlite README）：`TEXT(n)` 长度在 SQLite 不强制；摘要列长度由 Store 保证；外键依赖连接上的 `PRAGMA foreign_keys = ON`。

### E-011：JSON、字典与 UTC 转换

- 任务：STORE-02（部分）；系列：7.7.2。
- 日期：2026-10-19；实现：本次未提交的工作区。
- 产物：`src/Sharp.OpenIddict.Dapper/OpenIddictDapperJson.cs`、`tests/Sharp.OpenIddict.Dapper.Tests/JsonConversionTests.cs`（12 项）。
- 验证命令与结果：`dotnet test tests/Sharp.OpenIddict.Dapper.Tests -c Release` 通过 42 项。
- 覆盖方式：字符串集合按 Ordinal 往返（`api`/`api `/`Api`/`ápi` 互不合并）；null/空串/空白/`[]` 都读成空集合且空集合序列化为 null；非法 JSON 对四类转换器都抛 `InvalidOperationException`；本地化字典往返 culture 名，空键映射到不变文化，值类型错误抛数据异常；属性字典保留 `JsonElement` 类型；设置字典保留大小写不同的键；UTC 规范化截断到 10 tick 且保持 `Utc` 种类，`Unspecified` 按 UTC 处理；SQLite 日期文本往返保持微秒且固定宽度可直接排序。
- 结果与缺口：转换器可用，但 JWK 集合（`JsonWebKeySet`）转换与全部 Getter/Setter 尚未实现，长 Payload/Unicode 无截断缺少真库断言，因此 STORE-02 保持未勾选。


### E-013：Scope Store 与资源索引

- 任务：STORE-03（Scope 部分）、STORE-02（Scope 侧 Getter/Setter）；系列：7.7.2。
- 日期：2026-10-19；实现：本次未提交的工作区。
- 产物：`src/Sharp.OpenIddict.Dapper/Stores/OpenIddictDapperScopeStore.cs`、DI 注册（`IOpenIddictScopeStore<OpenIddictDapperScope>`）、`tests/Sharp.OpenIddict.Dapper.IntegrationTests/SqliteScopeStoreTests.cs`（12 项）、`ManagerIntegrationTests.cs` 增加 2 项 Scope 用例。
- 验证命令与结果：`dotnet build Sharp.OpenIddict.Dapper.slnx -c Release` 0 警告 0 错误；`dotnet test Sharp.OpenIddict.Dapper.slnx -c Release` 通过 101 项（42 单元 + 59 集成）。
- 真库证据（SQLite 临时文件 + 随包脚本）：Name/Description/DisplayName/Properties 与本地化字典、Resources 往返；空集合读回空；Name 精确比较（`api`/`Api`/`api ` 互不命中）与空键拒绝；Name 唯一索引冲突；`FindByResourceAsync` 走 ScopeResources 摘要候选 + JSON 完整值 Ordinal 复核（`https://rs` 与 `https://rs ` 并存时各自只命中一条）；`FindByNamesAsync` 空列表/默认列表返回空、精确匹配、大小写与尾空格变体不命中；Update 轮换标记、旧快照抛原生 `ConcurrencyException`、资源索引重建；Delete 只删自身与 ScopeResources 行，不影响其他 Scope；删除的 CAS 拒绝；Count/List 分页与负参数；查询回调明确拒绝；Instantiate 生成 GUID。
- Manager 证据：原生泛型与非泛型 `OpenIddictScopeManager` 均可创建、查找、计数与按资源名查找，精确比较在 Manager 路径同样生效。
- 关键取舍：`FindByNamesAsync` 用参数化 IN 并逐项生成参数名，不做字符串拼接；`Resources` 的权威数据仍是 JSON，辅助表只做候选筛选；查询回调保持明确拒绝，不引入未完成的翻译器。
- 结果与缺口：STORE-03 的两类 Store 均已完成；`FindByNamesAsync` 的分块（超过方言参数上限）归 QUERY-01 的参数上限处理，此处仅按请求规模生成参数，未做分块；Authorization/Token Store（STORE-04）与其余五种方言仍未实现。
### E-014：Authorization 与 Token Store

- 任务：STORE-04、ENG-03（Store 注册收尾）；系列：7.7.2。
- 日期：2026-10-19；实现：本次未提交的工作区。
- 产物：`src/Sharp.OpenIddict.Dapper/Stores/OpenIddictDapperAuthorizationStore.cs`、`OpenIddictDapperTokenStore.cs`、`OpenIddictDapperDateParameter.cs`、方言接口新增 `FormatDate`/`ReadDate`；四类 Store 全部注册进 DI；`tests/Sharp.OpenIddict.Dapper.IntegrationTests/SqliteAuthorizationStoreTests.cs`（9 项）、`SqliteTokenStoreTests.cs`（11 项）、`ManagerIntegrationTests.cs` 增至 9 项。
- 验证命令与结果：`dotnet build Sharp.OpenIddict.Dapper.slnx -c Release` 0 警告 0 错误；`dotnet test Sharp.OpenIddict.Dapper.slnx -c Release` 通过 124 项（42 单元 + 82 集成）。
- 真库证据（SQLite 临时文件 + 随包脚本）：
  - Authorization：全部标量列与本地化集合往返；`FindAsync` 四类可选筛选单独与组合生效、null/空串不筛选、大小写与尾空格变体不命中；RequiredScopes 必须全部包含（请求 `api`+`offline_access` 只命中同时具备者）、重复值去重、空集合不限制、`API`/`api ` 不命中；`FindByApplicationIdAsync`/`FindBySubjectAsync` 精确且拒绝空键；Update 轮换标记、重建 AuthorizationScopes、旧快照抛原生 `ConcurrencyException`；Delete 连带删除其全部 Token（含只绑 Authorization 的 Token）、AuthorizationScopes 行，Application 保留；删除 CAS 拒绝。
  - Token：全部标量列往返；三处日期（Creation/Expiration/Redemption）保留微秒并按 UTC 返回（Offset 为 0）；可空外键与日期保持 null；`FindAsync` 可选筛选与精确比较（`Alice`/`alice `/`revoked `/`ACCESS_TOKEN` 均不命中）；`FindByApplicationIdAsync`/`FindByAuthorizationIdAsync`/`FindBySubjectAsync`/`FindByReferenceIdAsync` 精确且拒绝空键；Update 轮换标记、旧快照抛原生并发异常；Delete 只删目标行；ReferenceId 唯一索引冲突。
- Manager 证据：`AllFourStores_AreResolvableFromContainer` 断言四个 Store 均可解析且类型正确；原生 `OpenIddictTokenManager` 与 `OpenIddictAuthorizationManager` 均可创建、计数、按 Id/Subject 查找。Manager 会把 TokenType 规范化为完整 URN，测试断言 `TokenTypeIdentifiers.AccessToken` 而非 `access_token`。
- 关键取舍：日期参数与读取统一走方言（`FormatDate`/`ReadDate`），SQLite 用固定格式文本，其他 provider 后续实现各自的转换，Store 不直接依赖驱动；未实现成员一律显式 `NotSupportedException`，新增 `RevokeNotImplemented`/`PruneNotImplemented` 两个错误工厂，杜绝“返回 0 假装没命中”。
- 结果与缺口：STORE-04 完成。批量撤销（LIFE-03）、Prune（LIFE-04）、关联删除竞态（LIFE-01/02）、查询回调（QUERY-01～03）仍为显式拒绝；其余五种方言未实现。
### E-015：集合索引的摘要候选语义与 CAS 原子性

- 任务：STORE-05、STORE-06；系列：7.7.2。
- 日期：2026-10-19；实现：本次未提交的工作区。
- 产物：`Stores/OpenIddictDapperAuthorizationStore.cs` 的 RequiredScopes 改为索引驱动并按方言参数上限分块求交集；`tests/Sharp.OpenIddict.Dapper.IntegrationTests/CollectionIndexContractTests.cs`（8 项）。
- 验证命令与结果：`dotnet build Sharp.OpenIddict.Dapper.slnx -c Release` 0 警告 0 错误；`dotnet test Sharp.OpenIddict.Dapper.slnx -c Release` 通过 132 项（42 单元 + 90 集成）。
- 走查发现的真实缺陷（已修）：`FindAsync` 的 RequiredScopes 原先流式扫描整张 Authorization 表后在内存过滤，既不是索引候选筛选，也随表增长退化。现在先按 `ScopeHash IN (...)` 求候选 Id、跨分块取交集，再按 Id 分批取实体并用权威 JSON 做 Ordinal 全包含复核。内存中只保留标识符字符串，不保留实体。
- 真库证据：
  - **强制摘要碰撞**：把 `other` 的索引行改写为 `https://real/cb` 的摘要，同时其 JSON 仍是 `https://other/cb`；查找只返回真正含该值的实体，证明摘要不定义身份。
  - **超长值**：400+ 字符 URI 往返无截断，索引查找命中且回读一致。
  - **重复值**：JSON 保留调用方顺序与重复项，索引表按 Ordinal 去重只存一行。
  - **索引重建**：Scope 资源更新后旧值不再命中、新值命中，辅助表只剩一行。
  - **分块交集**：1200 个 Scope 超过 SQLite 参数上限，按 900 分块；请求全集只命中具备全部 Scope 的那一条，少要一个 Scope 时两条都命中。
  - **CAS 变体**：并发标记的大小写与尾空格变体都不能命中，原值仍可删除。
  - **失败回滚**：旧快照更新时连同新资源一并失败，JSON、索引表、显示名均保持胜者写入的状态，没有半提交。
  - **取消回滚**：以已取消的令牌调用 Update，JSON 与索引表都保持旧值。
- 关键取舍：分块大小取 `MaximumParameters - 已用参数数`，不硬编码 900，方言换库时自动适配；交集只保留 Id 字符串，避免把候选实体整体载入内存。
- 结果与缺口：STORE-05、STORE-06 完成。查询回调（QUERY-01～03）、批量撤销（LIFE-03）、Prune（LIFE-04）仍显式拒绝；Application/Authorization 的完整关联删除竞态归 LIFE-01/02。
### E-016：七种批量撤销

- 任务：LIFE-03；系列：7.7.2。
- 日期：2026-10-19；实现：本次未提交的工作区。
- 产物：`IOpenIddictDapperDialect.BuildExactStringNotEqualsIncludingNull`（含 NULL 的精确不等谓词）、`OpenIddictDapperBase.RevokeCoreAsync`（单条条件 UPDATE）、`OpenIddictDapperStatuses`；Token Store 四个方法与 Authorization Store 三个方法的真实实现；`tests/Sharp.OpenIddict.Dapper.IntegrationTests/BulkRevocationContractTests.cs`（15 项）、`ManagerIntegrationTests.cs` 增加 2 项。
- 验证命令与结果：`dotnet build Sharp.OpenIddict.Dapper.slnx -c Release` 0 警告 0 错误；`dotnet test Sharp.OpenIddict.Dapper.slnx -c Release` 通过 149 项（42 单元 + 107 集成）。
- 逐方法合同（设计 16.2.2）全部有对应断言：
  - `Token.RevokeBySubjectAsync`：valid 1 → 0；已 revoked 行被排除，其标记不变。
  - `Token.RevokeAsync`（无筛选）：同数据两次都返回 2，两行标记各自再轮换。
  - `Token.RevokeAsync(status=valid)`：1 → 0。
  - `Authorization.RevokeBySubjectAsync`：同数据两次都返回 2，两行标记各自再轮换。
  - `Token.RevokeByApplicationIdAsync`/`RevokeByAuthorizationIdAsync`：只命中目标外键，同 Subject 的其他行与无外键行不变。
  - `Authorization.RevokeByApplicationIdAsync`：不排除已撤销行。
- NULL 与精确比较证据：NULL 状态行在 `Token.RevokeBy*` 与无筛选通用撤销中被命中，在显式 `status=valid` 中不被命中；指定键不匹配时 NULL 行始终不受影响，证明 NULL 分支仍受其他谓词约束；`alice`/`Alice`/`alice ` 三者互不串扰；`revoked ` 不被当作已撤销，撤销后才被排除；Status/Type 的大小写与尾空格变体都不命中。
- 并发保证证据：命中行标记轮换、未命中行标记不变；用撤销前的旧标记构造快照调用 `UpdateAsync` 会抛原生 `ConcurrencyException`，说明撤销结果不会被旧快照覆盖。
- Manager 路径证据：原生 `OpenIddictTokenManager.RevokeBySubjectAsync` 首次 1、再次 0；在**新作用域**重新查询，两行状态均为 `revoked`，证明写入确实落到数据库而非缓存；`OpenIddictAuthorizationManager.RevokeBySubjectAsync` 两次都是 2。
- 关键取舍：撤销用单条条件 UPDATE，不加载实体；命中行共用一个新 GUID 轮换标记（设计允许跨行重复，不要求唯一）；NULL 处理下沉到方言 `BuildExactStringNotEqualsIncludingNull`，避免各 Store 各写一份 `IS NULL OR <>`；`OpenIddictDapperErrors.RevokeNotImplemented` 已删除，不再有“撤销未实现”的出口。
- 结果与缺口：LIFE-03 完成。Prune（LIFE-04）仍显式拒绝；Application/Authorization 的完整关联删除竞态（LIFE-01/02）与查询回调（QUERY-01～03）未开始。
### E-017：独立 Prune 清理路径

- 任务：LIFE-04；系列：7.7.2。
- 日期：2026-10-19；实现：本次未提交的工作区。
- 产物：`OpenIddictDapperBase.PruneCoreAsync`（批次循环与独立提交）、`OpenIddictDapperStatuses` 补全 `Valid`/`Inactive`/`AdHoc`、Token 与 Authorization 的 `BuildPruneSql`；`tests/Sharp.OpenIddict.Dapper.IntegrationTests/PruneContractTests.cs`（18 项）、`ManagerIntegrationTests.cs` 增加 1 项。
- 验证命令与结果：`dotnet build Sharp.OpenIddict.Dapper.slnx -c Release` 0 警告 0 错误；`dotnet test Sharp.OpenIddict.Dapper.slnx -c Release` 通过 168 项（42 单元 + 126 集成）。
- 条件实现（设计 16.3）：
  - **Token**：`CreationDate` 非 NULL 且严格早于阈值，且满足三者之一——状态既非 `inactive` 也非 `valid`；确有关联 Authorization 且其状态非 `valid`；`ExpirationDate` 严格早于捕获的 UTC now。
  - **Authorization**：`CreationDate` 非 NULL 且严格早于阈值，状态非 `valid` 或类型为 `ad-hoc`，且 `NOT EXISTS` 关联 Token。
- 真库证据：
  - 非 valid 状态被清理、`valid` 与 `inactive` 被保留；NULL 状态视为“非 valid”被清理（用 `<>` 会静默丢掉）。
  - `Valid`/`valid ` 被当作非 valid 清理，`inactive ` 同样清理——精确比较在清理路径同样生效。
  - 阈值边界：`CreationDate == threshold` 保留，`threshold - 1 tick` 清理；`CreationDate` 为 NULL 永不清理。
  - 关联授权状态非 valid 时，状态为 valid 的 Token 仍被清理；授权 valid 时 Token 保留。
  - 已过期 Token 清理，未过期保留。
  - 1,200 条候选一次调用内按 500/批分三批全部清理，返回值 1200。
  - Authorization 的 `ad-hoc` 在 valid 状态下仍被清理；`Ad-Hoc`/`ad-hoc ` 不匹配。
  - 有 Token 关联的 Authorization 不被清理，其 Token 也不受影响（证明独立路径不级联）；Token 经普通删除后该 Authorization 才可清理。
  - 清理幂等，无可清理项时返回 0。
  - 已取消令牌调用抛 `OperationCanceledException` 且未删除任何行。
- Manager 证据：原生 `OpenIddictTokenManager.PruneAsync` 只清理非 valid 的那条，新作用域重新枚举确认仅剩 `valid`。
- 关键取舍：批次与上限作为 `PruneBatchSize`/`PruneBatchCount` 常量放在基类，两种 Store 共用同一循环；每批独立事务，失败只回滚该批并把异常原样抛出，不吞异常也不返回部分成功；`NOT EXISTS` 写在最终 DELETE 内而非先查后删，避免检查与删除之间新出现的 Token 被误删；`now` 在方法入口捕获一次，避免批次间漂移。
- 结果与缺口：LIFE-04 完成，`OpenIddictDapperErrors.PruneNotImplemented` 已删除。查询回调（QUERY-01～03）与 Application/Authorization 的完整关联删除竞态（LIFE-01/02）仍未开始；LIFE-05 的 Manager/Server 协议回归依赖前述任务。
### E-018：关联删除矩阵与删除竞态

- 任务：LIFE-01、LIFE-02；系列：7.7.2。
- 日期：2026-10-19；实现：本次未提交的工作区。
- 产物：`Stores/OpenIddictDapperApplicationStore.DeleteAsync` 与 `OpenIddictDapperAuthorizationStore.DeleteAsync` 增加根记录写入锁并按设计 16.2.1 的四步顺序执行；`tests/Sharp.OpenIddict.Dapper.IntegrationTests/DeleteAssociationContractTests.cs`（13 项）、`ManagerIntegrationTests.cs` 增加 1 项。
- 验证命令与结果：`dotnet build Sharp.OpenIddict.Dapper.slnx -c Release` 0 警告 0 错误；`dotnet test Sharp.OpenIddict.Dapper.slnx -c Release` 通过 182 项（42 单元 + 140 集成）。
- 走查发现并修复的三个真实缺陷：
  1. **缺少根记录写入锁。** 原实现直接开始删关联行，设计 16.2.1 第 1 步要求先确认根记录旧标记并取得写入权。现在先执行 `UPDATE ... SET ConcurrencyToken = ConcurrencyToken WHERE Id = @Id AND 旧标记精确等值`，命中 1 行才继续；否则立即抛原生 `ConcurrencyException`，一个关联行都不会被删。这同时堵住了“检查与删除之间新写入关联行”的窗口。
  2. **根记录 CAS 用 `=` 而非精确比较。** 原先 `ConcurrencyToken = @ConcurrencyToken` 依赖数据库默认排序规则，大小写或尾空格变体可能误命中。现统一走 `Dialect.BuildExactStringEquals`。
  3. **根记录影响行数判断过宽。** 由 `affected == 0` 改为 `affected != 1`，与设计“必须验证影响行数为 1”一致。
- 真库证据（设计 16.2.1 全矩阵）：
  - Application 删除：自身、A0（其 Authorization）、T（ApplicationId 匹配 ∪ AuthorizationId 属于 A）全部删除；两张 URI 辅助表与 A 的 AuthorizationScopes 清空。
  - **Token.ApplicationId 为 NULL 但 AuthorizationId 属于 A** 的 Token 被删除（协议验收要求的固定场景）。
  - **Token.ApplicationId 属于待删应用但 AuthorizationId 在 A 之外**：Token 被删除，其引用的外部 Authorization 保留。
  - 跨应用授权：其他 Application、其 Authorization、其 Token 均保留，URI 辅助行与 Scope 辅助行不受影响。
  - Authorization 删除：其全部直接 Token 被删除，含 ApplicationId 为 NULL 或属于其他应用者；不反向删除这些 Token 引用的其他 Authorization；两个 Application 都保留。
  - 旧快照：删除后对同一快照调用 `UpdateAsync` 抛原生 `ConcurrencyException`（Application 与 Authorization 各验证一次）。
  - 根 CAS 冲突：删除整体回滚，Application、Authorization、Token 均仍在；标记的大小写与尾空格变体同样被拒绝且不产生任何删除。
  - 无关联行时删除仍成功；Token 单体删除不影响其 Authorization 与 Application。
- Manager 证据：原生 `OpenIddictApplicationManager.DeleteAsync` 后，在新作用域分别用非泛型 Application/Authorization/Token Manager 查询，三者均为 null。
- 关键取舍：写入锁用“对自己赋值”的 UPDATE 取得行写锁，不引入 `SELECT ... FOR UPDATE`（SQLite 不支持，且设计只要求取得写入权）；锁与后续删除在同一事务同一连接内，失败自然整体回滚；保留行的标记轮换留给批量撤销路径，本任务不重复实现。
- 结果与缺口：LIFE-01、LIFE-02 完成。LIFE-05（Manager/Server 协议回归）需要与 OpenIddict 服务端联调，按当前约束暂不实施；QUERY-01/02 的 IQueryable 翻译器仍未开始。
### E-019：IQueryable 回调翻译器

- 任务：QUERY-01、QUERY-02、QUERY-03（查询侧）；系列：7.7.2。
- 日期：2026-10-19；实现：本次未提交的工作区。
- 产物：`Queries/OpenIddictDapperColumnMap.cs`（固定列映射与可比较/可排序分类）、`Queries/OpenIddictDapperPredicateTranslator.cs`（白名单谓词与两张 NULL 真值表）、`Queries/OpenIddictDapperQueryable.cs`（记录式 `IQueryable`，拒绝枚举）、`Queries/OpenIddictDapperQueryBuilder.cs`（算子分解、投影、SQL 生成）、`Queries/OpenIddictDapperEntityMapper.cs`（实体/投影读取）；`OpenIddictDapperBase` 新增 `CountQueryAsync`/`GetQueryAsync`/`ListQueryAsync`；四个 Store 的九个查询回调全部接入；方言新增 `BuildExactStringColumnEquals`。
- 验证命令与结果：`dotnet build Sharp.OpenIddict.Dapper.slnx -c Release` 0 警告 0 错误；`dotnet test Sharp.OpenIddict.Dapper.slnx -c Release` 通过 198 项（42 单元 + 156 集成）。
- 为什么需要记录式 `IQueryable`：OpenIddict 的查询回调签名是 `Func<IQueryable<T>, IQueryable<TResult>>`，拿到的是 `Func` 而非表达式树。EF Core 能直接翻译是因为它传入的是自己的 `IQueryable`。本实现提供 `OpenIddictDapperQueryable<T>`，回调在其上调用 `Where`/`OrderBy`/`Skip`/`Take`/`Select` 时只记录表达式节点，绝不执行；记录完再整体翻译成一条 SQL。任何试图枚举该 queryable 的代码立刻抛 `NotSupportedException`，从结构上杜绝整表内存回退。
- 设计 9.1 两张真值表逐项有断言：
  - **等值表**：NULL==NULL 为 true、NULL==非 NULL 为 false、非 NULL==NULL 为 false、非 NULL==非 NULL 按值；不等值是完整二值谓词的取反，不是 `<>`。
  - **集合表**：空集合任意值为 false、只含 NULL 时 NULL 为 true 非 NULL 为 false、含 NULL 与非 NULL 时 NULL 为 true、不含 NULL 时 NULL 为 false、非 NULL 值逐项匹配；取反对每行返回相反结果；`NOT IN (..., NULL)` 的陷阱有专门用例。
- 真库证据：`client`/`Client`/`client ` 互不命中（走方言精确谓词）；可空日期 `<` 在任一操作数为 NULL 时为 false，`!(date < cutoff)` 包含 NULL 行而不是变成 `>=`；同表两列比较走 `BuildExactStringColumnEquals`；闭包常量与 `TState` 参数都绑定为参数；日期参数经方言格式化后与存储形式一致（`2026-01-01 00:00:00.000000`）；LargeText/Json 只允许 NULL 判断，内容比较被拒绝；Join/GroupBy/`string.StartsWith`/未映射成员/`Enumerable` 枚举全部在 SQL 之前抛 `NotSupportedException`；投影、`OrderBy`、`Skip`/`Take` 均按调用顺序生成。
- 关键取舍：
  - 列名只来自固定映射，绝不来自用户输入；未映射成员在分类阶段就被拒绝。
  - 有界字符串的成员匹配不用原生 `IN`（会依赖排序规则），而是逐值生成方言精确谓词后 OR；这是设计 16.1.1 与 9.1 的共同要求。
  - 值操作数在分类时不绑定，由消费方按需要的表示绑定：日期走方言格式化，字符串走精确谓词参数。这样同一值不会以两种表示出现。
  - `BuildEquality` 先把列归一化到左侧，再分“对方是 NULL/列/值”三种情况生成谓词，避免四种操作数组合各写一遍。
- 结果与缺口：QUERY-01～03 完成。设计 9.2 明确不支持的表达式一律拒绝，未引入任何内存回退。LIFE-05（Manager/Server 协议回归）需要与 OpenIddict 服务端联调，按当前约束暂不实施；Oracle/DM 的空串语义化简随对应方言任务实施。
### E-020：访问器与无截断证据

- 任务：STORE-02（收尾）；系列：7.7.2。
- 日期：2026-10-19；实现：本次未提交的工作区。
- 产物：`tests/Sharp.OpenIddict.Dapper.IntegrationTests/AccessorContractTests.cs`（8 项）。
- 验证命令与结果：`dotnet build Sharp.OpenIddict.Dapper.slnx -c Release` 0 警告 0 错误；`dotnet test Sharp.OpenIddict.Dapper.slnx -c Release` 通过 206 项（42 单元 + 164 集成）。
- 补齐的验收缺口（此前 UNIQUE 覆盖不足的部分）：
  - **长 Payload 无截断**：100 KB 的 Token Payload 写入后长度与内容都完全一致。
  - **Unicode 无损失**：混合文字（拉丁、中文、西里尔、希伯来、阿拉伯、emoji、组合附加符号）作为 Name/DisplayName/Description/本地化字典值/Resources 元素往返无损，且尾空格变体作为独立集合元素各自可精确查到。
  - **大 JSON**：200 条 Properties 与 5 KB 的 Settings 值往返完整，嵌套对象属性可读。
  - **引用标识不散列**：ReferenceId 以字面值存入并读回，直接查库确认列中不是摘要；`FindByReferenceIdAsync` 能按该字面值命中。ClientSecret 同样原样保存。
  - **全部访问器往返**：Application 的 12 个 Get/Set、Scope 的 3 个、Authorization 的 6 个逐项断言。
  - **UTC 微秒规范化**：带亚微秒精度的值写入后按 10 tick 截断，三处日期一致。
- 既有覆盖引用：`JsonConversionTests`（12 项单元）已覆盖字符串/本地化/属性/设置集合的序列化、非法 JSON 抛异常、null 与空集合归一、UTC 规范化、SQLite 日期与分页。
- 关键取舍：长 Payload 与 Unicode 用真实数据库验证而不是只测序列化，因为截断风险来自列类型与驱动绑定，纯序列化测试看不到。
- 结果与缺口：STORE-01～STORE-06 全部完成。剩余实现任务集中在六种数据库方言、迁移/兼容/CI/发布验收，以及必须与服务端联调的 LIFE-05、APP-01～04。
### E-021：包资产核对

- 任务：COMPAT-02；系列：7.7.2。
- 日期：2026-10-19；实现：本次未提交的工作区。
- 产物：`tests/Sharp.OpenIddict.Dapper.IntegrationTests/PackageAssetTests.cs`（43 项）；`artifacts/` 下八个 7.0.0 包。
- 验证命令与结果：`dotnet pack Sharp.OpenIddict.Dapper.slnx -c Release -o artifacts` 产出 8 个包；`dotnet test` 通过 253 项（43 单元 + 211 集成）。
- 实际核对内容（全部读取真实 nupkg 内的 zip 与 nuspec，不依赖构建日志）：
  - 八个包全部产出，版本统一为 7.0.0，`artifacts` 中恰为 8 个文件。
  - 每包 `lib/` 下恰有五种 TFM，且每个 TFM 都含 `.dll` 而非空目录。
  - nuspec 为五种 TFM 各声明一个 dependency group。核心包的实测分组：ns2.0/ns2.1/net8.0 → `Microsoft.Extensions.* 8.0.2`；net9.0 → `9.0.21`；net10.0 → `10.0.13`；`OpenIddict.Core`/`OpenIddict.Abstractions` 全部 `7.7.2`。
  - 低代际 TFM 组中不出现高代际 `Microsoft.Extensions.*` 或 `System.Security.Cryptography.*`（正是设计 4.1.1 要防的冲突）。
  - 任何依赖都不得是预览版；`OpenIddict` 五包均为稳定 `7.7.2`。
  - 每个 nuspec 依赖版本必须落在 `Directory.Packages.props` 实际钉住的版本集合内，否则说明某工程用工程级 `Version` 绕过了中央包管理。
  - SQL 资产只出现在拥有它的包：核心包无 `sql/`，SQLite 包含 `sql/7/install/v0001.sql`、`sql/manifest.json`、`sql/README.md`。
- 走查中修正的测试缺陷：`ReadCentralVersions` 原先对同一包 ID 只保留最后一条，而 CPM 按 TFM 会让同一 ID 出现多次（`Microsoft.Extensions.*` 就是 8.0.2/9.0.21/10.0.13 三行），导致误报“中央版本是 10.0.13”。改为收集全部声明版本并要求包内依赖命中其中之一。版本号提取也从不稳固的 `LastIndexOf` 改为按已知产品名前缀剥离。
- 结果与缺口：COMPAT-02 完成。包资产层面的中央包管理约束已被测试固化，后续新增 provider 包会自动纳入检查。

### E-022：低 TFM 资产独立加载

- 任务：COMPAT-03；系列：7.7.2。
- 日期：2026-10-19；实现：本次未提交的工作区。
- 产物：`tests/Sharp.OpenIddict.Dapper.IntegrationTests/LowTargetFrameworkAssetTests.cs`（4 项）。
- 验证命令与结果：`dotnet build Sharp.OpenIddict.Dapper.slnx -c Release` 五 TFM 全构建；`dotnet test` 通过 253 项。
- 做法：用可回收的 `AssemblyLoadContext` 按 `deps.json` 记录的运行时资产映射解析整个依赖闭包，确保加载的是 ns2.0/2.1 自己编译时的依赖版本，而不是宿主里已加载的 net10 副本。断言 `TargetFrameworkAttribute` 恰为 `.NETStandard,Version=v2.0` 或 `v2.1`，从元数据层面排除“net8 程序集放在 netstandard 目录里”的伪装。
- 实际调用到的路径（不是只反射看方法名）：`OpenIddictDapperIdentifiers.Validate` 正常返回、`IsValid` 对空白串/数字开头/注入形状返回 false、`ApplyPrefix` 替换全部 `__PREFIX__`、`SplitStatements` 把两条语句拆成两条、`IOpenIddictDapperConnectionFactory.CreateConnectionAsync` 返回 `ValueTask<T>`、`OpenIddictDapperBase` 类型在元数据中可见。
- 走查中发现的真实约束：在隔离上下文里对 ns2.0 程序集调用 `GetTypes()` 会失败，因为 `OpenIddictDapper*Store` 实现的接口来自**按 TFM 区分的** `OpenIddict.Abstractions`，而宿主只提供 net10 版本，形状不同（`FindByPostLogoutRedirectUriAsync`/`FindAsync`/`FindByNamesAsync` 报“没有实现”）。这不是资产损坏，而是跨 TFM 混用依赖的后果，正好印证设计第 5 节“必须区分扩展 TFM 与驱动/依赖 TFM”。因此测试改为按名称验证类型存在，不强制解构整个类型表。
- 结果与缺口：COMPAT-03 完成。宿主仍是 net10，本文档不声称在旧运行时上实测通过。

### E-023：驱动与 TFM 兼容性矩阵

- 任务：COMPAT-04；系列：7.7.2。
- 日期：2026-10-19；实现：文档变更，无代码。
- 产物：`docs/design.md` §5.1“驱动与 TFM 实测矩阵”。
- 数据来源：逐包读取 `~/.nuget/packages/<id>/<version>/lib` 的目录名，锁定版本与 `Directory.Packages.props` 一致：SqlClient 7.1.1、Npgsql 10.0.3、MySqlConnector 2.6.2、Microsoft.Data.Sqlite 10.0.13、Oracle 23.26.301、DM 8.3.1.47463。
- 两条硬约束（此前文档未明确写出）：
  - **netstandard2.0 宿主无法使用 Npgsql 与 Oracle 驱动**：Npgsql 只有 net8.0/net9.0/net10.0；Oracle 最低 netstandard2.1。扩展仍产出 ns2.0 程序集，但这两个 provider 在 ns2.0 宿主上不可用。
  - **net10 宿主无法使用 SqlClient 7.1.1 与 DM.DmProvider**：二者都没有 net10 资产。因此 SQL Server 与达梦的**真实连接验收只能在 net8.0/net9.0 宿主进行**，不能在本机仅有 net10 SDK 的环境里完成，也不能用编译通过代替。
- 结果与缺口：矩阵已写入设计文档并与实际包核对一致。所有“回退”组合（如 net10 上用 Oracle 的 ns2.1 资产）仍未做真实服务器验证，按未验证处理；连接验收留给 DB-02～DB-07 与 BASE-03/04。
### E-024：全项目更名为 Sharp.OpenIddict.Stores

- 任务：用户书面要求（非原任务清单项）；系列：7.7.2。
- 日期：2026-10-19；实现：本次未提交的工作区。
- 变更：包名 `Sharp.OpenIddict.Dapper.*` → `Sharp.OpenIddict.Stores.*`；类型前缀 `OpenIddictDapper` → `OpenIddictStore`（`OpenIddictDapperStoreBase` 特例为 `OpenIddictStoreBase`，避免 `StoreStore`）；命名空间、slnx、示例工程、测试工程与目录一并调整。
- 原因：交付物是 OpenIddict 的四个 Store 实现，不是 Dapper 扩展。实测 Dapper API 仅在 `OpenIddictDapperSession` 中调用 8 处，实体映射/谓词翻译/方言 SQL/逐行异步读取均为自行实现，旧名错误暗示了依赖边界。
- 规模：13 个目录、14 个工程文件、40 个类型标识符、78 个文本文件；`OpenIddictDapper` 925 处、`Sharp.OpenIddict.Dapper` 386 处全部替换。
- 验证命令与结果：`dotnet build Sharp.OpenIddict.Stores.slnx -c Release` 0 警告 0 错误；`dotnet pack` 产出 8 个包；`dotnet test` 通过 254 项（43 单元 + 211 集成，含新增的 Dapper 边界测试）。
- 走查中发现并修正的问题：
  - **`manifest.json` 的 SHA-256 失效**：安装脚本首行注释含包名，改名后脚本内容变化，摘要随之变化（`D57BA1FF…` → `549BBE7A…`）。已核对脚本确实只改了那一行注释、无结构变化，更新清单摘要使二者一致，而不是放宽比较。
  - **历史记录被误改**：批量替换把 E-001～E-023 证据记录里的旧名一并改了，违背“保留历史原文”的约定。已逐行还原该区间的 43 行，历史记录保持当时如实。
  - **新增的 Dapper 边界测试最初无效**：限定规则写成“任意 `.QueryAsync<` 即违规”，误报三个 Store（它们调用的是本项目 session 上的同名方法，并非 Dapper API）。收敛为“不得出现 `using Dapper;`、`global::Dapper`、`Dapper.SqlMapper`、`new CommandDefinition(`”后，用注入真实违规的方式验证规则确实会失败、撤销后恢复通过——否则这条测试只是自证清白。
- 文档同步：设计文档版本升至 1.9，新增 §1.1“命名与依赖定位”与 §9.3“Dapper 依赖的现状、边界与退出条件”；README 更新项目名、现状与更名说明。E-001～E-023 保持旧名不改。
- 结果与缺口：更名完成且全量测试通过。`Sharp.OpenIddict.Stores.Models` 暂保持原名；若后续认为 `Stores.Models` 语义含混（它实为实体 POCO），可另起任务改为 `.Entities`。

### E-025：阶段成果整体纳入版本控制

- 任务：交付基线（非新增功能）；系列：7.7.2。
- 日期：2026-10-19；实现提交：`ec2a420`。
- 产物：E-005～E-024 期间产生的全部源码、测试、示例、构建与包管理文件首次进入版本控制，共 74 个文件：根级 `.gitattributes`、`global.json`、`Directory.Build.props`、`Directory.Packages.props`、`Sharp.OpenIddict.Stores.slnx`；`src/` 8 个产品工程（含 SQLite 的 `Sql/` 脚本）；`tests/` 3 个测试工程；`samples/` 2 个示例骨架；`docs/contracts/7x-store-contracts.md`。
- 原因：此前所有实现只存在于工作区，`git clean`/`git checkout` 一次误操作即全部丢失；§9.5 已把这种状态列为搬迁风险。
- 验证命令与结果（提交前重跑）：`dotnet build Sharp.OpenIddict.Stores.slnx -c Release` 0 警告 0 错误；`dotnet test Sharp.OpenIddict.Stores.slnx -c Release --no-build` 通过 254 项（43 单元 + 211 集成，0 跳过）；`artifacts/` 内 8 个 `7.0.0` 包与 COMPAT-02 记录一致；`git status --short` 只剩本次提交内容，`bin/`、`obj/`、`artifacts/` 均被 `.gitignore` 排除（已用 `git check-ignore -v` 逐项确认，未误提交构建产物）。
- 结果与缺口：这一提交固化的是“24/50 项完成”的已验证基线，不代表任何新功能；DB-02～DB-07 等未完成项状态不变。归档类记录（E-005～E-024）中写的“本次未提交的工作区”保留原文，不追改历史。

### E-026：SQL Server 方言、安装脚本与执行层去 Dapper

- 任务：DB-02（SQL Server 方言与真库验收）、ENG-03（执行层改造）；系列：7.7.2。
- 日期：2026-10-19；实现提交：`0604d4b`（执行层去 Dapper）、`49d6933`（SQL Server provider 与验收）；本文档记录提交 `f34e957`。
- 产物：`src/Sharp.OpenIddict.Stores.SqlServer/`（`OpenIddictStoreSqlServerDialect.cs`、`OpenIddictStoreSqlServerBuilderExtensions.cs`、`Sql/7/install/v0001.sql`、`Sql/manifest.json`、`Sql/README.md`）；`src/Sharp.OpenIddict.Stores/OpenIddictStoreCommands.cs`（新增 ADO.NET 执行层）；`tests/Sharp.OpenIddict.Stores.IntegrationTests/SqlServerStoreContractTests.cs`（15 项）、`ServerFactAttribute.cs`；`OpenIddictStoreCoreBuilderExtensions.cs` 新增 `UseStores()` 并把 `UseDapper()` 降级为 `[Obsolete]` 别名。
- 原因一（执行层）：核心包 nuspec 的五个 TFM 依赖组都带着 `Dapper 2.1.89`，消费者安装即被强制拉入该依赖，与包名 `Stores` 的定位不符。实测该依赖只出现在 `OpenIddictStoreSession` 一个类型的 8 处调用，替换面收敛，因此改为直接使用 `DbCommand`/`DbParameter`/`DbDataReader`，并删除 `Directory.Packages.props` 中的版本项。
- 原因二（方言）：SQL Server 的精确比较、可空唯一键、分页语法与日期精度都不能照抄 SQLite 实现。
- 驱动抽象选型（实测六驱动）：`Microsoft.Data.Sqlite`、`Microsoft.Data.SqlClient`、`Npgsql`、`MySqlConnector`、`Oracle.ManagedDataAccess.Core`、`DM.DmProvider` 全部同时实现 `IDbConnection` 与 `DbConnection`，且都有 `OpenAsync`/`ExecuteReaderAsync`/`ExecuteNonQueryAsync`/`ExecuteScalarAsync`。选 `DbConnection`/`DbCommand`/`DbParameter`/`DbDataReader`：`IDbConnection`/`IDbCommand` 只有同步 API，会与“不得用 `Task.Run` 掩盖同步实现”冲突；`DbConnection` 本身即实现 `IDbConnection`，按接口访问的调用方仍可隐式转换。
- 真库证据（LocalDB，SQL Server 2025 17.0.4025.3，SqlClient 7.1.1，net10 宿主经 `runtimes/win/lib/net9.0` 加载）：15 项全部通过，覆盖随包脚本真实执行（9 张表、3 个过滤唯一索引、版本行）、重复安装被拒绝而非跳过、`nvarchar(max)` 长值与 Unicode、四集合索引与 JSON 权威复核、`datetime2(6)` 微秒往返、精确比较拒绝大小写/重音/尾随空格变体、唯一键拒绝重复且允许任意多个 NULL、CAS 旧快照抛原生 `ConcurrencyException`、`OFFSET/FETCH` 分页与仅 OFFSET、COUNT 不产生派生表 ORDER BY、关联删除（仅经 Authorization 可达的 Token）、批量撤销把 NULL 状态按“未撤销”处理、Prune 前后边界。
- 关键实测结论：`BIN2` 单用仍把 `client` 与 `client ` 视为相等，必须同时比较 `DATALENGTH`；`FETCH NEXT 0 ROWS` 是语法错误，方言选择抛 `ArgumentOutOfRangeException` 而非静默返回；`COUNT(*) FROM (… ORDER BY …)` 在 SQL Server 上直接报错，`CountQueryAsync` 因此传入 `forCount: true` 丢弃无分页的 ORDER BY；以 `DateTime` 绑定 `datetime2(6)` 会被推断为 `datetime` 并截断到 3.33 ms（回读 `…1233330` 刻度），改用 UTC `DateTimeOffset` 后精确保留（`…1234560`）。
- 验证命令与结果：`dotnet build Sharp.OpenIddict.Stores.slnx -c Release` 0 警告 0 错误；`dotnet test Sharp.OpenIddict.Stores.slnx -c Release --no-build` 通过 271 项（45 单元 + 226 集成）；未设置 `DSH_SQLSERVER` 时该套件 15 项显式跳过、其余集成仍通过，设置后 0 跳过；`dotnet pack -o artifacts` 后核心包 nuspec 五个 TFM 依赖组已无 `Dapper`。
- 结果与缺口：DB-02 按 §5 记为“部分通过”——15 项已覆盖验收清单中的唯一索引、尾随空格、Unicode、`datetime2(6)`、摘要索引、分页与关联删除，但完整合同（全部 Store 成员对 SQL Server 的逐项运行）尚未铺开，因此 §5 主表不填“通过”。未验证最低版本 SQL Server 2016，实测环境为 2025；`SqlClient` 无 `net10.0` 资产，走 `net9.0` 资产属正常降级。`UseDapper()` 保留为 `[Obsolete]` 别名并新增测试固定其与 `UseStores()` 行为一致，避免别名退化为空实现；本仓库内部调用点已全部改用 `UseStores()`。

### E-027：移除 global.json，SQL 脚本改为可配置前缀/schema 的模板

- 任务：BASE-01（SDK 要求）、MIG-01/MIG-03（脚本交付形态）、STORE-01/DB-02（安装验证）；系列：7.7.2。
- 日期：2026-10-19；实现：本次未提交的工作区。
- 起因：用户提出两个问题——(1) `global.json` 固定 SDK 补丁版本，换机器就可能因版本对不上而无法构建；(2) 表前缀是运行时可配置的，而仓库脚本把 `__PREFIX__Applications` 这类占位写法当成了可执行 SQL，装到库里根本不是合法表名。
- 变更一（SDK）：删除 `global.json`。工程以 `net10.0` 目标框架本身作为 SDK 下限，不再绑定补丁号。验证：删除后 `dotnet --version` 为本机 10.0.401（原文件要求 10.0.402，本机并未安装），`dotnet build Sharp.OpenIddict.Stores.slnx -c Release` 0 警告 0 错误。
- 变更二（脚本）：`v0001.sql` → `v0001.sql.template`（SQLite、SQL Server 两套，`git mv` 保留历史）；新增 `OpenIddictStoreIdentifiers.RenderDeploymentScript(template, dialect, prefix, schema)`，按运行时同一套 `Validate`+`QuoteIdentifier` 规则把 `__PREFIX__`/`__SCHEMA__` 展开为真实标识符，非法前缀/schema 在生成副本之前抛出。SQL Server 模板的全部表名、外键引用、`OBJECT_ID` 检查均加 schema 限定；SQLite 无 schema，仅用前缀占位符。
- 清单：`manifest.json` 增加 `rendersTokens`，声明该文件包含哪些占位符，并由 `SqlAssetTests.Manifest_DeclaresTheTokensEachTemplateRenders` 与文件实际占位符（忽略注释行）逐项比对；`ShippedScripts_AreTemplatesNotRunnableSql` 断言 `Sql/` 下不再有 `.sql` 文件。
- 测试改造：新增 `DeploymentScript` 帮助类统一渲染（11 个测试类原先各自复制 `ApplyPrefix` + 路径拼接）；同时合并 10 份重复的 `SqliteConnectionFactory` 到单个文件。
- 真库证据一（SQLite，Microsoft.Data.Sqlite 10.0.13，net10 宿主）：渲染后执行，九张表、`Series=7/Version=1`、外键、唯一键、尾随空格唯一性、索引、重复安装被拒全部通过；新增 `CustomPrefix_InstallsAndIsUsable` 用前缀 `Oidc` 渲染安装，断言表名变为 `OidcApplications`/`OidcSchemaVersions`、默认前缀的 `OpenIddictApplications` 不存在，并用同一前缀的 Store 真实写入与按 ClientId 查回。
- 真库证据二（SQL Server 2025 LocalDB 17.0.4025.3，SqlClient 7.1.1）：默认前缀 15 项保持通过；新增 `CustomPrefixAndSchema_InstallAndAreUsable` 在独立数据库上用前缀 `Oidc` + schema `auth` 渲染安装，断言 `auth` 下 9 张表、`dbo` 下 0 张 `Oidc*` 表、`auth.OidcSchemaVersions` 版本行为 1，并用以 `TablePrefix=Oidc`/`Schema=auth` 配置的 Store 写入与查回。该套件 16 项全部通过、0 跳过。
- 走查中发现并修正的问题：
  - SQLite 模板首行注释同时提到两个占位符，导致“清单声明”与“文件实际占位符”断言不一致。修正方式是让比对忽略整行注释（注释是说明，不是部署需要填充的位置），而不是改注释迁就测试。
  - `Record.ExceptionAsync(ExecuteInstallScriptAsync)` 在方法签名增加可选参数后无法继续按方法组使用，改为显式 lambda。
  - `PackageAssetTests` 与 `SqlAssetTests` 中硬编码的 `v0001.sql` 路径同步为 `.sql.template`，并验证包内 `sql/` 下确实携带模板而非渲染后的 SQL。
- 验证命令与结果：`dotnet build Sharp.OpenIddict.Stores.slnx -c Release` 0 警告 0 错误；`dotnet pack -c Release -o artifacts` 产出 8 个包；`dotnet test Sharp.OpenIddict.Stores.slnx -c Release --no-build` 通过 285 项（45 单元 + 240 集成）；未设置 `DSH_SQLSERVER` 时 16 项显式跳过（269 通过 / 16 跳过），设置 LocalDB 后 285 项全通过、0 跳过。
- 结果与缺口：脚本不再假设默认表名，前缀/schema 已是可验证的部署输入；SQLite 与 SQL Server 两套模板均有默认值与自定义值的真库证据。其余四个 provider 尚无模板；运行时读取 `SchemaVersions` 的版本检查仍未接入 Store（归 MIG-01）。

后续任务完成时复制以下模板并填写，再勾选任务；同一任务有多个系列或环境时分别列明，不覆盖过去的失败/修复记录。
```text
### E-012：Application Store 与集合索引

- 任务：STORE-03（Application 部分）、ENG-03（Store 基础设施）；系列：7.7.2。
- 日期：2026-10-19；实现：本次未提交的工作区。
- 产物：`src/Sharp.OpenIddict.Dapper/Stores/`（`OpenIddictDapperBase.cs`、`OpenIddictDapperApplicationStore.cs`、`OpenIddictDapperJsonWebKey.cs`）、`OpenIddictDapperSchema.cs`、`OpenIddictDapperSession.cs`、`OpenIddictDapperConnections.cs`；`tests/Sharp.OpenIddict.Dapper.IntegrationTests/SqliteApplicationStoreTests.cs`（14 项）。
- 验证命令与结果：`dotnet build Sharp.OpenIddict.Dapper.slnx -c Release` 0 警告 0 错误；`dotnet test Sharp.OpenIddict.Dapper.slnx -c Release` 通过 87 项（42 单元 + 45 集成）。
- 真库证据：全部 14 项在真实 SQLite 临时文件库上执行随包安装脚本，覆盖标量列与集合列往返、JWK 集合往返、空集合读回空、ClientId 精确比较（`client`/`Client`/`client `/`CLIENT` 互不命中）、唯一索引冲突、URI 集合按摘要候选 + Ordinal 复核（含 `https://a/cb` 与 `https://a/cb ` 并存）、Update 轮换标记且旧快照抛原生 `ConcurrencyException`、Update 重建集合索引、Application 关联删除（Authorization、AuthorizationScopes、直接与间接 Token）、删除的 CAS 拒绝、Count、按 Id 排序的分页（count/offset/负参数/count=0）、查询回调明确拒绝、Instantiate 生成 GUID。
- 关键设计落地：Store 实现原生 `IOpenIddictApplicationStore<T>` 全部 42 个成员；集合与字典以 JSON 为权威，辅助表用 SHA-256 候选 + Ordinal 复核；每次操作获取并释放新连接；写入走事务，提交成功后才更新内存标记。
- 明确拒绝而非近似：`CountAsync`/`GetAsync`/`ListAsync` 的 IQueryable 回调抛 `NotSupportedException` 并说明由 QUERY-01～03 交付，不退化为内存整表执行；这是设计 9 允许的唯一诚实实现，测试已锁定该行为。
- 剩余缺口：Scope Store 未实现（STORE-03 保持进行中）；Authorization/Token Store（STORE-04）、Revoke/Prune（LIFE-03/04）未开始；`ListAsync` 的逐行读取已在 SQLite 验证，但大结果集与提前退出的资源释放归 QUERY-03 专门验收。
- DI 接入证据：`UseDapper` 只注册已实现的 Store；`ManagerIntegrationTests`（4 项）证明 `IOpenIddictApplicationStore<OpenIddictDapperApplication>` 可解析、原生泛型与非泛型 Manager 均可创建/查找/更新，且精确比较在 Manager 路径同样生效。未实现的 Store 不注册占位实现，缺失时 DI 明确失败。
### E-xxx：任务结果

- 任务编号：
- 系列/上游精确版本/源码提交：
- 日期、实现提交（未提交则写工作区）：
- 产物路径：
- 验证命令、CI 运行链接或结果文件：
- SDK/测试宿主/实际加载的 TFM 资产：
- provider、驱动精确版本、服务器精确版本、模式/字符集：
- 涉及 SQL 时的脚本路径/摘要、源/目标架构版本、执行工具及 schema/prefix：
- 通过/失败/跳过的范围与结果：
- 完成条件逐项结论、剩余缺口与支持声明：
```

进行中或阻塞时，在对应任务下补充一行并按需关联详细记录：

```text
状态：进行中/阻塞；已完成：……；剩余：……；原因/所需条件：……；证据：E-xxx。
```

不预填负责人、截止日期或完成比例。后续实际分工、环境和运行结果明确后再记录，保证进度能够直接支持下一次开发接续。

## 9. 接续说明（新会话从这里开始）

本节专供中断后接续使用。读这一节即可回答“做到哪了、下一步做什么、哪些决定已定、有什么坑”，无需翻阅对话记录。

### 9.1 当前状态快照

- 已完成 **25 / 50** 项。P0 文档基线、P3 SQLite 基础读写（6/6）、P4 的查询与生命周期（QUERY-01～03、LIFE-01～04）、P6 多目标与包资产（4/4）全部完成，DB-02 完成（按 §5 记“部分通过”）。基线已整体提交并纳入版本控制（E-025、E-026）。
- 验证基线：`dotnet build Sharp.OpenIddict.Stores.slnx -c Release` 0 警告 0 错误；`dotnet pack -o artifacts` 产出 8 个包；`dotnet test` 通过 **285 项（45 单元 + 240 集成）**。任何改动的底线是不低于这组数字。
- 已具备的能力：四类 Store 的全部 145 个成员、集合索引（JSON 权威 + SHA-256 候选 + Ordinal 复核）、Update CAS、七种批量撤销、Prune 清理、关联删除矩阵、IQueryable 回调翻译、逐行异步读取、五 TFM 与分 TFM 依赖组、八个包资产、SQLite 与 SQL Server 两套方言。
- 仓库不提交 `global.json`（E-027）：工程只要求 .NET 10，换机器不会因补丁号对不上而构建失败。
- SQLite 与 SQL Server（LocalDB）完成真库验收；PostgreSQL/MySQL/MariaDB/Oracle/DM 五种方言尚无实现。

### 9.2 下一步：DB-03（PostgreSQL 方言）

SQL Server 已把方言抽象需要补的洞（精确比较的 `DATALENGTH` 补偿、`OFFSET/FETCH`、`datetime2(6)` 绑定）探完，PostgreSQL 需要重点处理的是 schema 限定、`bytea` 摘要、`timestamp(6)` 的 UTC 转换、`LIMIT/OFFSET` 以及驱动参数适配不污染其他 provider。**同时必须补 `Sql/7/install/v0001.sql.template` 与清单**（E-027 起脚本以模板形式交付）。

`Npgsql 10.0.3` 有 net8.0/net9.0/net10.0 资产，可在 net10 宿主直连；需要一个可用的 PostgreSQL 14+ 实例（本机当前没有）。Oracle/DM 的驱动限制见 §5.1。

其余待办按阶段：BASE-03/04（Oracle、达梦真库）、ENG-04/05、DB-03～DB-07、MIG-01/03/04、APP-02/03/04、CI-01/03、REL-01。**LIFE-05 与 APP-01 需要与 OpenIddict 服务端联调，按用户明确要求暂不实施。**

### 9.3 已定且不必再讨论的决定

1. **命名**：包 `Sharp.OpenIddict.Stores.*`、类型前缀 `OpenIddictStore`、命名空间同包名，三层一致。旧名 `Sharp.OpenIddict.Dapper.*` 已废弃（见 E-024）。**历史记录 E-001～E-023 刻意保留旧名原文**，它们记录的是当时实际状态，不得为了好看而改写。
2. **`Sharp.OpenIddict.Stores.Models` 保持原名，不改 `.Entities`**：上游 `OpenIddict.EntityFrameworkCore.Models`、`OpenIddict.EntityFramework.Models`、`OpenIddict.MongoDb.Models` 三套持久化实现均用 `.Models`，本项目刻意与上游同层级，跟随生态惯例。此前“`Stores.Models` 读起来像 Store 的模型”的顾虑已被上游先例否定。
3. **执行层已直接基于 ADO.NET**：Dapper 已从依赖与源码中移除（E-026），`DapperIsNotReferencedOrImported` 与 `CentralManifest_DoesNotPinDapper` 固化该边界。不要再引入任何 ORM。
4. **不提交 `global.json`**：只要求 .NET 10 SDK，实际版本记在完成记录里（E-027）。想“精确锁版本”的冲动要压住——换机就构建不了的成本更高。
5. **SQL 脚本一律以 `.sql.template` 交付**：前缀与 schema 是运行时配置，写死表名的文件对定制部署必错；渲染唯一入口是 `OpenIddictStoreIdentifiers.RenderDeploymentScript`，清单用 `rendersTokens` 声明占位符（E-027）。**不得**为图省事再加一份“默认前缀可直接执行”的 `.sql`。
6. **公开合同以设计文档为准**：API、数据库结构或支持范围需要变化时，先改设计文档再改代码与测试。

### 9.4 踩过的坑（避免重复）

- **`manifest.json` 的 SHA-256 会随脚本内容失效**。任何改动模板（哪怕只是注释）都必须重算摘要，`SqlAssetTests` 会失败提醒。更新摘要前先确认文件只有预期差异，不要把“改清单凑通过”当成修复。
- **模板占位符与注释**：`Manifest_DeclaresTheTokensEachTemplateRenders` 只统计非注释行里的占位符。模板头部注释里写占位符名不会让断言失败，但真正用在 SQL 里的占位符必须与清单一致。
- **`docs/development.md` 的 §8 历史记录不要批量替换**。改名、术语调整等全局替换必须排除 §8 区间，否则会篡改历史证据（E-024 曾发生并已还原 43 行）。
- **新增的“约束类测试”必须验证其有效性**。写完限制性断言后，故意注入一次违规确认测试会失败，再撤销；否则可能写出永远为真的假保护（E-024 中曾发生过）。
- **本仓库 `.cs`、`.sql.template`、`.json`、`.md` 统一使用 LF 且无 BOM**（见 `.gitattributes`）。用脚本批量写文件时保持 LF，`git diff --check` 必须干净。
- **代码与测试文本一律英文**，仅 `docs/` 为中文。

### 9.5 仓库搬迁注意

`src/`、`tests/`、`samples/`、`docs/contracts/`、`Directory.*.props`、`*.slnx`、`.gitattributes` 已在 `ec2a420`（E-025）纳入版本控制，搬到新路径后 `git status` 应当干净（`global.json` 已删除，见 E-027）。仍然：

- 不要执行 `git clean -fdx`，它会连未跟踪的 `bin/`、`obj/`、`artifacts/` 一起删；`artifacts/` 里的 8 个包需要重新 `dotnet pack` 才能恢复（COMPAT-02 的断言读的是真实 nupkg）。
- 移动后先在新路径执行 `dotnet restore Sharp.OpenIddict.Stores.slnx`，再 `dotnet test`，确认仍是 285 项通过（`bin/`、`obj/` 中留有旧的绝对路径）。

### 9.6 下一会话的读取顺序

1. 本节（§9）确认状态与下一步。
2. `docs/design.md` §1.1（命名）、§5.1（驱动矩阵）、§9.3（执行层）、§10.1（模板交付）四处与本阶段强相关。
3. §4 任务清单确认具体验收标准。
4. 动手前先跑一次 `dotnet test`，确认基线仍是 285 项通过。
