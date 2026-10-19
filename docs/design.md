# Sharp.OpenIddict.Dapper 项目设计文档

文档版本：1.2。创建日期：2026-10-19。修订日期：2026-10-19。状态：设计评审稿。

本文是项目的正式设计文档和后续开发依据，覆盖产品范围、兼容版本、公共 API、持久化结构、数据库差异、运行语义、测试、示例与交付标准。它不是临时计划或执行记录。评审通过后以本文作为实现基线；实现若需要改变公开合同、数据库结构或支持范围，先修订本文，再修改代码与测试。

## 1. 目标与已确认要求

为 OpenIddict 提供基于 Dapper 的关系数据库存储扩展，沿用 OpenIddict 原生 Manager、Store、Descriptor 和依赖注入扩展点；通过原生选项禁用实体缓存，保证批量变更后的重新查询读取数据库。

- 所有发布包以 `Sharp` 开头，统一采用 `Sharp.OpenIddict.Dapper.*`。
- 首版数据库覆盖 SQL Server、PostgreSQL、MySQL/MariaDB、SQLite、Oracle、达梦。
- 提供 .NET Standard 2.0、2.1、.NET 8、9、10 的兼容系列，并提供面向 OpenIddict 8.x 的独立系列。
- 所有测试、示例和可运行辅助程序使用 .NET 10，解决方案使用 `.slnx`。
- 本文明确给出技术选型，不把尚待评审的设计决定表述为用户已经确认的要求。

扩展负责 Application、Authorization、Scope、Token，以及 8.x 新增的 Resource、Session 的持久化。用户、密码、角色、用户登录界面和 ASP.NET Core Identity 存储不属于此扩展。

## 2. 源码调研与版本边界

本地参考仓库：`D:\Code\dotnet-source\openiddict-core`。只读取源码和 Git 对象，不改变该仓库的分支或工作区。

| 参考版本 | 本地证据 | 存储接口 | 与本项目的关系 |
| --- | --- | --- | --- |
| 7.7.2 | 标签，提交 `950cb13b` | Application、Authorization、Scope、Token | Core 提供全部五个要求的目标框架，作为兼容系列基线 |
| 8.0.0-preview.4 | 标签，提交 `644eab6a` | 上述四类，加 Resource、Session | 作为可复现的 8.x 设计基线，选用其 .NET 10 资产 |
| 当前 HEAD | `ab23a162`，Versions.props 标记 preview.5 | 六类存储，接口也有变化 | 用来了解发展方向，不把尚未核实发布的 HEAD 当成 NuGet 依赖 |

8.x 当前的 Core 还包含 .NET Framework 4.8 目标，但没有 .NET Standard、.NET 8、.NET 9 目标；本项目不新增 .NET Framework 专用目标。不能把扩展项目的 TargetFrameworks 改成五个值，就宣称 OpenIddict 8 支持这五个平台。

8.x 的变化也不止增加两个实体：例如 `CountAsync` 查询回调增加状态参数，Authorization/Token 的 `FindAsync` 改为元组参数，Token 增加 SessionId 访问方法。两个主版本需要分别对接真实接口。

主要参考位置：

- `src/OpenIddict.Abstractions/Stores/`：必须实现的合同。
- `src/OpenIddict.Core/Managers/`：调用方式、验证、缓存和并发异常处理。
- `src/OpenIddict.Core/OpenIddictCoreBuilder.cs`：原生实体和 Store 替换入口。
- `src/OpenIddict.EntityFrameworkCore/Stores/`：关联删除、撤销和清理规则。
- `src/OpenIddict.EntityFrameworkCore/Configurations/`：字段长度、唯一性和关系。
- `eng/Versions.props`、`Directory.Build.props`：版本和框架范围。

实施开始时核实所选标签对应 NuGet 包确已发布，再锁定精确依赖。上游版本升级需要重新检查 Store 合同与目标框架，不自动追随最新预览包。

## 3. 分版本发布方案

建议同一仓库采用两个长期分支，每个分支都只有一套同名工程和一个 `Sharp.OpenIddict.Dapper.slnx`。

| 项目系列 | 建议分支 | NuGet 版本示意 | OpenIddict 依赖 | 发布类库目标 |
| --- | --- | --- | --- | --- |
| 兼容系列 | `release/7.x` | `7.0.0` | 开发/验证锁定 7.7.2；发布依赖范围排除 8.x，含其预览版 | `netstandard2.0;netstandard2.1;net8.0;net9.0;net10.0` |
| 主线系列 | `main` | `8.0.0-preview.1` | 锁定选定的 8.0 预览包；正式版发布后单独升级 | `net10.0` |

包的主版本表达 OpenIddict 兼容系列，次版本不必与上游保持一致。例如扩展 7.0.0 可以依赖 OpenIddict 7.7.2。7.x 发布范围可采用 `[7.7.2,8.0.0-0)`，不能用会允许 8.0 预览版进入的宽范围。8.x 预览阶段精确锁定所选上游预览依赖，扩展也以预览包发布。

不建议在一个程序集里同时引用 OpenIddict 7 和 8，也不通过大量 `#if OPENIDDICT8` 兼容两套公共合同。通用 SQL、序列化、方言和测试修复可以在分支间移植；Store、实体和 DI 注册分别编译。实际创建分支留到实施阶段。

与把两套同名包工程塞进同一个解决方案相比，该方案减少重复工程和打包冲突。代价是公共修复要维护两个分支，CI 必须分别执行。

## 4. NuGet 包与工程布局

| 包名 | 职责 |
| --- | --- |
| `Sharp.OpenIddict.Dapper.Models` | 默认 POCO 实体，不依赖 EF Core 或数据库驱动 |
| `Sharp.OpenIddict.Dapper` | Store 实现、Builder、Options、连接生命周期、查询和序列化 |
| `Sharp.OpenIddict.Dapper.SqlServer` | SQL Server 方言、参数差异、建表与升级脚本 |
| `Sharp.OpenIddict.Dapper.PostgreSql` | PostgreSQL 对应实现 |
| `Sharp.OpenIddict.Dapper.MySql` | MySQL 与 MariaDB 对应实现及分别验收 |
| `Sharp.OpenIddict.Dapper.Sqlite` | SQLite 对应实现 |
| `Sharp.OpenIddict.Dapper.Oracle` | Oracle 对应实现，包含大文本参数处理 |
| `Sharp.OpenIddict.Dapper.Dm` | 达梦独立方言与参数处理 |

provider 引用核心包，核心包引用 Models、OpenIddict.Core 和 Dapper。产品代码不依赖 EF、ABP、Dapper.Contrib 或另一套 ORM，也不额外发布空的 Abstractions 包。

建议目录：

```text
Sharp.OpenIddict.Dapper.slnx
global.json
Directory.Build.props
Directory.Packages.props
src/
  Sharp.OpenIddict.Dapper.Models/
  Sharp.OpenIddict.Dapper/
    Stores/
    Queries/
  Sharp.OpenIddict.Dapper.SqlServer/
  Sharp.OpenIddict.Dapper.PostgreSql/
  Sharp.OpenIddict.Dapper.MySql/
  Sharp.OpenIddict.Dapper.Sqlite/
  Sharp.OpenIddict.Dapper.Oracle/
  Sharp.OpenIddict.Dapper.Dm/
tests/
  Sharp.OpenIddict.Dapper.Tests/
  Sharp.OpenIddict.Dapper.IntegrationTests/
  Sharp.OpenIddict.Dapper.Server.Tests/
samples/
  Sharp.OpenIddict.Dapper.Sample.Server/
  Sharp.OpenIddict.Dapper.Sample.Client/
scripts/
docs/
```

使用 .NET 10 SDK、Nullable、集中包版本管理。7.x 按 TFM 管理 Microsoft.Extensions 等依赖，避免给 net8.0 消费者强行引入另一主版本的依赖体系。Dapper 精确版本在实施前锁定；本机已有 2.1.89，包含 netstandard2.0、net8.0、net10.0 资产，可作为候选而非未经检查的最终选型。

## 5. 驱动与数据库支持范围

provider 包原则上只携带方言、参数适配和 SQL 资源，不强制传递某个 ADO.NET 驱动。连接由应用提供。这样无需为了选择不同驱动或驱动版本而重发存储核心。

| 数据库 | 拟验收版本下限 | 示例/集成测试的驱动选择 |
| --- | --- | --- |
| SQL Server | 2016+ | Microsoft.Data.SqlClient |
| PostgreSQL | 14+ | Npgsql |
| MySQL / MariaDB | 8.0+ / 10.6+ | MySqlConnector；两种服务器分别测试 |
| SQLite | 3.35+ | Microsoft.Data.Sqlite |
| Oracle | 19c+ | Oracle.ManagedDataAccess.Core |
| 达梦 | DM8 | DM.DmProvider；原生模式，另测声明支持的 Oracle 兼容模式 |

这些是计划验收范围，不是已经连接验证的支持声明。达梦不直接复用 Oracle provider，也不因为一条 SQL 能执行就宣布兼容 PostgreSQL 等其他模式。

必须区分“扩展程序集支持某 TFM”和“驱动支持某宿主运行时”。本机 Oracle 23.x 包包含 netstandard2.1/net8.0，Npgsql 10.x 包包含 net8.0/net9.0/net10.0；不能拿它们作为 netstandard2.0 驱动兼容性的证明。

7.x 的存储与方言包保持五个 TFM；宿主选择适配自身运行时的驱动。README 按“OpenIddict 系列 × 扩展 TFM × 宿主 × 驱动版本 × 服务器版本”列出实际验证组合。未验证的旧驱动组合明确标记，不把早期有 netstandard2.0 资产的驱动自动列入正式支持范围。

方言负责引号、参数名、分页、批量删除和参数类型。对 Oracle/达梦大文本、按名称绑定等标准 ADO.NET 不能完整表达的行为，使用 provider 内部的小型适配器，按实际 command/parameter 类型设置公开驱动属性并缓存必要元数据；不引入全局驱动状态。正式支持只覆盖通过真实驱动验证的实现。

## 6. 运行架构与接入 API

调用路径为：OpenIddict Server → 原生 Manager → 本扩展 Store → 数据库方言 → Dapper/ADO.NET → 数据库。

`UseDapper()` 注册默认实体和相应的 Store，沿用 `SetDefault*Entity`、`Replace*Store` 等原生扩展点，保留 `DisableAdditionalFiltering = false`，并调用原生 `DisableEntityCaching()` 设置 `OpenIddictCoreOptions.DisableEntityCaching = true`。不替换协议处理器、Manager 或缓存算法。

原生实体缓存位于 DI 作用域内；批量 Revoke 直接更新 Store，不会逐项移除缓存中的实体。只轮换数据库并发标记无法阻止同一作用域再次返回旧状态。因此首版要求实体缓存保持禁用，对最终生效选项进行校验，重新启用时在首次使用 Manager/Store 前报配置错误，不依赖配置调用顺序。禁用缓存不使已返回的 POCO 自动刷新，也不使撤销成为所有在途请求的同步屏障。

建议提供 `OpenIddictDapperBuilder`、`OpenIddictDapperOptions`、`IOpenIddictDapperConnectionFactory` 和一个窄职责的数据库方言接口。连接工厂接受取消信号并创建可由本扩展释放的新 `DbConnection`。方言实例是无状态的，每个已配置的存储实例绑定自己的方言，进程里不存在“当前数据库”静态变量。

API 示意，尚非已经存在的可调用 API：

```csharp
builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseDapper(dapper =>
        {
            dapper.UsePostgreSql();
            dapper.UseConnectionFactory(_ =>
                new NpgsqlConnection(connectionString));
        });
    });
```

同步委托表示“构造连接”，实际 OpenAsync 在内部执行；同时允许替换连接工厂以接入 NpgsqlDataSource、凭据服务等。外部 DataSource 不归本扩展释放，工厂返回的连接归本次操作释放。首版不接收外部共享 connection/transaction，以免事务所有权含糊。

一个 OpenIddict Core 注册使用一套存储配置；同进程的不同容器/宿主可以使用不同数据库。首版不扩展为多租户分库路由框架。

默认实体采用 string 主键，默认值由应用生成 GUID 字符串，保留显式可空的外键值。不加入 EF 导航属性或延迟加载。类和必要 Store 方法保留继承与替换能力；首版不承诺任意 TKey、任意新增列的自动映射，新增业务数据优先使用 Properties。

## 7. 数据模型与查询索引

7.x 包含四张主表；8.x 再增加 `OpenIddictResources`、`OpenIddictSessions`，以及 Token.SessionId。默认主表名称沿用 OpenIddict 常用名称，但这不表示可以直接操作既有 EF 表。

字段按目标版本 Store/Descriptor 逐项覆盖，包括 ClientSecret、JsonWebKeySet、Permissions、Requirements、Settings、本地化名称、Properties，以及 8.x 的 LoginId、SessionId 等。主键建议保持上游默认最大长度 100，并发标记 50，ClientId 100、Scope/Resource Name 200、Subject 400；其余长度以锁定版本配置为准。

集合和字典保存为 JSON 文本，不依赖数据库原生 JSON 类型。对必须参与查找的集合，增加四张事务维护的辅助索引表：

| 辅助表 | 用途 |
| --- | --- |
| ApplicationRedirectUris | 精确查找包含给定 RedirectUri 的应用 |
| ApplicationPostLogoutRedirectUris | 精确查找包含给定退出回调地址的应用 |
| AuthorizationScopes | 查询包含全部 RequiredScopes 的授权 |
| ScopeResources | 查询包含给定 Resource 名称的 Scope |

JSON 是集合值的权威来源；辅助表只保存去重后的查询索引，主表和辅助表必须在同一事务更新。这样六类数据库都可以使用索引查询，不依赖 `LIKE '%uri%'`、整表 JSON 扫描或各家不同的 JSON 运算符。

四张辅助表统一以所属实体 ID 和 Position 为主键，完整集合值保存为 LargeText，另存 SHA-256 二进制摘要并建立非唯一查找索引。集合值按 Ordinal 去重，不添加长度 200 等原生合同没有的限制，也不以数据库字符串比较定义集合元素身份。摘要只用于缩小候选集，返回前必须与权威 JSON 中的完整值做 Ordinal 比较；摘要碰撞不能造成插入冲突、漏项或误匹配。不对 URI、Scope 或 Resource 值做大小写、尾随空格或其他标准化。

关键约束：ClientId、Scope.Name、Resource.Name、非空 Token.ReferenceId 唯一。SQL Server 等需要为可空唯一键使用合适的过滤索引，允许多个 NULL ReferenceId。标识符列使用满足精确匹配要求的排序规则，同时保留原生 Manager 的附加过滤。

时间统一为 UTC；使用各方言对应的原生时间类型，读出后显式恢复 UTC 语义。SQLite 使用固定格式 UTC 文本。不同数据库的时间精度在合同测试中明确，不能让驱动把 UTC 当成本地时间。Oracle 的空字符串/NULL、大文本 CLOB、Unicode、LOB 读取必须独立测试，不隐式承诺所有可选空串在 Oracle 上无损。

仅提供必要的 schema/table-prefix 配置；标识符先校验再由方言引用，SQL 值全部参数化。不提供任意列重命名、动态实体映射或通用仓储配置系统。

## 8. Store 行为、事务与并发

实现每个目标版本所有 Store 成员，固定查询通过参数化 SQL 在服务器过滤。Get/Set 类成员遵循 OpenIddict 的空值、不可变集合和 CultureInfo 约定，敏感数据按原生 Manager 给出的形式存储，不在 Store 重复散列客户端密钥或引用令牌标识。

Update/Delete 使用 Id 与旧 ConcurrencyToken 进行条件写入，更新成功生成新标记。条件未命中时抛出 OpenIddict 原生 ConcurrencyException。成功提交后才更新内存实体标记；失败回滚不能留下看似已成功保存的实体状态。

包含关联修改时，先在同一事务获得/确认父记录写入权，再修改子表。并发冲突必须回滚全部子表变更，不能先删子表再发现父记录版本不匹配。外键和方言特定锁策略负责阻止关联写入穿过删除窗口。

普通 DeleteAsync 按第 16.2 节的版本矩阵执行。删除 Application 处理直接及间接关联，不能仅按各子表 ApplicationId 删除。删除 Authorization 时，删除所有直接关联 Token；8.x 同时删除关联 Session 及其全部 Token，包括 AuthorizationId 为空或指向其他授权的 Token。单独删除 8.x Session 时，AuthorizationId 为空的关联 Token 删除，AuthorizationId 非空的 Token 保留并清除 SessionId，保留其 Authorization。两种上下文的 Session 关联处理不同，不能复用一套无条件级联规则。Resource 与 Scope.Resources 的名称关系不擅自变成强制级联外键。

凡清空外键但保留的主实体行，必须在同一事务内同时轮换其 ConcurrencyToken，包括单独删除 Session 时保留的 Token，以及第 16.3 节清理路径保留的 Session；旧快照后续 Update 应命中 0 行并抛出原生 ConcurrencyException，而不是重写已删除的外键后触发约束错误。可以批量更新，不要求先加载全部实体；任何失败必须同时回滚外键和并发标记。

Revoke 使用数据库批量条件更新，不先加载全部实体。筛选和计数严格按第 16.2.2 节逐方法定义：Token 的 RevokeBy* 排除已撤销行，通用 RevokeAsync 和 Authorization 的 RevokeBy* 不增加这个隐含条件。仅对实际命中行设置 revoked 并轮换并发标记，防止旧实体随后覆盖撤销结果。实体缓存按第 6 节禁用，提交后通过同一作用域或新作用域的原生 Manager 重新查询均读取数据库中的已提交状态；调用前持有的实体仍是旧快照。通过 Manager/Server 路径验证，不只测试裸 Store SQL；不承诺已开始的验证或请求会被追溯中止。

Prune 完整遵循上游条件，而不是简单删除过期行。例如 Token 还要考虑 CreationDate 阈值、inactive/valid 状态和所属 Authorization 状态；Authorization 清理考虑状态、ad-hoc 类型和是否仍有 Token；8.x Session 也有自己的清理条件。采用有上限的小批次，建议每批不超过 500 个 ID，避开 Oracle IN 参数限制及其他数据库参数上限。删除时重检清理条件、使用事务，按实际影响行数返回；数据库错误和取消向上传播。

默认列表稳定排序并在数据库分页。返回 IAsyncEnumerable 的实现保持真正逐行读取：在低版本目标可使用 Dapper ExecuteReaderAsync、GetRowParser 与 DbDataReader.ReadAsync；连接/reader 存活到枚举结束，取消和提前退出都必须释放。不能把缓冲 QueryAsync 的完整结果包装成“流式”。驱动内部是否真正异步由真实驱动验证，不用 Task.Run 掩盖同步实现。

## 9. IQueryable 回调：明确而有限的翻译能力

Dapper 不提供 LINQ provider；OpenIddict 的 CountAsync/GetAsync/ListAsync 却接受 IQueryable 回调。设计必须承认这个差异。

比较过三种方式：整表加载后 AsQueryable 会导致内存和延迟失控；全部抛 NotSupportedException 会让常用原生管理接口不可用；引入另一套 ORM 会增加依赖和双重映射。建议实现仅覆盖本存储单表查询的小型翻译器。

首版支持：

- Where：有界字符串、标识符和日期等可比较映射列的等值、不等值、范围、空值判断、AND/OR/NOT；LargeText/Json 仅允许空值判断，不支持内容比较。
- 参数和闭包常量；标量列表 Contains 转为有参数上限保护的 IN，并正确处理空列表。
- OrderBy/ThenBy 仅接受支持排序的有界列和日期列；Skip/Take 无排序时补稳定主键排序。
- Select：实体、单个映射标量、映射标量组成的简单 DTO/匿名对象。
- Count/Get/List 在上述查询结果上执行，保持 Skip/Take/Select 对结果的语义；Get 取首项。
- 原生非泛型 Manager 所需的 object 转换/实体类型转换，不能只测试直接调用泛型 Store。

首版不支持 Join、GroupBy、导航展开、任意方法执行、JSON 内部属性和集合表达式，也不支持 LargeText/Json 的内容比较、排序和 IN。它们仍可直接投影返回。此限制对六类数据库一致，避免 Oracle NCLOB 不能直接使用普通等值/ORDER BY 的差异；不以截断 LOB 后比较代替完整值语义。无法翻译时在执行 SQL 前抛出明确的 NotSupportedException，并列明不支持的表达式与列。绝不静默退化为内存执行。

该组件不是通用 LINQ ORM。采用白名单表达式、列名来自固定映射、值全部参数化；null、排序、分页和投影都有合同测试。额外复杂的报表由应用自己的 Dapper 查询承担。README 必须给出支持与拒绝示例，不能宣称兼容任意 IQueryable。

## 10. 建库与版本升级

每个 provider 提供可审阅的版本化建表、索引和升级 SQL，并随包交付；脚本有独立的存储架构版本记录。默认运行服务不自动建表、不自动修改生产 schema，不额外实现迁移引擎。应用可以用现有部署工具执行脚本。

8.x 的新安装脚本建立六张主表和所需辅助表。7.x → 8.x 提供单独升级脚本，保留既有 ID、令牌数据、JSON 和集合索引，增加 Resource/Session 结构及相关外键。Scope.Resources 的已有完整值保持不变，它们没有 Resource.Name 的长度 200 限制，也不强制对应独立 Resource 实体。升级脚本不从这些值自动创建 Resource 记录；应用如需导入，须提供符合独立实体 Name 合同的明确映射和元数据，不能截断、合并或猜测名称。

升级前备份并停止并发旧版本写入；不承诺 7.x 与 8.x 对同一 schema 的混合滚动写入或自动降级。已有 EF/MongoDB 数据转换另行评估，不把本扩展的创建脚本描述成官方 EF 数据库的无缝替代。

## 11. 测试与验收

全部测试项目目标为 net10.0，建议 xUnit。共享同一组 Store 合同测试数据和断言，通过 provider fixture 分别执行，而不是复制六份断言。

| 测试层 | 重点 |
| --- | --- |
| 单元测试 | DI 注册、最终选项校验及重新启用实体缓存的拒绝、JSON/本地化转换、参数化、LINQ 翻译与拒绝、取消和资源释放 |
| Store 合同集成测试 | 每个成员、唯一性、空值、Unicode、长文本、集合精确匹配、UTC、更新/删除并发、事务回滚；集合包含超过 200 字符、大小写及尾随空格不同的值 |
| 生命周期专项 | 不同状态与阈值的 Prune、逐方法 Revoke 筛选/计数/标记轮换、删除竞态、8.x Session/Resource；强制摘要碰撞时仍可同时保存并精确查询；全部 RequiredScopes 的 Ordinal 匹配 |
| Manager/Server 测试 | 原生 Manager 接入、非泛型查询回调、Client Credentials、Code + PKCE、刷新、撤销和失效验证；同一作用域先读再批量撤销后重读、新作用域重读及旧快照 CAS 冲突；删除 Authorization 后仅绑定其 Session 的令牌失效 |
| 包与框架检查 | 每个类库 TFM 独立构建，nupkg 包含正确资产和依赖，最小消费者引用验证 |
| 迁移测试 | 空库初始化、脚本版本检查、已有 7.x 数据升级至 8.x、失败后的可诊断状态 |

数据库集成测试采用 SQLite 临时文件，以及 SQL Server、PostgreSQL、MySQL、MariaDB、Oracle、达梦的独立测试数据库。可以用现有容器或外部连接串，Oracle/达梦可采用自托管 CI。测试只能修改专用测试 schema，不能对用户现有数据库执行清理。

缺少数据库环境时，本地明确显示跳过；正式支持某 provider 的发布门禁则要求它的真实数据库测试成功，不能以跳过充当通过。MySQL 成功不替代 MariaDB 验证，DM 原生模式成功不替代 Oracle 兼容模式验证。

netstandard 不是可执行运行时；net10 测试成功也不等于已经在 net8/net9 运行。按用户要求，常规测试宿主保持 net10；7.x 的五目标构建和消费者检查作为编译兼容性证据，运行时支持声明另标注实际验证宿主。若以后要求 net8/net9 原生运行证明，再增加专用宿主验证，不能用包可还原代替。

删除专项必须覆盖第 16.2.1 节的全部矩阵，特别是：Token.ApplicationId 为空或与 Authorization/Session 所属应用不同；Application 所属 Session 引用 ApplicationId 为空或属于其他应用的 Authorization；待删 Authorization 关联的其他应用 Session；Token 只绑定待删 Session、AuthorizationId 为空，或同时绑定另一 Authorization。验证选定集合完整删除，集合外的父实体保留；单独删除 Session 时保留 Token 的外键清空、标记轮换及旧快照更新抛出原生并发异常；根记录 CAS 冲突、关联写入竞争和中途失败时，删除、外键、辅助表及并发标记一起回滚。Prune 使用独立 fixture 验证不会复用普通删除而删除 Session 下的 Token。

撤销专项必须逐方法覆盖无匹配行、valid/revoked/NULL 混合状态、可选状态筛选、重复调用、实际影响行数、命中行标记轮换和未命中行标记不变；第 16.2.2 节的计数示例均为必需断言。不得将通用 RevokeAsync、Token RevokeBy* 和 Authorization RevokeBy* 绑定到相同的隐含状态过滤器。

最低发布验收包括：目标版本全部 Store 成员可调用；声明支持的查询子集通过；真实数据库完成长 Token.Payload、LOB、唯一性、集合边界、事务并发和清理测试；通过原生 Manager 完成同一及跨作用域的撤销后重新查询、逐方法撤销计数与旧快照冲突验证；上述删除专项全部通过，包括仅绑定 Session 的令牌在所属授权普通删除后被拒绝；无全表内存 LINQ 回退；包不带 EF/ABP 依赖；SQL 和异常不泄露客户端密钥、令牌正文或连接凭据。

## 12. 示例与文档

提供两个 net10 示例，避免为六种数据库复制六套服务器：

- Sample.Server：ASP.NET Core + OpenIddict + 本扩展，默认 SQLite，可通过配置切换其他 provider；通过原生 Manager 初始化应用、Scope，8.x 另演示 Resource/Session。
- Sample.Client：演示 Client Credentials，以及 Authorization Code + PKCE、刷新和撤销；交互登录使用明确标注的开发演示身份，不把此扩展包装成用户管理系统。

示例的服务器证书/演示凭据仅用于开发，连接串通过环境变量或 user-secrets 提供。文档给出各数据库所需驱动、建库脚本、运行步骤、查询限制、实体缓存禁用要求和生产部署所需配置，说明撤销后须重新查询而非检查旧 POCO。提供 .http 请求便于快速验证；无需增加管理后台。

## 13. 实施顺序与交付

1. 锁定 7.x/8.x 的实际发布依赖与完整接口清单。先对 Oracle/达梦做小范围真实驱动验证，确认绑定、大文本、UTC、分页和事务，尽早暴露六数据库共同设计的障碍。
2. 完成 8.x 主线的工程布局、Models、DI、连接工厂及 SQLite 六类 Store，用合同测试建立基线。
3. 实现有限 LINQ 翻译和四张集合索引表，验证 Manager/Server 接入及事务并发。
4. 接入其余五类 provider，MySQL/MariaDB 分别验收，完成各自 schema 脚本和驱动适配。
5. 建立 7.x 兼容分支，按四类 Store 和旧接口对接，完成五目标构建及对应数据库合同测试。
6. 完成两个 net10 示例、7→8 升级测试、README、兼容矩阵和 CI 打包验证，形成 7.x 正式候选与 8.x 预览候选。

每一步都以具体行为与测试作为完成标准；不以工程数量、模拟 SQL 测试成功或 SQLite 成功代替六类数据库支持。发布 NuGet 属于后续单独操作，本轮只交付设计。

首版不增加通用仓储、可插拔 ORM、多租户路由、任意主键映射、管理后台、分布式缓存或通用 LINQ/迁移引擎。最主要的风险是 8.x 预览接口变化、旧宿主的驱动兼容性，以及 Oracle/达梦的实际参数和事务行为。

## 14. 公共 API 与组件合同

### 14.1 命名空间与实体

产品命名空间使用 `Sharp.OpenIddict.Dapper`、`Sharp.OpenIddict.Dapper.Models` 和对应 provider 子命名空间。DI 扩展方法放在 `Microsoft.Extensions.DependencyInjection`，遵循 OpenIddict 现有接入方式。

默认实体命名为 `OpenIddictDapperApplication`、`OpenIddictDapperAuthorization`、`OpenIddictDapperScope`、`OpenIddictDapperToken`；8.x 另含 `OpenIddictDapperResource`、`OpenIddictDapperSession`。Store 相应命名为 `OpenIddictDapperApplicationStore<TApplication>` 等。

实体是存储 POCO：日期属性为 `DateTime?`，只接受/返回 UTC 语义；外键为 `string?`；集合、字典与 JWK 集合的底层属性为 JSON `string?`。Store 负责在 JSON 与原生接口要求的 ImmutableArray、ImmutableDictionary、JsonWebKeySet 之间转换。不要照搬 EF 导航属性，也不要直接依赖其 Models 程序集。

每类实体均有 string Id 与 ConcurrencyToken，默认构造时分别生成新的 GUID 字符串。公共可继承实体和虚方法保留类型扩展点；调用者扩展实际数据库列时，需要替换对应 Store 并提供 DDL，不能认为继承一个属性就会自动保存它。

### 14.2 Builder

首版公共配置面限定如下，正式签名在对应版本编译时落实：

| API | 合同 |
| --- | --- |
| `UseDapper()` / `UseDapper(Action<OpenIddictDapperBuilder>)` | 在 OpenIddictCoreBuilder 上注册本扩展，调用原生 DisableEntityCaching；幂等，不重复注册同一服务 |
| `UseSqlServer()` 等六个 provider 方法 | 安装对应方言；同一注册选择多个不同 provider 时启动失败，不以最后一次调用静默覆盖 |
| `UseConnectionFactory(Func<IServiceProvider, DbConnection>)` | 每次操作获取新连接，默认实现负责调用 OpenAsync 与释放 |
| `UseConnectionFactory<TFactory>()` | 注册实现 IOpenIddictDapperConnectionFactory 的连接工厂 |
| `SetSchema(string?)` | 设置独立 schema；SQLite/MySQL 不支持的 schema 语义须拒绝，不能静默忽略 |
| `SetTablePrefix(string)` | 默认 OpenIddict；应用于主表、辅助表及架构版本表，长度和合法性由 provider 校验 |
| `SetCommandTimeout(int?)` | 默认 null，遵循驱动/Dapper 的默认值；显式值必须为正秒数 |

连接工厂的最小合同为 `ValueTask<DbConnection> CreateConnectionAsync(CancellationToken cancellationToken)`。返回的连接尚未打开且由调用方拥有，不能返回应用其他代码正在使用的共享连接。

Options 只包含 provider、连接工厂、schema、table prefix、command timeout。JSON 格式、查询语义、清理合法性和并发控制不是可随意关闭的选项。缺少 provider/工厂、非法超时或非法表名在首次使用前给出明确配置错误。另校验最终生效的原生 OpenIddictCoreOptions，要求 DisableEntityCaching 为 true；无论调用者在 UseDapper 前后配置，只要最终重新启用实体缓存就必须拒绝，不能以自定义缓存失效或替换 Manager 弥补。

Store 默认 Scoped；无状态方言可 Singleton；连接不注册为 Singleton，也不让单个连接跨异步枚举并发复用。工厂与服务生命周期遵循 DI 原则，不由 Singleton 捕获 Scoped 服务。

### 14.3 内部职责

核心内部只共享连接执行、JSON 转换、固定实体列映射和单表查询表达式处理。方言接口覆盖标识符引用、参数占位符、参数赋值、分页、写入/删除锁、限量清理 SQL 和架构脚本。业务状态规则仍在 Store，不能分散为六份 provider 业务代码。

查询表达式解析先形成受支持的操作序列，再由方言产生 SQL 与参数。保持原查询算子顺序；例如 Take 之后的 Where，不能直接改写成先 Where 再 Take。需要嵌套子查询才能保持语义时应生成子查询；无法正确处理的组合按公开限制拒绝，不能返回近似结果。

## 15. 持久化数据字典

### 15.1 公共列和逻辑类型

所有主表包含 `Id: Identifier(100), NOT NULL, PK` 与 `ConcurrencyToken: Text(50), NOT NULL`。除特别说明外，以下业务列可空；Manager 负责业务必填验证，数据库仍承担主键、外键、唯一约束和长度等完整性校验。

逻辑类型：Identifier/Text(n) 为有界字符串；LargeText 为无短长度限制的 Unicode 文本；Json 为 LargeText 中的合法 JSON；UtcDateTime 为可空 UTC 时间。JSON 空集合与 null 在原生 Getter 中按接口约定返回空集合，非法 JSON 抛出数据异常，不悄悄返回默认值。

### 15.2 主表列

| 表 | 除公共列外的列 |
| --- | --- |
| Applications | ApplicationType Text(50)、ClientId Text(100)、ClientSecret LargeText、ClientType Text(50)、ConsentType Text(50)、DisplayName LargeText、DisplayNames Json、JsonWebKeySet Json、Permissions Json、PostLogoutRedirectUris Json、Properties Json、RedirectUris Json、Requirements Json、Settings Json |
| Authorizations | ApplicationId Identifier(100)、CreationDate UtcDateTime、Properties Json、Scopes Json、Status Text(50)、Subject Text(400)、Type Text(50) |
| Scopes | Description LargeText、Descriptions Json、DisplayName LargeText、DisplayNames Json、Name Text(200)、Properties Json、Resources Json |
| Tokens | ApplicationId Identifier(100)、AuthorizationId Identifier(100)、CreationDate UtcDateTime、ExpirationDate UtcDateTime、Payload LargeText、Properties Json、RedemptionDate UtcDateTime、ReferenceId Text(100)、Status Text(50)、Subject Text(400)、Type Text(150)；8.x 另有 SessionId Identifier(100) |
| Resources，8.x | Description LargeText、Descriptions Json、DisplayName LargeText、DisplayNames Json、Name Text(200)、Properties Json |
| Sessions，8.x | ApplicationId Identifier(100)、AuthorizationId Identifier(100)、CreationDate UtcDateTime、LoginId Text(100)、Properties Json、Status Text(50)、Subject Text(400) |

ApplicationId 外键指向 Applications；AuthorizationId 指向 Authorizations；Token.SessionId 指向 Sessions。所有外键默认使用限制删除，由 Store 明确执行关联处理，避免不同数据库对级联路径的支持差异。

### 15.3 辅助索引表

辅助表的以下各列均为 NOT NULL；所属实体 ID 为 Identifier(100) 并建立限制删除外键，摘要为 Hash32，Position 为非负整数。完整值不进入主键或唯一键。

| 表 | 列 | 主键 | 非唯一查找索引 |
| --- | --- | --- | --- |
| ApplicationRedirectUris | ApplicationId、UriHash、Uri LargeText、Position | (ApplicationId, Position) | (UriHash, ApplicationId) |
| ApplicationPostLogoutRedirectUris | ApplicationId、UriHash、Uri LargeText、Position | (ApplicationId, Position) | (UriHash, ApplicationId) |
| AuthorizationScopes | AuthorizationId、ScopeHash、Scope LargeText、Position | (AuthorizationId, Position) | (ScopeHash, AuthorizationId) |
| ScopeResources | ScopeId、ResourceHash、Resource LargeText、Position | (ScopeId, Position) | (ResourceHash, ScopeId) |

重复值按 StringComparer.Ordinal 去重后赋 Position，辅助表顺序不覆盖 JSON 内原始顺序。摘要统一由应用对未经标准化的完整字符串 UTF-8 字节计算 SHA-256，不依赖数据库编码、排序规则或哈希函数。索引只筛选候选所属实体，再按其权威 JSON 中的完整值作 Ordinal 复核，并保证同一实体只返回一次。不同完整值即使摘要相同也各占一行；不得按摘要去重或添加摘要唯一约束。

Scope/Resource 集合值不限定为 200 字符，且 `api` 与 `api ` 是两个不同元素；这些值与主表 Scope.Name/Resource.Name 的有界列合同分开。不能依赖二进制排序规则解决 SQL Server 字符串等值比较的尾随空格折叠，也不能在插入失败后用查询复核补救。

### 15.4 必需索引

| 主表 | 约束/索引 |
| --- | --- |
| Applications | ClientId 非空值唯一 |
| Authorizations | (ApplicationId, Status, Subject, Type)；按 Subject 查找索引；按 CreationDate 清理索引 |
| Scopes | Name 非空值唯一 |
| Tokens | ReferenceId 非空值唯一；(ApplicationId, Status, Subject, Type)；AuthorizationId；Subject；CreationDate/ExpirationDate；8.x SessionId |
| Resources | Name 非空值唯一 |
| Sessions | LoginId；ApplicationId；AuthorizationId；Subject；CreationDate |

索引总键长须在每种服务器字符集下检查。URI、集合完整值和 Payload 不直接进入长字符串索引键。DDL 中的索引、约束名称按 provider 长度上限生成，不能让自定义前缀导致截断重名。

### 15.5 物理类型映射

| 逻辑类型 | SQL Server | PostgreSQL | MySQL/MariaDB | SQLite | Oracle | DM8 |
| --- | --- | --- | --- | --- | --- | --- |
| Text(n) | nvarchar(n) | varchar(n) | varchar(n)，utf8mb4 | TEXT，长度校验 | nvarchar2(n) | nvarchar(n) |
| LargeText/Json | nvarchar(max) | text | longtext，utf8mb4 | TEXT | nclob | clob，Unicode 字符集验收 |
| Hash32（UriHash/ScopeHash/ResourceHash） | binary(32) | bytea，校验 32 字节 | binary(32) | BLOB，校验 32 字节 | raw(32) | varbinary(32) |
| UtcDateTime | datetime2(6) | timestamp(6) without time zone | datetime(6) | 固定格式 UTC TEXT | timestamp(6) | timestamp(6) |

统一最低保存到微秒，写入前规范化到微秒精度；不依赖服务器本地时区或自动 CURRENT_TIMESTAMP。JSON、Payload 和 Unicode 大文本通过参数绑定写入，超过 4 KB、32 KB 等驱动阈值都要有测试。

## 16. 查询、变更和错误的确定语义

### 16.1 查找规则

- FindById/ClientId/Name/ReferenceId/Subject 等指定键方法拒绝 null/空字符串，找不到返回 null 或空序列，按各原生接口返回类型处理。
- FindAsync/RevokeAsync 的可选筛选项为 null 或空串时不应用该筛选，与锁定上游行为一致；不能误写为 `column = NULL`。
- FindByNames 的空列表返回空序列，不执行无筛选查询。RequiredScopes 为 null/default/空时不限制 Scope；否则授权必须包含全部不同的请求 Scope，而不是任意一个。
- RequiredScopes 按 Ordinal 去重，通过摘要索引筛选候选，再逐项核对授权 JSON 是否包含全部完整请求值。摘要相等或 COUNT(DISTINCT hash) 不能替代最终集合包含判断；强制碰撞时仍须区分每个请求值。该固定 FindAsync 不带分页，候选可流式读取并复核，无须加载整表；集合表达式不加入通用 IQueryable 翻译范围。不能在分页之后过滤集合而破坏分页合同。
- URI/Resource 等集合查找同样使用摘要候选与完整 JSON 的 Ordinal 复核，数据库字符串排序规则不能改变集合身份。
- 固定查找保留上游附加过滤；不把数据库默认不区分大小写的结果当成最终协议比较结果。
- 默认 List 按 Id 排序；count=0 返回空序列；负 count/offset 拒绝；offset 无 count 由方言生成正确 SQL。
- 参数集合过大时，在保持算子语义的前提下分块；无法分块的查询给出明确参数上限错误。不得截断输入或把值拼进 SQL。

### 16.2 原子写入顺序

创建集合实体时：开始事务 → 插入主表 → 插入去重后的辅助索引 → 提交。任一失败均回滚。

更新时：生成候选新并发标记 → 以 Id/旧标记条件更新主表 → 验证影响行数为 1 → 重建受影响的集合索引 → 提交 → 更新内存实体标记。0 行即并发异常；回滚后仍保留旧实体标记。

删除时：在事务内按 Id/旧标记取得根记录写入权 → 按以下规则处理子记录与辅助表 → 删除根记录并确认 CAS 成功 → 提交。相关 Authorization/Session 也须取得方言所需的写入锁，保证间接关联的并发写入受保护；不得只锁 Application 就假设所有间接关系不再变化。并发创建子项要么发生在删除之前并被正确处理，要么因外键/锁竞争失败；不允许产生孤儿或返回假成功。

#### 16.2.1 普通删除与关联失效

8.x 的关联失效以锁定的 8.0.0-preview.4 EF Core 非批量 DeleteAsync 路径为参照：Authorization 删除会删除其 Session 和会话 Token；Application 删除也处理所属 Session 引用的 Authorization。参考方法为 OpenIddictEntityFrameworkCoreAuthorizationStore.DeleteAsync 和 OpenIddictEntityFrameworkCoreApplicationStore.DeleteAsync。该预览版的批量与非批量路径处理范围不完全一致，本项目统一采用下表的合同，不提供切换删除语义的批量操作选项。

| 普通删除入口 | 必须删除 | 必须保留/清空 |
| --- | --- | --- |
| Application，7.x | Application、其 Authorization、直接或经这些 Authorization 关联的 Token、相应辅助行 | 集合外实体保留 |
| Application，8.x | Application、下述 A/S/T 集合及相应辅助行 | 集合外实体保留，其他 Application 主记录保留 |
| Authorization，7.x | Authorization、所有 AuthorizationId 指向它的 Token、AuthorizationScopes | 其他父实体保留 |
| Authorization，8.x | Authorization、所有 AuthorizationId 指向它的 Session、所有直接关联 Token 及这些 Session 的全部 Token、AuthorizationScopes | 不因 Token 引用其他 Authorization 就删除该其他 Authorization；其他 Application 保留 |
| Session，8.x，单独删除 | Session、SessionId 指向它且 AuthorizationId 为空的 Token | 保留 Session 的 Authorization；其余关联 Token 清空 SessionId 并轮换标记 |
| Token，两系列 | 该 Token | Application、Authorization、Session 保留 |
| Scope，两系列 | Scope 及其 ScopeResources 辅助行 | Resource 实体保留 |
| Resource，8.x | 该 Resource | Scope 及其 Resources JSON/辅助行保留 |

Application 删除使用以下集合，全部在同一连接、同一事务及上述锁保护下确定并处理：

- A0：ApplicationId 等于待删 Application.Id 的所有 Authorization。
- S0：ApplicationId 等于待删 Application.Id 的所有 Session；7.x 为空集。
- A：A0 加上 S0 中非空 AuthorizationId 指向的所有 Authorization，包括 ApplicationId 为空或属于其他应用的授权。
- S：S0 加上所有 AuthorizationId 属于 A 的 Session；7.x 为空集。
- T：ApplicationId 等于待删 Application.Id，或 AuthorizationId 属于 A，或 SessionId 属于 S 的所有 Token。各条件取并集，不要求 Token 的其他外键与所属应用一致。

将 A 对应的其他 Session 纳入 S，是本项目为统一授权删除失效语义和限制外键处理明确补足的规则；不宣称 EF 预览版所有路径已实现相同集合。集合按上面列出的关系计算，不沿 Token 外键反向扩展 A 或 S，也不删除其他 Application 主记录。

执行顺序固定为：

1. 确认根记录旧并发标记并取得 A/S 中记录所需的写入锁，确定受保护的关联集合。
2. 删除 T，再删除 S；7.x 省略 Session 步骤。
3. 删除 A 的 AuthorizationScopes 辅助行，再删除 A。
4. 删除该 Application 的两张 URI 辅助表行，再按根记录旧并发标记条件删除 Application，确认影响 1 行后提交。

因此，Token.ApplicationId 为空但 AuthorizationId 属于 A 的 Token、仅绑定 S 的 Token，以及 AuthorizationId 在 A 之外但 SessionId 属于 S 的 Token 均会删除。最后一种情况只删除该 Token，其引用的外部 Authorization 保留。Application 或 Authorization 的关联删除不采用单独删除 Session 时保留授权 Token 的规则。

单独删除 Authorization 时：取得根记录写入权；8.x 确定并保护所有 AuthorizationId 指向它的 Session；删除 AuthorizationId 指向它或 SessionId 属于这些 Session 的 Token；删除这些 Session；删除 AuthorizationScopes；按根记录旧标记删除 Authorization 并提交。7.x 省略 Session 条件和步骤。即使 Token.AuthorizationId 为空、ApplicationId 为空，或 Token 另有一个不同的 Authorization，也不能漏掉会话关联的 Token。

单独删除 Session 时：取得根记录写入权；删除关联且 AuthorizationId 为空的 Token；对其余关联 Token 清空 SessionId 并轮换标记；按根记录旧标记删除 Session 并提交。该入口不删除 Session 引用的 Authorization。所有入口都必须验证根记录删除影响行数为 1，失败则回滚。

协议验收场景固定为：S.AuthorizationId=A，T.SessionId=S、T.AuthorizationId=NULL，S/T 均为 valid；普通删除 A 后 S/T 必须不存在，携带该 SessionId 的原有令牌在启用对应存储校验的 Server 路径中必须被拒绝。禁用实体缓存仍不追溯中止已经完成校验的在途请求。

上述流程通过共享内部 SQL/事务辅助方法实现，不调用会另开连接或独立提交的公共 Store DeleteAsync。保留行的外键清空与并发标记设置必须是同一 UPDATE；一次批量语句可为受影响行设置同一新 GUID 标记，并发标记不要求跨行唯一。任何根记录版本冲突、约束错误或中途失败均回滚全部关联变更；成功后重新查询获取新状态，旧快照的更新必须返回 0 行并转换为原生 ConcurrencyException。

#### 16.2.2 批量撤销的逐方法合同

以下规则对 7.x/8.x 均适用，以对应标签的 EF Core Store 筛选谓词为依据。这里的 client 参数表示 Application 主键，不是 ClientId。可选参数为 null/空串时不应用相应条件；RevokeBy* 的指定键必须非 null/空串。

| Store/方法 | 完整筛选条件 | 已 revoked 的匹配行 |
| --- | --- | --- |
| Token.RevokeAsync(subject, client, status, type) | 所有非空可选参数对应 Subject/ApplicationId/Status/Type 的等值条件相与 | 不自动排除，满足完整筛选即更新并计数 |
| Token.RevokeByApplicationIdAsync(id) | ApplicationId=id，且 Status 非 revoked | 排除，不更新、不计数 |
| Token.RevokeByAuthorizationIdAsync(id) | AuthorizationId=id，且 Status 非 revoked | 排除，不更新、不计数 |
| Token.RevokeBySubjectAsync(subject) | Subject=subject，且 Status 非 revoked | 排除，不更新、不计数 |
| Authorization.RevokeAsync(subject, client, status, type) | 所有非空可选参数对应 Subject/ApplicationId/Status/Type 的等值条件相与 | 不自动排除，满足完整筛选即更新并计数 |
| Authorization.RevokeByApplicationIdAsync(id) | ApplicationId=id | 更新并计数 |
| Authorization.RevokeBySubjectAsync(subject) | Subject=subject | 更新并计数 |

“Status 非 revoked”包含 Status 为 NULL，SQL 必须将条件分组为 `AND (Status <> @revoked OR Status IS NULL)` 等等价谓词，使 NULL 分支仍受 ApplicationId/AuthorizationId/Subject 条件约束。显式 status 筛选只匹配相等值，不包含 NULL；省略 status 时，通用 RevokeAsync 也包含其他条件匹配的 NULL 状态行。三个 Token.RevokeBy* 的额外排除规则不能加入通用撤销或 Authorization.RevokeBy*。

每次操作使用单条条件 UPDATE，仅对满足该方法完整谓词的行设置 Status=revoked 和新的 ConcurrencyToken，并返回实际命中更新的行数。已 revoked 但依该方法仍命中的行也须轮换标记；被排除的行保留原标记。可以为一次语句的全部命中行设置同一新 GUID，不能要求并发标记跨行唯一。该标记轮换是本扩展防止旧快照覆盖的明确保证，EF Core 参考批量语句本身只设置 Status，不能据此宣称实现细节完全相同。不同驱动的匹配行/变化行差异通过参数适配和真实数据库测试统一。

必需计数示例：同一 Subject 下有一个 valid 和一个 revoked Token，Token.RevokeBySubjectAsync 首次返回 1、再次返回 0；对同样初始数据调用不带 status 筛选的 Token.RevokeAsync，两次均返回 2。对同样初始状态的 Authorization 调用 RevokeBySubjectAsync，两次均返回 2。显式 status=valid 的通用撤销首次返回 1、再次返回 0。另加入 NULL 状态行验证它在无 status 筛选和 Token.RevokeBy* 中被命中，在显式 status=valid/revoked 中不被命中；其他指定键不匹配的 NULL 状态行始终不受影响。每个示例同时断言命中行/未命中行的并发标记变化。

这些方法只更新目标 Store 的实体，不隐式级联撤销另一类实体；授权、令牌及会话的后续协议校验仍由原生 Manager/Server 负责。Session Store 不新增原生合同没有的批量撤销成员。

### 16.3 清理条件

每次 Prune 捕获一个 UTC now，循环至没有可删项或达到规定批次数上限。只处理 CreationDate 严格早于 threshold 的行，CreationDate 为 null 不清理。候选按 Id 稳定选择，每批至多 500 条，单次至多 1,000 批。

| 对象 | 满足阈值后还必须满足的条件 |
| --- | --- |
| Token | 状态既非 inactive 也非 valid；或者确有关联 Authorization 且其状态非 valid；或者 ExpirationDate 严格早于 now |
| Authorization | 状态非 valid 或 Type 为 ad-hoc，并且不存在关联 Token |
| Session | 状态非 valid，并且不存在关联 Token |

“状态非 valid”等不等比较必须包含应视为不相等的 NULL，不能直接依赖 SQL 三值逻辑的 `<>`/NOT IN。

Authorization/Session 清理必须在事务与相应锁保护下重新验证完整条件，尤其是不存在关联 Token；最终 DELETE 仍携带完整清理谓词，并由限制删除的外键兜底。Prune 使用独立清理路径，不调用第 16.2.1 节会删除会话及其 Token 的普通 DeleteAsync，也不为了使父记录可删而删除并发出现的 Token。

Authorization 的“不存在关联 Token”指不存在 AuthorizationId 指向它的 Token。清理符合条件的 Authorization 时，保留其 Session，并在同一事务内清空这些 Session 的 AuthorizationId、轮换其并发标记，保留 Session 的状态及其 Token；这延续本项目清理路径的显式外键处理策略。Prune 是受谓词限制的清理，不承担普通 DeleteAsync 的主动关联失效合同；测试须覆盖只绑定 Session 的 Token 不被清理路径误删。对最终不再满足清理条件的候选，不能提交外键清空等中间变更。独立清理路径与普通删除路径的差异必须在使用文档中说明。

每批独立提交，删除时必须复核完整条件；并发标记只能作为附加检查，因为新建关联 Token 不会改变父记录标记。条件已不满足则保留该项；并发外键冲突等失败回滚该批并向上传播，之前已提交的批次不伪装成整次回滚。无自动重试未知写入、无忽略异常继续报告成功。

### 16.4 错误与诊断

参数错误、配置错误、并发冲突、约束错误、取消和数据库不可用要可区分。并发冲突转换为原生 ConcurrencyException；取消保留 OperationCanceledException；其他驱动错误保留原始异常链，不统一改成“未找到”或“认证失败”。

日志只记录 provider、操作名、耗时、影响行数和必要的错误分类，不记录连接串、ClientSecret、Payload、访问/刷新令牌、完整参数或授权主体个人数据。首版使用现有 ILogger，不另建诊断框架。

## 17. 架构版本与升级合同

每套表前缀有独立的架构版本表，逻辑名为 `{prefix}SchemaVersions`；单行记录 Series（7/8）、Version（从 1 递增）和 UpdatedUtc。prefix 默认 OpenIddict；表所属 schema 与主表一致。

provider 脚本目录按系列与版本组织，至少包含新安装脚本，以及后续明确的 upgrade 脚本。7→8 跨系列脚本单独命名，最后在结构和数据步骤成功后更新版本记录。Oracle/DM 等 DDL 可能隐式提交，不能声称外层事务可回滚所有迁移。

首次实际使用存储时检查 Series/Version，结果只缓存在该存储配置的生命周期内。数据库版本不符则拒绝操作并给出所需脚本版本；不自动修改数据库，不继续对不兼容表结构写入。更换数据库或升级 schema 后重启应用，不能用同一配置的连接工厂在不同 schema 之间轮换。

跨系列升级步骤固定为：备份 → 停止旧写入 → 校验当前系列/架构版本 → 执行已审阅脚本 → 验证数据与约束 → 部署新系列 → 运行冒烟测试。失败恢复按数据库实际已提交的 DDL 处理，不自动清除 dirty 状态或强制标记成功。

## 18. 构建、持续集成和发布合同

所有项目加入 `.slnx`，通过 `.NET 10 SDK` 构建；SDK 精确版本在 global.json 固定，允许同 feature band 的安全补丁更新。常规入口包括 Release build、test、pack 与格式/差异检查。

CI 分三个阶段：

1. 构建每个目标 TFM，执行纯单元测试和 SQLite 合同测试，检查包内容、依赖、公开 API 与最小消费者。
2. 在专用环境执行全部声明支持的服务器/驱动组合，以及 Manager/Server、迁移、事务与并发测试。Oracle/DM 自托管任务也是发布必需结果。
3. 只有前两阶段成功才产生可发布 nupkg/snupkg 候选；两个系列独立版本化，所有同系列子包使用相同版本并精确依赖同版核心/Models。

普通 PR 可在缺少部分外部数据库时给出明确缺口，但不能获得“六数据库已验证”的发布结论。实际发布到 NuGet 由单独发布操作完成，凭据由 CI secrets 提供，不放入仓库。

测试进程虽统一 net10，针对 netstandard2.0/2.1 编译资产仍应有独立加载/调用验证，覆盖低 TFM 的读取、释放与异步接口路径。该验证说明“此资产在 net10 宿主下运行”，不能改写成已验证所有旧宿主。

## 19. 需求与验收追踪

| 需求 | 对应实现 | 必需证据 |
| --- | --- | --- |
| Sharp 包名前缀 | 第 4、14 节 | 包与程序集名称检查 |
| OpenIddict 7/8 分版本 | 第 2、3、17 节 | 两系列真实接口编译、不同 schema 误用拒绝、7→8 升级测试 |
| 五个目标框架 | 7.x 所有发布类库 | 五 TFM 构建、包资产与消费者检查；实际运行宿主另列 |
| 六类数据库 | 六 provider、公共 Store | 七种服务器（含单独 MariaDB）的合同测试；DM 声明模式测试 |
| 原生协议接入 | Manager/Server 及示例，实体缓存禁用 | 发令牌、PKCE、刷新、同一及跨作用域撤销后重读；授权普通删除后仅绑定 Session 的令牌被拒绝；重新启用缓存被拒绝 |
| .NET 10 测试/示例 | tests、samples | net10 运行，全部工程由 slnx 管理 |
| 无内存整表回退 | 固定 SQL、有限查询翻译器 | 不支持表达式拒绝、分页与大结果枚举测试 |
| 集合原生语义 | JSON、摘要候选、完整值 Ordinal 复核 | 超过 200 字符、大小写/尾随空格区分、强制摘要碰撞、全部 RequiredScopes 匹配 |
| 逐方法撤销合同 | 第 16.2.2 节的七种批量撤销方法 | valid/revoked/NULL、显式状态筛选、重复调用计数、命中/未命中行标记变化 |
| 持久化可靠性 | CAS、事务、外键、版本 SQL | 冲突、取消、部分失败、并发关联写入和真实 LOB 测试；普通删除矩阵与 A/S/T 完整性、独立 Prune 路径、保留行标记轮换、旧快照冲突与整笔回滚 |

## 20. 设计维护规则

本文保持固定路径 `docs/design.md`。README 链接到本文；不以带日期的过程文档或会话记录替代设计。

影响公共 API、支持矩阵、schema、关键查询/事务语义的变更，必须在同一变更中更新设计与测试。执行任务清单可以另行维护，但它必须引用本文，并且不能悄悄重定义要求。

评审通过后将状态更新为“已确认”，再开始工程与实现；此处的评审状态只表示设计尚待用户审阅，不表示需要额外批准阅读、整理文档等已授权工作。
