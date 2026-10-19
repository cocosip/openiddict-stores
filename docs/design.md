# Sharp.OpenIddict.Stores 项目设计文档

文档版本：1.9。创建日期：2026-10-19。修订日期：2026-10-19。状态：设计评审稿。

本文是项目的正式设计文档和后续开发依据，覆盖产品范围、兼容版本、公共 API、持久化结构、数据库差异、运行语义、测试、示例与交付标准。它不是临时计划或执行记录。评审通过后以本文作为实现基线；实现若需要改变公开合同、数据库结构或支持范围，先修订本文，再修改代码与测试。

## 1. 目标与已确认要求

为 OpenIddict 提供直接使用 SQL 的关系数据库存储扩展，沿用 OpenIddict 原生 Manager、Store、Descriptor 和依赖注入扩展点；通过原生选项禁用实体缓存，保证批量变更后的重新查询读取数据库。

- 所有发布包以 `Sharp` 开头，统一采用 `Sharp.OpenIddict.Stores.*`。
- 首版数据库覆盖 SQL Server、PostgreSQL、MySQL/MariaDB、SQLite、Oracle、达梦。
- 提供 .NET Standard 2.0、2.1、.NET 8、9、10 的兼容系列（基于 OpenIddict 7.7.2 稳定版）；OpenIddict 8.x 系列等上游发布稳定版后另行启动。
- 所有测试、示例和可运行辅助程序使用 .NET 10，解决方案使用 `.slnx`。
- 本文明确给出技术选型，不把尚待评审的设计决定表述为用户已经确认的要求。

### 1.1 命名与依赖定位

本项目的交付物是 OpenIddict 四个 Store 契约的实现，底层如何访问数据库属于内部实现细节。命名据此确定，不绑定具体数据访问技术：

- 包名 `Sharp.OpenIddict.Stores.*` 与上游 `OpenIddict.EntityFrameworkCore` 处于同一层级、描述同一件事，消费者从包名即可判断用途。
- 类型前缀为 `OpenIddictStore`（如 `OpenIddictStoreToken`、`OpenIddictStoreBase`），命名空间为 `Sharp.OpenIddict.Stores.*`，与包名三层一致。
- 旧名 `Sharp.OpenIddict.Dapper.*` / `OpenIddictDapper*` 已废弃。早期按 Dapper 命名，但实测 Dapper 的 API 只出现在一个文件、共 8 处调用；实体映射、谓词翻译、方言 SQL 生成与逐行异步读取全部由本项目自行实现。继续用 Dapper 命名会错误暗示依赖边界。
- Dapper 已从依赖中完全移除（DB-02 期间实施，见第 9.3 节）。全部代码只依赖 ADO.NET 抽象 `DbConnection`、`DbCommand`、`DbParameter`、`DbDataReader`，生成的安装包依赖组中不再出现 Dapper。


首版（7.x 兼容系列）负责 Application、Authorization、Scope、Token 四类实体的持久化。OpenIddict 8.x 的 Resource、Session 尚无稳定版依赖，暂不纳入实施范围；用户、密码、角色、用户登录界面和 ASP.NET Core Identity 存储不属于此扩展。

## 2. 源码调研与版本边界

本地参考仓库：`D:\Code\dotnet-source\openiddict-core`。只读取源码和 Git 对象，不改变该仓库的分支或工作区。

| 参考版本 | 本地证据 | 存储接口 | 与本项目的关系 |
| --- | --- | --- | --- |
| 7.7.2 | 标签，提交 `950cb13b` | Application、Authorization、Scope、Token | **唯一实施基线**：已发布稳定版，Core 提供全部五个要求的目标框架 |
| 8.0.0-preview.4 | 标签，提交 `644eab6a` | 上述四类，加 Resource、Session | 仅作演进方向参考，**不作为依赖**：8.x 尚无稳定版，本项目不引用预览包 |
| 当前 HEAD | `ab23a162`，Versions.props 标记 preview.5 | 六类存储，接口也有变化 | 用来了解发展方向，不把尚未核实发布的 HEAD 当成 NuGet 依赖 |

本项目只依赖稳定版包，不引用任何预览版依赖。8.x 当前没有稳定版本发布，因此 8.x 主线暂不实施；7.7.2 是唯一实现基线，其存储合同为 Application、Authorization、Scope、Token 四类。

8.x 当前的 Core 还包含 .NET Framework 4.8 目标，但没有 .NET Standard、.NET 8、.NET 9 目标。7.7.2 的 Core 则同时提供 `netstandard2.0`、`netstandard2.1`、`net8.0`、`net9.0`、`net10.0` 资产，因此五目标只对 7.7.2 成立。

将来升级到 8.x 稳定版时，变化也不止增加两个实体：例如 `CountAsync` 查询回调增加状态参数，Authorization/Token 的 `FindAsync` 改为元组参数，Token 增加 SessionId 访问方法。两个主版本需要分别对接真实接口，不在一个程序集里用条件编译兼容。

主要参考位置：

- `src/OpenIddict.Abstractions/Stores/`：必须实现的合同。
- `src/OpenIddict.Core/Managers/`：调用方式、验证、缓存和并发异常处理。
- `src/OpenIddict.Core/OpenIddictCoreBuilder.cs`：原生实体和 Store 替换入口。
- `src/OpenIddict.EntityFrameworkCore/Stores/`：关联删除、撤销和清理规则。
- `src/OpenIddict.EntityFrameworkCore/Configurations/`：字段长度、唯一性和关系。
- `eng/Versions.props`、`Directory.Build.props`：版本和框架范围。

实施开始时核实所选标签对应 NuGet 包确已发布，再锁定精确依赖。上游版本升级需要重新检查 Store 合同与目标框架，不自动追随最新预览包。

## 3. 分版本发布方案

建议同一仓库采用两个长期分支，每个分支都只有一套同名工程和一个 `Sharp.OpenIddict.Stores.slnx`。

| 项目系列 | 建议分支 | NuGet 版本示意 | OpenIddict 依赖 | 发布类库目标 |
| --- | --- | --- | --- | --- |
| 兼容系列（当前实施） | `release/7.x` | `7.0.0` | 锁定 7.7.2 稳定版；发布依赖范围 `[7.7.2,8.0.0-0)`，排除 8.x 及其预览版 | `netstandard2.0;netstandard2.1;net8.0;net9.0;net10.0` |
| 主线系列（暂不实施） | `main` | 待定 | 需等 8.x 稳定版发布后再定；不锁定预览包 | `net10.0` |

包的主版本表达 OpenIddict 兼容系列，次版本不必与上游保持一致。例如扩展 7.0.0 依赖 OpenIddict 7.7.2。7.x 发布范围采用 `[7.7.2,8.0.0-0)`，不能用会允许 8.0 预览版进入的宽范围。

8.x 主线暂不实施：上游尚无稳定版，本项目不引用预览包，因此不为其创建工程骨架、不锁定预览依赖，也不发布预览包。等 8.x 稳定版发布并核实存储合同后再单独启动。

不建议在一个程序集里同时引用 OpenIddict 7 和 8，也不通过大量 `#if OPENIDDICT8` 兼容两套公共合同。通用 SQL、序列化、方言和测试修复可以在分支间移植；Store、实体和 DI 注册分别编译。实际创建分支留到实施阶段。

与把两套同名包工程塞进同一个解决方案相比，该方案减少重复工程和打包冲突。代价是公共修复要维护两个分支，CI 必须分别执行。

## 4. NuGet 包与工程布局

| 包名 | 职责 |
| --- | --- |
| `Sharp.OpenIddict.Stores.Models` | 默认 POCO 实体，不依赖 EF Core 或数据库驱动 |
| `Sharp.OpenIddict.Stores` | Store 实现、Builder、Options、连接生命周期、查询和序列化 |
| `Sharp.OpenIddict.Stores.SqlServer` | SQL Server 方言、参数差异、建表与升级脚本 |
| `Sharp.OpenIddict.Stores.PostgreSql` | PostgreSQL 对应实现 |
| `Sharp.OpenIddict.Stores.MySql` | MySQL 与 MariaDB 对应实现及分别验收 |
| `Sharp.OpenIddict.Stores.Sqlite` | SQLite 对应实现 |
| `Sharp.OpenIddict.Stores.Oracle` | Oracle 对应实现，包含大文本参数处理 |
| `Sharp.OpenIddict.Stores.Dm` | 达梦独立方言与参数处理 |

provider 引用核心包，核心包只引用 Models 与 OpenIddict.Core。产品代码不依赖 EF、ABP、Dapper（含 Dapper.Contrib）或另一套 ORM，也不额外发布空的 Abstractions 包。

建议目录：

```text
Sharp.OpenIddict.Stores.slnx
Directory.Build.props
Directory.Packages.props
src/
  Sharp.OpenIddict.Stores.Models/
  Sharp.OpenIddict.Stores/
    Stores/
    Queries/
  Sharp.OpenIddict.Stores.SqlServer/
  Sharp.OpenIddict.Stores.PostgreSql/
  Sharp.OpenIddict.Stores.MySql/
  Sharp.OpenIddict.Stores.Sqlite/
  Sharp.OpenIddict.Stores.Oracle/
  Sharp.OpenIddict.Stores.Dm/
tests/
  Sharp.OpenIddict.Stores.Tests/
  Sharp.OpenIddict.Stores.IntegrationTests/
  Sharp.OpenIddict.Stores.Server.Tests/
samples/
  Sharp.OpenIddict.Stores.Sample.Server/
  Sharp.OpenIddict.Stores.Sample.Client/
scripts/
docs/
```

使用 .NET 10 SDK、Nullable、集中包版本管理。7.x 按 TFM 管理 Microsoft.Extensions 等依赖，避免给 net8.0 消费者强行引入另一主版本的依赖体系。

### 4.1 依赖版本与中央包管理

依赖版本必须只有一个权威来源，不散落在各工程文件里：

- 仓库根目录 `Directory.Packages.props` 启用 `ManagePackageVersionsCentrally`，用 `PackageVersion` 集中登记所有直接依赖和需要提升的传递依赖版本。任何 `.csproj` 不得再写带 `Version` 的 `PackageReference`；出现即视为违反本节，须由构建与 CI 检查拒绝。
- 启用 `CentralPackageTransitivePinningEnabled`，把需要覆盖的传递依赖（例如被上游引入的过时版本、存在已知漏洞的版本）提升为中央版本。未登记的传递依赖保留上游解析结果，但仍参与 NuGet 审核。
- 产品自身版本（`VersionPrefix`/`VersionSuffix`）在根 `Directory.Build.props` 统一定义，同系列八个包使用同一版本；不按工程分别设置版本号。
- 精确版本经第 2 节核实后写入中央清单。当前只有 7.x 一个实施分支，`Directory.Packages.props` 就是唯一清单；8.x 稳定版发布后另建分支并维护自己的中央清单，同一分支内不允许工程级版本例外。
- 中央清单只允许稳定版本。任何带 `-` 的预发布版本（如 `8.0.0-preview.4`）都不得出现在 `PackageVersion` 中，由单元测试检查拒绝。
- 数据库驱动只被测试、示例和可运行工具引用，不进入产品包依赖；测试工程使用的驱动版本同样来自中央清单，缺环境时明确跳过，不通过临时改版本或工程级覆盖绕过。
- 版本升级是一次原子变更：只修改中央清单，然后重新还原、构建并运行受影响测试。NuGet 审核发现的已知漏洞包必须在中央清单中提升到修复版本，不能靠关闭审计或 `NoWarn` 掩盖。
- 检查项：工程文件不含 `Version` 属性；包内依赖版本与中央清单一致；产品 nupkg 的依赖组不含 ADO.NET 驱动、EF Core 或其他 ORM。

#### 4.1.1 按 TFM 的依赖版本（7.x 五目标）

同一条依赖不能在所有目标框架上用同一个版本。为 netstandard2.0/2.1、net8.0、net9.0、net10.0 生成的多目标程序集，每个 TFM 的依赖组必须与该 TFM 的宿主代际匹配，不能把 net10 时代的包喂给 netstandard 或 net8 资产，否则消费者会被强制升级 Microsoft.Extensions 依赖体系，产生版本冲突或需要绑定重定向：

| TFM | Microsoft.Extensions.* 版本线 | 与上游的关系 |
| --- | --- | --- |
| netstandard2.0 | 8.0.x | 与 OpenIddict 7.7.2 的 `.NETStandard2.0` 依赖组一致，不得升高到 9.x/10.x |
| netstandard2.1 | 8.0.x | 与 OpenIddict 7.7.2 的 `.NETStandard2.1` 依赖组一致 |
| net8.0 | 8.0.x | 与 OpenIddict 7.7.2 的 `net8.0` 依赖组一致 |
| net9.0 | 9.0.x | 与 OpenIddict 7.7.2 的 `net9.0` 依赖组一致 |
| net10.0 | 10.0.x | 与 OpenIddict 7.7.2 的 `net10.0` 依赖组一致 |

当前实际锁定的精确版本（全部稳定版）：

| TFM | DependencyInjection.Abstractions | Logging.Abstractions | Options |
| --- | --- | --- | --- |
| netstandard2.0 / netstandard2.1 / net8.0 | 8.0.2 | 8.0.2 | 8.0.2 |
| net9.0 | 9.0.21 | 9.0.21 | 9.0.21 |
| net10.0 | 10.0.13 | 10.0.13 | 10.0.13 |

`System.Security.Cryptography.Pkcs` 作为传递依赖提升按同一分线锁定（8.0.1 / 9.0.21 / 10.0.13），用于消除上游传递引入的已知漏洞版本。

规则：

- 每个 TFM 选取该 TFM 上仍受支持的**最低版本线**，且不低于上游同 TFM 依赖组的下限；消费者可以自行解析到更高补丁，本扩展不替消费者锁定高主版本。
- 8.x 主线只有 `net10.0`，其依赖统一使用 10.0.x；该行在 8.x 稳定版发布并实施后生效，当前不适用。- 在中央包管理中按 TFM 条件登记（`Directory.Packages.props` 中用 `Condition="'$(TargetFramework)' == 'net8.0'"` 的 `ItemGroup` 分别声明），不允许改用工程级 `Version`、`VersionOverride` 或运行时程序集绑定绕过。
- 单一版本即可覆盖全部 TFM 的依赖不必拆分；只有存在按代际发布的依赖（Microsoft.Extensions.*、缓存、日志、选项等）才按 TFM 分线。
- 打包后检查 nuspec 依赖组：`netstandard2.0`/`netstandard2.1`/`net8.0` 组不得出现 9.x/10.x 的 Microsoft.Extensions.*，`net9.0` 组不得出现 10.x；主线 `net10.0` 组不得出现低代际依赖。7.7.2 基线已实测通过：`Sharp.OpenIddict.Stores` 7.0.0 的 nuspec 五个依赖组分别为 8.0.2 / 8.0.2 / 8.0.2 / 9.0.21 / 10.0.13，OpenIddict 依赖统一为 7.7.2。
- 只在全局组登记 `Microsoft.Extensions.DependencyInjection` 这类会向下拉取更高代际的包，会把 10.0.x 注入低 TFM 并触发 NU1109 降级错误；因此这类提升必须收进对应 TFM 的条件组，或直接不提升，让上游解析结果保留。
- 数据库驱动只被测试、示例引用时同样受宿主 TFM 限制：驱动缺少某 TFM 资产（例如 Oracle 驱动只有 `net8.0`/`netstandard2.1`）时，在该 TFM 上不可用，必须在支持矩阵和使用说明中标注，不得用版本提升假装支持。
- 测试、示例和可运行工具固定 `net10.0`，统一引用 10.0.x；它们引用多目标类库时由 NuGet 选择 `net10.0` 资产，这条不构成“已在 net8/net9 原生运行”的证据。

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

### 5.1 驱动与 TFM 实测矩阵

下表是本机在锁定版本下**实际检查包资产**得到的结论（`~/.nuget/packages/<id>/<version>/lib`），不是从文档推断的。`lib` 目录存在某 TFM 只代表该驱动在该运行时上“可被还原并加载”，不代表已连接真实数据库；连接验收见第 5 节的其他任务。

| 驱动 | 锁定版本 | 驱动提供的 `lib` TFM | 可用于 ns2.0 | 可用于 ns2.1 | 可用于 net8.0 | net9.0 | net10.0 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Microsoft.Data.SqlClient | 7.1.1 | net462、netstandard2.0、net8.0、net9.0 | 是 | 是（回退 ns2.0） | 是 | 是 | **否**（无 net10 资产） |
| Npgsql | 10.0.3 | net8.0、net9.0、net10.0 | 否 | 否 | 是 | 是 | 是 |
| MySqlConnector | 2.6.2 | net462、net471、net48、net6.0、net8.0、net9.0、net10.0、netstandard2.0、netstandard2.1 | 是 | 是 | 是 | 是 | 是 |
| Microsoft.Data.Sqlite | 10.0.13 | netstandard2.0 | 是 | 是（回退 ns2.0） | 是 | 是 | 是（回退 ns2.0） |
| Oracle.ManagedDataAccess.Core | 23.26.301 | net8.0、netstandard2.1 | **否** | 是 | 是 | 是（回退 ns2.1） | 是（回退 ns2.1） |
| DM.DmProvider | 8.3.1.47463 | net40、net45、net5.0、net6.0、net7.0、net8.0、net9.0、netcoreapp2.1、netcoreapp3.1、netstandard2.0 | 是 | 是（回退 ns2.0） | 是 | 是 | **否**（无 net10 资产） |

由此得到两条必须写进使用说明的约束：

- **netstandard2.0 宿主无法使用 Npgsql 与 Oracle 驱动**：前者不提供该 TFM 资产，后者最低只到 netstandard2.1。扩展本身仍为该 TFM 产出程序集，但 PostgreSQL/Oracle provider 在 ns2.0 宿主上不可用，必须在支持矩阵中显式标注而不是留给消费者自己发现。
- **net10 宿主无法使用 SqlClient 7.1.1 与 DM.DmProvider**：二者都没有 net10 资产。这意味着 SQL Server 与达梦的**真实连接验收无法在 net10 上进行**，只能在 net8.0/net9.0 宿主中验证；本文档不把编译通过当作连接验证。

未实测的组合（包括所有“回退”情形是否在真实服务器上语义正确）一律标记为未验证，不列入正式支持范围。这张表由 `PackageAssetTests` 中的资产/依赖检查与实际包内容交叉验证，驱动版本变化时必须重新检查。


7.x 的存储与方言包保持五个 TFM；宿主选择适配自身运行时的驱动。README 按“OpenIddict 系列 × 扩展 TFM × 宿主 × 驱动版本 × 服务器版本”列出实际验证组合。未验证的旧驱动组合明确标记，不把早期有 netstandard2.0 资产的驱动自动列入正式支持范围。

方言负责引号、参数名、分页、批量删除和参数类型。对 Oracle/达梦大文本、按名称绑定等标准 ADO.NET 不能完整表达的行为，使用 provider 内部的小型适配器，按实际 command/parameter 类型设置公开驱动属性并缓存必要元数据；不引入全局驱动状态。正式支持只覆盖通过真实驱动验证的实现。

## 6. 运行架构与接入 API

调用路径为：OpenIddict Server → 原生 Manager → 本扩展 Store → 数据库方言 → ADO.NET → 数据库。

`UseDapper()` 注册默认实体和相应的 Store，沿用 `SetDefault*Entity`、`Replace*Store` 等原生扩展点，保留 `DisableAdditionalFiltering = false`，并调用原生 `DisableEntityCaching()` 设置 `OpenIddictCoreOptions.DisableEntityCaching = true`。不替换协议处理器、Manager 或缓存算法。

原生实体缓存位于 DI 作用域内；批量 Revoke 直接更新 Store，不会逐项移除缓存中的实体。只轮换数据库并发标记无法阻止同一作用域再次返回旧状态。因此首版要求实体缓存保持禁用，对最终生效选项进行校验，重新启用时在首次使用 Manager/Store 前报配置错误，不依赖配置调用顺序。禁用缓存不使已返回的 POCO 自动刷新，也不使撤销成为所有在途请求的同步屏障。

建议提供 `OpenIddictStoreBuilder`、`OpenIddictStoreOptions`、`IOpenIddictStoreConnectionFactory` 和一个窄职责的数据库方言接口。连接工厂接受取消信号并创建可由本扩展释放的新 `DbConnection`。方言实例是无状态的，每个已配置的存储实例绑定自己的方言，进程里不存在“当前数据库”静态变量。

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

首版（7.x）包含四张主表：Applications、Authorizations、Scopes、Tokens。OpenIddict 8.x 的 `OpenIddictResources`、`OpenIddictSessions` 及 Token.SessionId 待其稳定版发布后再纳入。默认主表名称沿用 OpenIddict 常用名称，但这不表示可以直接操作既有 EF 表。

字段按 7.7.2 的 Store/Descriptor 逐项覆盖，包括 ClientSecret、JsonWebKeySet、Permissions、Requirements、Settings、本地化名称、Properties。主键建议保持上游默认最大长度 100，并发标记 50，ClientId 100、Scope Name 200、Subject 400；其余长度以锁定版本配置为准。

集合和字典保存为 JSON 文本，不依赖数据库原生 JSON 类型。对必须参与查找的集合，增加四张事务维护的辅助索引表：

| 辅助表 | 用途 |
| --- | --- |
| ApplicationRedirectUris | 精确查找包含给定 RedirectUri 的应用 |
| ApplicationPostLogoutRedirectUris | 精确查找包含给定退出回调地址的应用 |
| AuthorizationScopes | 查询包含全部 RequiredScopes 的授权 |
| ScopeResources | 查询包含给定 Resource 名称的 Scope |

JSON 是集合值的权威来源；辅助表只保存去重后的查询索引，主表和辅助表必须在同一事务更新。这样六类数据库都可以使用索引查询，不依赖 `LIKE '%uri%'`、整表 JSON 扫描或各家不同的 JSON 运算符。
四张辅助表统一以所属实体 ID 和 Position 为主键，完整集合值保存为 LargeText，另存 SHA-256 二进制摘要并建立非唯一查找索引。集合值按 Ordinal 去重，不添加长度 200 等原生合同没有的限制，也不以数据库字符串比较定义集合元素身份。摘要只用于缩小候选集，返回前必须与权威 JSON 中的完整值做 Ordinal 比较；摘要碰撞不能造成插入冲突、漏项或误匹配。不对 URI、Scope 或 Resource 值做大小写、尾随空格或其他标准化。

关键约束：ClientId、Scope.Name、非空 Token.ReferenceId 唯一。SQL Server 等需要为可空唯一键使用合适的过滤索引，允许多个 NULL ReferenceId。标识符列选用区分大小写、重音的物理比较规则；普通字符串的查询和条件写入还必须遵守第 16.1.1 节，不能只靠排序规则或原生 Manager 的附加过滤保证精确匹配。

时间统一为 UTC；使用各方言对应的原生时间类型，读出后显式恢复 UTC 语义。SQLite 使用固定格式 UTC 文本。不同数据库的时间精度在合同测试中明确，不能让驱动把 UTC 当成本地时间。Oracle 的空字符串/NULL、大文本 CLOB、Unicode、LOB 读取必须独立测试，不隐式承诺所有可选空串在 Oracle 上无损。

仅提供必要的 schema/table-prefix 配置；标识符先校验再由方言引用，SQL 值全部参数化。不提供任意列重命名、动态实体映射或通用仓储配置系统。

## 8. Store 行为、事务与并发

实现每个目标版本所有 Store 成员，固定查询通过参数化 SQL 在服务器过滤。Get/Set 类成员遵循 OpenIddict 的空值、不可变集合和 CultureInfo 约定，敏感数据按原生 Manager 给出的形式存储，不在 Store 重复散列客户端密钥或引用令牌标识。

Update/Delete 使用 Id 与旧 ConcurrencyToken 进行条件写入，更新成功生成新标记。条件未命中时抛出 OpenIddict 原生 ConcurrencyException。成功提交后才更新内存实体标记；失败回滚不能留下看似已成功保存的实体状态。

包含关联修改时，先在同一事务获得/确认父记录写入权，再修改子表。并发冲突必须回滚全部子表变更，不能先删子表再发现父记录版本不匹配。外键和方言特定锁策略负责阻止关联写入穿过删除窗口。

普通 DeleteAsync 按第 16.2 节的删除矩阵执行。删除 Application 处理直接及间接关联，不能仅按各子表 ApplicationId 删除。删除 Authorization 时，删除所有 AuthorizationId 指向它的 Token。Scope 删除同时删除其 ScopeResources 辅助行，但不影响任何独立实体。

凡清空外键但保留的主实体行，必须在同一事务内同时轮换其 ConcurrencyToken，包括第 16.3 节清理路径保留的行；旧快照后续 Update 应命中 0 行并抛出原生 ConcurrencyException，而不是重写已删除的外键后触发约束错误。可以批量更新，不要求先加载全部实体；任何失败必须同时回滚外键和并发标记。

Revoke 使用数据库批量条件更新，不先加载全部实体。筛选和计数严格按第 16.2.2 节逐方法定义：Token 的 RevokeBy* 排除已撤销行，通用 RevokeAsync 和 Authorization 的 RevokeBy* 不增加这个隐含条件。仅对实际命中行设置 revoked 并轮换并发标记，防止旧实体随后覆盖撤销结果。实体缓存按第 6 节禁用，提交后通过同一作用域或新作用域的原生 Manager 重新查询均读取数据库中的已提交状态；调用前持有的实体仍是旧快照。通过 Manager/Server 路径验证，不只测试裸 Store SQL；不承诺已开始的验证或请求会被追溯中止。

Prune 完整遵循上游条件，而不是简单删除过期行。例如 Token 还要考虑 CreationDate 阈值、inactive/valid 状态和所属 Authorization 状态；Authorization 清理考虑状态、ad-hoc 类型和是否仍有 Token。采用有上限的小批次，建议每批不超过 500 个 ID，避开 Oracle IN 参数限制及其他数据库参数上限。删除时重检清理条件、使用事务，按实际影响行数返回；数据库错误和取消向上传播。

默认列表稳定排序并在数据库分页。返回 IAsyncEnumerable 的实现保持真正逐行读取：直接使用 `DbCommand.ExecuteReaderAsync` 与 `DbDataReader.ReadAsync`；连接/reader 存活到枚举结束，取消和提前退出都必须释放。不能把缓冲查询的完整结果包装成“流式”。驱动内部是否真正异步由真实驱动验证，不用 Task.Run 掩盖同步实现。

## 9. IQueryable 回调：明确而有限的翻译能力

ADO.NET 不提供 LINQ provider；OpenIddict 的 CountAsync/GetAsync/ListAsync 却接受 IQueryable 回调。设计必须承认这个差异。

比较过三种方式：整表加载后 AsQueryable 会导致内存和延迟失控；全部抛 NotSupportedException 会让常用原生管理接口不可用；引入另一套 ORM 会增加依赖和双重映射。建议实现仅覆盖本存储单表查询的小型翻译器。

首版支持：

- Where：有界字符串、标识符和日期等可比较映射列的等值、不等值、范围、空值判断、AND/OR/NOT；LargeText/Json 仅允许空值判断，不支持内容比较。
- 参数和闭包常量；标量列表 Contains 转为有参数上限保护的 IN 或等价精确成员谓词，并按第 9.1 节处理空列表及 NULL。
- OrderBy/ThenBy 仅接受支持排序的有界列和日期列；Skip/Take 无排序时补稳定主键排序。
- Select：实体、单个映射标量、映射标量组成的简单 DTO/匿名对象。
- Count/Get/List 在上述查询结果上执行，保持 Skip/Take/Select 对结果的语义；Get 取首项。
- 原生非泛型 Manager 所需的 object 转换/实体类型转换，不能只测试直接调用泛型 Store。

### 9.1 NULL 与集合参数的确定语义

受支持表达式采用与 EF Core 默认 NULL 补偿方向一致的 C# 比较语义，不采用 SQL unknown 直接决定结果的规则，也不提供切换为关系数据库 NULL 语义的选项。只覆盖本节白名单，不因此扩展为任意 C# 表达式执行器。

等值、不等值适用于受支持的字符串、日期等可比较标量。下表中的“相等”对非 NULL 字符串（包括空串）按第 16.1.1 节作精确等值判断，对其他类型按其值判断：

| 左值 | 右值 | `a == b` | `a != b` |
| --- | --- | --- | --- |
| NULL | NULL | true | false |
| NULL | 非 NULL | false | true |
| 非 NULL | NULL | false | true |
| 非 NULL | 非 NULL | 按值比较 | 等值结果取反 |

设 `EqualNonNull(a,b)` 表示两个非 NULL 值的等值谓词，这是文档中的逻辑记号，不要求创建数据库函数。通用等值翻译为 `(a IS NULL AND b IS NULL) OR (a IS NOT NULL AND b IS NOT NULL AND EqualNonNull(a,b))`；不等值是该完整二值谓词的取反，而不是直接使用 `<>`。这同时覆盖列与常量、列与闭包参数以及同表两列比较。可空日期的 `<`、`<=`、`>`、`>=` 在任一操作数为 NULL 时为 false；NOT、AND、OR 在补偿后的布尔结果上组合，例如 `!(CreationDate < cutoff)` 必须包含 CreationDate 为 NULL 的行，不能机械翻成 `CreationDate >= cutoff`。

标量集合 Contains 的 NULL 规则固定如下；其取反对每一行返回相反的布尔结果：

| 参数集合 L | 被比较值 x | `L.Contains(x)` | `!L.Contains(x)` |
| --- | --- | --- | --- |
| 空集合 | 任意值，包括 NULL | false | true |
| 只含 NULL | NULL | true | false |
| 只含 NULL | 非 NULL | false | true |
| 含非 NULL 值，不含 NULL | NULL | false | true |
| 含非 NULL 值和 NULL | NULL | true | false |
| 含非 NULL 值，可含 NULL | 非 NULL | 与非 NULL 元素逐项等值，任一匹配即 true | Contains 结果取反 |

翻译时先分离集合中的 NULL 和非 NULL 值，再组合 `IS NULL`/`IS NOT NULL` 与参数化的成员等值判断；只有数据库原生 IN 满足所需等值语义时才使用 IN。不能生成依赖 `NOT IN (..., NULL)` 的谓词。NULL 集合本身按参数/回调求值错误处理，不当作空集合；空集合或仅含 NULL 的集合不生成非法 `IN ()`。分块与参数上限处理必须保持上述语义。

查询回调中的 `x == ""` 是显式比较，与固定 FindAsync/RevokeAsync 的空串可选条件“不筛选”不同。Oracle 对有界文本把空串保存为 NULL；查询以数据库读取后的列值为准，但表达式常量/参数仍区分 C# 的 null 与空串。对这种列，`x == ""` 恒为 false、`x != ""` 恒为 true；Contains 中的空串也不能被错误当成 NULL 元素。翻译应在绑定前识别空串并化简相应谓词，不能因驱动将空串参数绑定为 NULL 就把 `x == ""` 改成 `x IS NULL`。DM 的实际声明模式若具有相同空串行为，采用同样处理。此规则不改变 JSON 内部的空字符串值。

共享合同测试必须逐项覆盖两张真值表、含 NULL 的 IN/取反、同表可空列比较、NOT 与可空日期、AND/OR 组合及 Oracle/DM 的空串边界；泛型 Store、非泛型 Manager、Count/Get/List 走相同翻译规则。

### 9.2 支持边界

首版不支持 Join、GroupBy、导航展开、任意方法执行、JSON 内部属性和集合表达式，也不支持 LargeText/Json 的内容比较、排序和 IN。它们仍可直接投影返回。此限制对六类数据库一致，避免 Oracle NCLOB 不能直接使用普通等值/ORDER BY 的差异；不以截断 LOB 后比较代替完整值语义。无法翻译时在执行 SQL 前抛出明确的 NotSupportedException，并列明不支持的表达式与列。绝不静默退化为内存执行。

该组件不是通用 LINQ ORM。采用白名单表达式、列名来自固定映射、值全部参数化；null、排序、分页和投影都有合同测试。额外复杂的报表由应用自己的查询承担。README 必须给出支持与拒绝示例，不能宣称兼容任意 IQueryable。

### 9.3 执行层：直接基于 ADO.NET（已移除 Dapper）

**现状（实测）**：Dapper 已作为包依赖完全删除。核心包 nuspec 的五个 TFM 依赖组中都不再出现 Dapper，源码中也没有任何 `using Dapper`。执行层改写为本项目自有的 `OpenIddictStoreCommands` 与 `OpenIddictStoreSession`，直接使用 `DbCommand`、`DbParameter`、`DbDataReader` 与 `ExecuteReaderAsync`。实体映射由 `OpenIddictStoreEntityMapper` 手写完成，谓词翻译由 `OpenIddictStorePredicateTranslator` 完成，SQL 由 `OpenIddictStoreQueryBuilder` 生成。

改写前的实测依据：Dapper 的公开 API 只出现 8 处，全部集中在 `OpenIddictStoreSession` 一个类型内部，承担的只是“执行命令并返回标量/行”这一薄层；其约定式映射能力恰好是本项目刻意绕开的部分。因此替换面收敛在一个文件，属于机械替换而非架构调整。

**边界约束**：以下四条是硬约束，由测试固化，违反即视为走查不通过。

1. 任何项目不得引用 Dapper 包，任何源文件不得出现 `using Dapper`、`Dapper.SqlMapper`、`global::Dapper` 或 `new CommandDefinition(`；`Directory.Packages.props` 也不得保留其版本项。
2. 业务代码只依赖 ADO.NET 抽象（`DbConnection`、`DbCommand`、`DbDataReader`、`DbParameter`），不依赖具体驱动类型。六个受支持驱动的实测结论一致：它们都同时实现 `IDbConnection` 与 `DbConnection`，也都有 `OpenAsync`、`ExecuteReaderAsync`、`ExecuteNonQueryAsync`、`ExecuteScalarAsync`。
3. 参数类型适配由方言负责。执行层只按值的运行时类型赋值，不依赖驱动的隐式类型推断作为唯一依据。
4. `netstandard2.0` 下 `DbConnection`、`DbCommand`、`DbDataReader` 不实现 `IAsyncDisposable`，释放一律走 `OpenIddictStoreConnections.DisposeAsync` 的 `#if` 分派，不写 `await using`。

**为何选择 `DbConnection` 而不是 `IDbConnection`**：`IDbConnection`、`IDbCommand`、`IDataReader` 只有同步 API，`DbConnection` 本身已实现 `IDbConnection`。用 `DbConnection` 既覆盖全部六个驱动，又保留真正的异步执行；而 `IDbConnection` 会强制退回同步调用，与第 9.1 节“不得用 `Task.Run` 掩盖同步实现”的要求冲突。需要按接口访问的调用方仍可隐式转换到 `IDbConnection`，反向则做不到。

**驱动的异步陷阱**：`DbConnection.OpenAsync` 的基类默认实现是同步调用 `Open()`。接口存在不等于真异步，因此“驱动是否真正异步”必须由真库测试确认，不能只看类型。SQL Server 方言的验收测试即为此类证据。

**日期绑定的实测结论（SQL Server）**：同一微秒值以不同 CLR 类型绑定到 `datetime2(6)` 列，回读的刻度不同。`DateTime`（无论 `Kind` 为 Utc 还是 Unspecified）被推断为 `datetime`，截断到 3.33 ms（回读 `…1233330` 刻度）；`DateTimeOffset`（UTC）与 ISO 字符串都精确保留（回读 `…1234560` 刻度），且以 `DateTimeOffset` 参数做等值比较可正确命中。方言因此固定以 UTC `DateTimeOffset` 绑定日期。


## 10. 数据库 SQL 脚本、建库与版本升级

每个 provider 提供可审阅的版本化建表、索引和升级 SQL，并随包交付；脚本有独立的存储架构版本记录。默认运行服务不自动建表、不自动修改生产 schema，不额外实现迁移引擎。应用可以用现有部署工具执行脚本。

各数据库 SQL 脚本是本项目持续维护的正式交付物。模型、索引或方言变化时，必须同时评估并维护受影响的脚本、升级路径、操作说明和测试，不能把首次建库完成视为脚本维护结束。

首版安装脚本建立四张主表和四张辅助表。当前不提供 7→8 升级脚本：OpenIddict 8.x 尚无稳定版，本项目不实施 8.x。等 8.x 稳定版发布并实施后，再补 Resource/Session 结构与对应升级脚本；届时 Scope.Resources 的已有完整值必须保持不变，不从这些值自动创建 Resource 记录。

升级前备份并停止并发旧版本写入；不承诺混合滚动写入或自动降级。已有 EF/MongoDB 数据转换另行评估，不把本扩展的创建脚本描述成官方 EF 数据库的无缝替代。

### 10.1 唯一源码与随包交付

每个 provider 的 `src/Sharp.OpenIddict.Stores.<Provider>/Sql/` 是该数据库部署脚本的唯一源码位置，包含安装、同系列增量升级和适用的跨系列升级。运行时 CRUD、查询、锁及清理 SQL 仍由 Store/方言维护，不另复制成一套部署脚本；它们必须与所要求的架构版本一起验证。根目录 `scripts/`、测试、示例和部署文档引用这份源码，不维护第二套建表 SQL，也不使用 EF EnsureCreated 代替脚本。

目录约定如下，`8/` 在兼容分支替换为 `7/`。示例中的 v0002 仅展示命名规则，不表示首版需要无实际变更的升级脚本。

```text
src/Sharp.OpenIddict.Stores.<Provider>/Sql/
  7/
    manifest.json
    README.md
    install/v0001.sql.template
    install/v0002.sql.template
    upgrade/v0001-to-v0002.sql.template
    variants/                         # 仅放确有差异的服务器/模式脚本
  cross-series/
    7-v0001-to-8-v0001.sql.template    # 8.x 稳定版实施后再补
```

所有脚本文件都以 `.sql.template` 结尾：表前缀与 schema 是运行时配置，写死表名的文件对任何定制部署都是错的，而扩展名必须让“这是模板、不是可直接执行的脚本”一眼可见。

`manifest.json` 逐条记录 provider、适用服务器/模式与最低版本、脚本类型、可用/仅历史保留状态、`rendersTokens`（该文件包含哪些占位符）、源/目标 Series 与 Version、相对 Sql 根目录的路径和 SHA-256。安装脚本的源版本为空；升级脚本必须有确定的源/目标版本。每个模板的头部注释说明用途、适用目标、源/目标版本及事务/失败边界，与清单对应。MySql 清单分别列出 MySQL 和 MariaDB 的适用项；可引用同一物理文件，但必须分别执行验收。有差异时在 variants 中增加明确的专用文件；Dm 原生与声明支持的 Oracle 兼容模式遵循相同规则，不通过人工临时修改脚本适配。

SQL 源文件统一为 UTF-8 无 BOM、LF，由 `.gitattributes` 固定行尾；摘要针对待交付文件的原始字节计算。对应 provider 的 nupkg 在 `sql/` 下保留 Sql 目录的相对结构，交付适用的模板、清单和 README；不按 TFM 复制多套脚本，不依靠安装 NuGet 自动执行或自动复制到应用目录。用户可从包中提取，包检查验证提取文件与仓库源码逐字节一致。

### 10.2 版本与历史维护

NuGet 包版本、OpenIddict 版本和数据库架构版本各自管理。架构 Version 按 Series/provider 递增，同一 provider 的已声明服务器/模式保持同一逻辑架构版本；不同 provider 的 Version 不要求同步。每个发布包明确其要求的架构版本以及支持的升级起点，不能仅凭包主版本选择任意 SQL。

- 已发布的安装和升级 SQL 保持不可变，不覆盖、改名、删除旧文件或通过重算摘要掩盖修改；清单中既有脚本的路径、摘要和版本边界随历史保留。预览包发布的脚本同样适用。未发布的草稿允许修改。
- 表、列、约束、索引、排序规则或需要持久化数据转换的变更，必须提升受影响 provider 的架构 Version，同时新增目标版本的完整安装脚本、明确起点的增量升级脚本与清单条目。仅修改运行时查询 SQL 且不改变存储结构/数据合同时，可保持架构 Version，但仍要重跑相关数据库合同。
- 修复已发布脚本的问题也通过新版本交付。已知有缺陷的旧安装脚本在当前清单/说明中标记为“仅历史保留，不再用于新装”，仍为真实已部署的受支持旧架构提供升级/恢复路径。需要恢复部分执行状态时提供针对具体状态的操作说明，不能修改旧文件后让部署方把它当成原脚本重跑。
- 同系列保留仍声明支持的历史安装与升级链；不支持跨过中间版本时写明执行顺序，不凭文件名自动猜测路线。跨系列脚本由目标系列维护，7.x 包不携带未来 8.x 的安装脚本。

每次架构变更同时更新第 15 节数据字典、Store/方言所需版本、相关脚本及清单、升级说明、开发进度矩阵和测试。跨分支移植按目标系列重新编写/核对 SQL，不直接复制另一系列的版本记录。

### 10.3 执行约定与数据库差异

安装脚本要求目标前缀下尚未安装；升级脚本先检查精确的源 Series/Version，再改变对象和数据，全部成功后才更新目标版本。错系列、错版本、重复执行或已有部分对象时必须停止并报告，不通过 IF EXISTS 跳过错误结构，也不通过 DROP 后重建假装幂等。架构版本相同不代表任意手改数据库都兼容。

各 provider 的 README 维护执行工具/版本、脚本顺序、权限、批次分隔符、遇错停止方式、schema/prefix 设置、停写要求、备份与失败恢复步骤。SQL Server 的 GO、Oracle 客户端的 `/` 等客户端分隔符不能直接当作 ADO.NET 命令发给数据库；真实测试和部署必须遵循相同执行约定。恢复按已有对象、数据与版本记录诊断，Oracle/DM 的 DDL 部分提交如实记录；失败后不恢复应用写入，直到人工恢复并重新验证。

schema/table-prefix 定制只作用于模板中的两个占位符，按 provider 与运行时相同的校验、引用和约束命名规则生成部署副本。渲染入口是 `OpenIddictStoreIdentifiers.RenderDeploymentScript(template, dialect, prefix, schema)`：非法前缀或 schema 在生成副本前抛出；`__SCHEMA__` 由方言引用后展开为带尾点的限定前缀，未配置 schema（或该 provider 无 schema 概念）时展开为空。使用前必须渲染，因此模板不提供“默认前缀可直接执行”的版本。标识符不能用普通值参数代替，也不能直接拼接未经校验的输入。保留原始模板摘要，并记录定制标识符与部署副本摘要；定制不修改仓库中的历史文件。首版沿用已有部署工具或明确的人工步骤，不为此新增通用模板/迁移框架。

MySQL/MariaDB 的字符集、索引与 DDL 差异，以及 Oracle/DM 的模式、LOB、名称长度和事务差异，均在各自脚本和说明中处理。共用文件不等于共用验收结论；不支持的服务器/模式在清单和使用说明中明确排除。

### 10.4 维护验收与变更门禁

每次 SQL 变更都提供可追溯的验证记录；仅改注释也不能直接覆盖已发布文件。必需检查包括：

- 清单引用完整、文件存在、摘要匹配、源/目标版本与脚本头部说明一致，已发布 SQL 与对应发布标签/包中的字节相同；NuGet 提取结果没有漏项、改写或混入其他 provider/不适用系列。
- 每个当前声明支持新装的目标版本的空库安装，以及从每个仍支持起点执行完整升级链；在相同服务器/模式下，升级到目标版本的结构与该版本全新安装一致，覆盖列类型/长度/可空性、排序规则、主外键、索引、辅助表和架构记录，而非只比较表数量。
- 升级保留应保留的 ID、令牌、JSON 和集合索引数据，升级前由源系列/源架构对应版本验证，升级后由目标系列/目标架构对应版本验证 Store/Manager 合同；测试直接使用将随包交付的脚本，不能临时补 DDL 才使测试通过。
- 默认及自定义 schema/prefix、错误起点/重复执行、DDL 或数据步骤中途失败和可诊断恢复；MySQL、MariaDB 分开执行，DM 按声明模式执行，并保留最低服务器版本的验证证据。

首版尚无同系列旧架构时，增量升级项可记录为“不适用：首版无前序架构”，不能编造升级通过记录；首次架构变更时必须补齐并重新验收。`docs/development.md` 独立记录脚本维护任务及逐数据库状态。缺少所声明数据库的安装/升级证据，或只有运行时 Store 测试而无脚本证据，均不能通过发布门禁。

## 11. 测试与验收

全部测试项目目标为 net10.0，建议 xUnit。共享同一组 Store 合同测试数据和断言，通过 provider fixture 分别执行，而不是复制六份断言。四个 Store 接口的逐成员基线固定在 `docs/contracts/7x-store-contracts.md`，并由单元测试对 7.7.2 反射结果做冻结比对；上游升级先让该用例失败，而不是让实现悄悄漂移。

| 测试层 | 重点 |
| --- | --- |
| 单元测试 | DI 注册、最终选项校验及重新启用实体缓存的拒绝、JSON/本地化转换、参数化、LINQ 翻译与拒绝、第 9.1 节 NULL 真值表、取消和资源释放 |
| Store 合同集成测试 | 每个成员、唯一性、空值、Unicode、长文本、集合精确匹配、UTC、更新/删除并发、事务回滚；集合包含超过 200 字符、大小写及尾随空格不同的值；普通字符串精确比较、同名不同大小写/尾空格主体的批量变更隔离、Oracle 空串谓词 |
| 生命周期专项 | 不同状态与阈值的 Prune、逐方法 Revoke 筛选/计数/标记轮换、删除竞态；强制摘要碰撞时仍可同时保存并精确查询；全部 RequiredScopes 的 Ordinal 匹配 |
| Manager/Server 测试 | 原生 Manager 接入、非泛型查询回调、Client Credentials、Code + PKCE、刷新、撤销和失效验证；同一作用域先读再批量撤销后重读、新作用域重读及旧快照 CAS 冲突；删除 Authorization 后其令牌失效 |
| 包与框架检查 | 每个类库 TFM 独立构建，nupkg 包含正确资产和依赖，最小消费者引用验证 |
| 迁移与脚本维护测试 | 第 10.4 节清单/历史摘要/包文件检查、空库安装与完整升级链的结构一致性、数据保留、版本检查、7→8 升级和部分失败诊断 |

数据库集成测试采用 SQLite 临时文件，以及 SQL Server、PostgreSQL、MySQL、MariaDB、Oracle、达梦的独立测试数据库。可以用现有容器或外部连接串，Oracle/达梦可采用自托管 CI。测试只能修改专用测试 schema，不能对用户现有数据库执行清理。

**本仓库的开发过程中，凡涉及数据库服务端一律只做走查**（用户明确要求）：不下载、不安装、不启动、不连接任何数据库服务器；方言、SQL 脚本、分页与类型映射通过静态走查核对，而不是靠临时搭服务器验证。本地执行证据仅限单元测试、脚本资产与占位符检查，以及 SQLite 临时文件库（NuGet 驱动，无需安装服务端）。需要外部服务器的套件在缺环境时如实跳过并记录缺口，由用户环境执行后再补真库证据；不得为了得到通过结论而临时搭建服务器，也不得用编译通过或静态走查替代真库记录。

缺少数据库环境时，本地明确显示跳过；正式支持某 provider 的发布门禁则要求它的真实数据库测试成功，不能以跳过充当通过。MySQL 成功不替代 MariaDB 验证，DM 原生模式成功不替代 Oracle 兼容模式验证。

netstandard 不是可执行运行时；net10 测试成功也不等于已经在 net8/net9 运行。按用户要求，常规测试宿主保持 net10；7.x 的五目标构建和消费者检查作为编译兼容性证据，运行时支持声明另标注实际验证宿主。若以后要求 net8/net9 原生运行证明，再增加专用宿主验证，不能用包可还原代替。

删除专项必须覆盖第 16.2.1 节的全部矩阵，特别是：Token.ApplicationId 为空，或与所属 Authorization 的应用不同；Token 同时绑定另一 Authorization。验证选定集合完整删除，集合外的父实体保留；根记录 CAS 冲突、关联写入竞争和中途失败时，删除、外键、辅助表及并发标记一起回滚。Prune 使用独立 fixture 验证不会复用普通删除的关联语义。

撤销专项必须逐方法覆盖无匹配行、valid/revoked/NULL 混合状态、可选状态筛选、重复调用、实际影响行数、命中行标记轮换和未命中行标记不变；第 16.2.2 节的计数示例均为必需断言。不得将通用 RevokeAsync、Token RevokeBy* 和 Authorization RevokeBy* 绑定到相同的隐含状态过滤器。

最低发布验收包括：目标版本全部 Store 成员可调用；声明支持的查询子集通过；真实数据库完成长 Token.Payload、LOB、唯一性、集合边界、事务并发和清理测试；通过原生 Manager 完成同一及跨作用域的撤销后重新查询、逐方法撤销计数与旧快照冲突验证；上述删除专项全部通过；无全表内存 LINQ 回退；包不带 EF/ABP 依赖；SQL 和异常不泄露客户端密钥、令牌正文或连接凭据。

## 12. 示例与文档

提供两个 net10 示例，避免为六种数据库复制六套服务器：

- Sample.Server：ASP.NET Core + OpenIddict + 本扩展，默认 SQLite，可通过配置切换其他 provider；通过原生 Manager 初始化应用、Scope。
- Sample.Client：演示 Client Credentials，以及 Authorization Code + PKCE、刷新和撤销；交互登录使用明确标注的开发演示身份，不把此扩展包装成用户管理系统。

示例的服务器证书/演示凭据仅用于开发，连接串通过环境变量或 user-secrets 提供。文档给出各数据库所需驱动、建库脚本、运行步骤、查询限制、实体缓存禁用要求和生产部署所需配置，说明撤销后须重新查询而非检查旧 POCO。提供 .http 请求便于快速验证；无需增加管理后台。

## 13. 实施顺序与交付

1. 锁定 7.7.2 的实际发布依赖与完整接口清单。先对 Oracle/达梦做小范围真实驱动验证，确认绑定、大文本、UTC、分页和事务，尽早暴露六数据库共同设计的障碍。
2. 完成 7.x 系列的工程布局、Models、DI、连接工厂及 SQLite 四类 Store，用合同测试建立基线。
3. 实现有限 LINQ 翻译和四张集合索引表，验证 Manager/Server 接入及事务并发。
4. 接入其余五类 provider，MySQL/MariaDB 分别验收，完成各自 schema 脚本和驱动适配。
5. 完成五目标构建、包资产与低 TFM 资产加载验证。
6. 完成两个 net10 示例、README、兼容矩阵和 CI 打包验证，形成正式候选。

OpenIddict 8.x 主线等上游稳定版发布后再单独立项，本节不为其预留骨架。

每一步都以具体行为与测试作为完成标准；不以工程数量、模拟 SQL 测试成功或 SQLite 成功代替六类数据库支持。发布 NuGet 属于后续单独操作，本轮只交付设计。

首版不增加通用仓储、可插拔 ORM、多租户路由、任意主键映射、管理后台、分布式缓存或通用 LINQ/迁移引擎。最主要的风险是旧宿主的驱动兼容性，以及 Oracle/达梦的实际参数和事务行为。

## 14. 公共 API 与组件合同

### 14.1 命名空间与实体

产品命名空间使用 `Sharp.OpenIddict.Stores`、`Sharp.OpenIddict.Stores.Models` 和对应 provider 子命名空间。DI 扩展方法放在 `Microsoft.Extensions.DependencyInjection`，遵循 OpenIddict 现有接入方式。

默认实体命名为 `OpenIddictStoreApplication`、`OpenIddictStoreAuthorization`、`OpenIddictStoreScope`、`OpenIddictStoreToken`。Store 相应命名为 `OpenIddictStoreApplicationStore<TApplication>` 等。OpenIddict 8.x 的 Resource、Session 实体待其稳定版发布后再补充。

实体是存储 POCO：日期属性为 `DateTime?`，只接受/返回 UTC 语义；外键为 `string?`；集合、字典与 JWK 集合的底层属性为 JSON `string?`。Store 负责在 JSON 与原生接口要求的 ImmutableArray、ImmutableDictionary、JsonWebKeySet 之间转换。不要照搬 EF 导航属性，也不要直接依赖其 Models 程序集。

每类实体均有 string Id 与 ConcurrencyToken，默认构造时分别生成新的 GUID 字符串。公共可继承实体和虚方法保留类型扩展点；调用者扩展实际数据库列时，需要替换对应 Store 并提供 DDL，不能认为继承一个属性就会自动保存它。

### 14.2 Builder

首版公共配置面限定如下，正式签名在对应版本编译时落实：

| API | 合同 |
| --- | --- |
| `UseDapper()` / `UseDapper(Action<OpenIddictStoreBuilder>)` | 在 OpenIddictCoreBuilder 上注册本扩展，调用原生 DisableEntityCaching；幂等，不重复注册同一服务 |
| `UseSqlServer()` 等六个 provider 方法 | 安装对应方言；同一注册选择多个不同 provider 时启动失败，不以最后一次调用静默覆盖 |
| `UseConnectionFactory(Func<IServiceProvider, DbConnection>)` | 每次操作获取新连接，默认实现负责调用 OpenAsync 与释放 |
| `UseConnectionFactory<TFactory>()` | 注册实现 IOpenIddictStoreConnectionFactory 的连接工厂 |
| `SetSchema(string?)` | 设置独立 schema；SQLite/MySQL 不支持的 schema 语义须拒绝，不能静默忽略 |
| `SetTablePrefix(string)` | 默认 OpenIddict；应用于主表、辅助表及架构版本表，长度和合法性由 provider 校验 |
| `SetCommandTimeout(int?)` | 默认 null，遵循驱动的默认值；显式值必须为正秒数 |

连接工厂的最小合同为 `ValueTask<DbConnection> CreateConnectionAsync(CancellationToken cancellationToken)`。返回的连接尚未打开且由调用方拥有，不能返回应用其他代码正在使用的共享连接。

Options 只包含 provider、连接工厂、schema、table prefix、command timeout。JSON 格式、查询语义、清理合法性和并发控制不是可随意关闭的选项。缺少 provider/工厂、非法超时或非法表名在首次使用前给出明确配置错误。另校验最终生效的原生 OpenIddictCoreOptions，要求 DisableEntityCaching 为 true；无论调用者在 UseDapper 前后配置，只要最终重新启用实体缓存就必须拒绝，不能以自定义缓存失效或替换 Manager 弥补。

Store 默认 Scoped；无状态方言可 Singleton；连接不注册为 Singleton，也不让单个连接跨异步枚举并发复用。工厂与服务生命周期遵循 DI 原则，不由 Singleton 捕获 Scoped 服务。

### 14.3 内部职责

核心内部只共享连接执行、JSON 转换、固定实体列映射和单表查询表达式处理。方言接口覆盖标识符引用、参数占位符、参数赋值、普通字符串非空精确等值、分页、写入/删除锁、限量清理 SQL 和架构脚本。NULL 补偿与业务状态规则仍在核心/Store，不能分散为六份 provider 业务代码。

首版方言接口的确定成员是：`Provider`、`ParameterPrefix`、`MaximumParameters`（分块与 IN 保护上限）、`MaximumIdentifierLength`、`QuoteIdentifier`、`BuildExactStringEquals`、`BuildExactStringNotEqualsIncludingNull`、`BuildPaging`、`FormatDate`、`ReadDate`。`MaximumIdentifierLength` 是标识符字节上限（SQLite 无上限、SQL Server 128、PostgreSQL 63），`QuoteIdentifier` 超过该上限即拒绝，因为 PostgreSQL 会静默截断超长名字、生成 Store 再也找不到的表；部署副本渲染时对模板里每个 `__PREFIX__` 后缀套同一上限。`FormatDate`/`ReadDate` 负责日期参数绑定与列读取：SQLite 无原生日期类型，绑定固定格式 UTC 文本（`yyyy-MM-dd HH:mm:ss.ffffff`）并按文本解析；有原生日期类型的 provider 直接绑定 `DateTime`，PostgreSQL 需在写入前把 `Kind` 改为 `Unspecified`（Npgsql 6+ 拒绝以 `Utc` 写入 `timestamp without time zone`），读取后再标记为 UTC。`BuildExactStringNotEqualsIncludingNull` 生成把 NULL 视为不相等的精确不等谓词（`column IS NULL OR column <> @p`），供批量撤销使用；直接用 `<>` 会因三值逻辑静默丢掉 NULL 行。Store 不直接依赖驱动对日期的默认转换，因此新增方言时必须实现这几个成员。provider 方法（`UseSqlServer()` 等）由各自的 provider 包提供，核心包只暴露 `UseProvider(OpenIddictStoreProvider)` 作为它们共同的落地入口，避免六个 provider 包互相引用。方言实例无状态，注册为 Singleton；Store 为 Scoped。

标识符只有两个来源：固定的表/列名常量，以及经 `OpenIddictStoreIdentifiers` 校验的 schema/前缀。表前缀与 schema 是运行时配置（`SetTablePrefix`/`SetSchema`），部署脚本因此不能写死具体表名：仓库中的脚本文件一律以 `.sql.template` 交付，用 `__PREFIX__` 和 `__SCHEMA__` 两个占位符保存可配置位置，由 `OpenIddictStoreIdentifiers.RenderDeploymentScript(template, dialect, prefix, schema)` 按运行时同一套校验与引用规则生成部署副本后才执行。占位符不会留在任何可执行文本里；把模板直接交给数据库会因表名非法而失败，这是刻意的——它防止有人漏掉渲染步骤后误以为装好了。

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
| Tokens | ApplicationId Identifier(100)、AuthorizationId Identifier(100)、CreationDate UtcDateTime、ExpirationDate UtcDateTime、Payload LargeText、Properties Json、RedemptionDate UtcDateTime、ReferenceId Text(100)、Status Text(50)、Subject Text(400)、Type Text(150) |

ApplicationId 外键指向 Applications；AuthorizationId 指向 Authorizations。所有外键默认使用限制删除，由 Store 明确执行关联处理，避免不同数据库对级联路径的支持差异。OpenIddict 8.x 的 Resources、Sessions 表及 Token.SessionId 列待其稳定版发布后再补入本节。

列映射没有第二套元数据：实体属性名即列名（`Id`→`Id`），主表名见本节表格，类型由第 15.5 节按 provider 决定。实体上不使用特性或 Fluent 映射，Store 的 SQL 直接引用这些名称；因此新增属性必须同时改本节、Store SQL 与安装脚本，不能靠自动映射。`ConcurrencyToken` 无原生访问器，只由 Store 维护。

### 15.3 辅助索引表

辅助表的以下各列均为 NOT NULL；所属实体 ID 为 Identifier(100) 并建立限制删除外键，摘要为 Hash32，Position 为非负整数。完整值不进入主键或唯一键。

| 表 | 列 | 主键 | 非唯一查找索引 |
| --- | --- | --- | --- |
| ApplicationRedirectUris | ApplicationId、UriHash、Uri LargeText、Position | (ApplicationId, Position) | (UriHash, ApplicationId) |
| ApplicationPostLogoutRedirectUris | ApplicationId、UriHash、Uri LargeText、Position | (ApplicationId, Position) | (UriHash, ApplicationId) |
| AuthorizationScopes | AuthorizationId、ScopeHash、Scope LargeText、Position | (AuthorizationId, Position) | (ScopeHash, AuthorizationId) |
| ScopeResources | ScopeId、ResourceHash、Resource LargeText、Position | (ScopeId, Position) | (ResourceHash, ScopeId) |

重复值按 StringComparer.Ordinal 去重后赋 Position，辅助表顺序不覆盖 JSON 内原始顺序。摘要统一由应用对未经标准化的完整字符串 UTF-8 字节计算 SHA-256，不依赖数据库编码、排序规则或哈希函数。索引只筛选候选所属实体，再按其权威 JSON 中的完整值作 Ordinal 复核，并保证同一实体只返回一次。不同完整值即使摘要相同也各占一行；不得按摘要去重或添加摘要唯一约束。

Scope/Resource 集合值不限定为 200 字符，且 `api` 与 `api ` 是两个不同元素；这些值与主表 Scope.Name 的有界列合同分开。不能依赖二进制排序规则解决 SQL Server 字符串等值比较的尾随空格折叠，也不能在插入失败后用查询复核补救。

### 15.4 必需索引

| 主表 | 约束/索引 |
| --- | --- |
| Applications | ClientId 非空值唯一 |
| Authorizations | (ApplicationId, Status, Subject, Type)；按 Subject 查找索引；按 CreationDate 清理索引 |
| Scopes | Name 非空值唯一 |
| Tokens | ReferenceId 非空值唯一；(ApplicationId, Status, Subject, Type)；AuthorizationId；Subject；CreationDate/ExpirationDate |

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

#### 16.1.1 普通字符串的精确等值与条件写入

本节覆盖 Id/外键、ConcurrencyToken、ClientId、Name、ReferenceId、LoginId、Subject、Status、Type 等有界字符串的等值、不等值和成员匹配，适用于固定查找、IQueryable、CAS、关联集合计算、Revoke 和 Prune。非 NULL 值的等值按 StringComparison.Ordinal 定义：区分大小写、重音、尾随空格和 Unicode 规范化形式，不做 Trim、大小写转换或其他标准化。两个 NULL 的比较及取反按第 9.1 节补偿；固定可选参数 null/空串“不筛选”的既有规则保持不变。这是本扩展的确定保证，不宣称上游 EF Core 的批量操作已独立于数据库排序规则提供相同语义。

方言负责生成非空精确等值谓词，核心统一复用它构造条件，不把比较规则复制到各 Store。SQL Server 对 nvarchar 列/参数须使用二进制 BIN2 比较并同时检查 `DATALENGTH(column) = DATALENGTH(parameter)`，因为 BIN2 本身不能区分 `alice` 与 `alice ` 的等值比较；列与列比较同样检查两边长度，不能用忽略尾空格的 LEN。其他 provider 选择能够保留完整值、区分大小写和尾空格的二进制比较方式，必要时结合长度或字节比较；MySQL/MariaDB 的 PAD SPACE、Oracle/DM 的会话排序设置均不能改变结果。具体 SQL 和参数类型在对应真实驱动/服务器 fixture 验证，不能通过截断、大小写转换或内存整表复核实现条件写入。

所有 Revoke 的键、Subject、Status/Type 条件，以及 Prune 的状态/类型判断都使用上述规则；`Status 非 revoked` 是精确不等并包含 NULL，不能因为数据库将 `revoked ` 视为 revoked 而漏更新。Manager 的附加过滤仅是读取后的防线，批量 Revoke 直接进入 Store，不存在写入后的 Manager 二次过滤。关联写入也必须在同一事务内确认存在精确匹配的父键，不能接受仅因数据库折叠大小写/尾空格而命中的父记录；数据库外键继续承担完整性兜底。

本节规定谓词语义，不自动放宽数据库的主键/唯一键约束。例如 SQL Server 单列唯一索引仍可能拒绝仅尾空格不同的两个 ClientId；对应 provider 须在使用说明与真库记录中说明该约束，并将冲突作为创建/更新失败报告，不能静默修剪输入、认作更新另一实体，或声称查询复核已使唯一索引区分尾空格。物理索引与约束由各 provider 的版本化 SQL 定义。

必需测试至少包括并存的 Subject=`alice`、`Alice`、`alice `，以及重音/Unicode 规范化形式不同的值：Find、查询回调及 Revoke 仅匹配完整指定值，未命中行的状态与并发标记不变；对 Status=`revoked`、`revoked `、NULL 验证逐方法撤销及 Prune。对键/CAS 参数的大小写或尾空格变体不得操作原记录；对只存有 ClientId=`client ` 的应用，按 `client` 查找不得返回它。真实 SQL、单元断言与七种服务器结果均须覆盖这些边界。

### 16.2 原子写入顺序

创建集合实体时：开始事务 → 插入主表 → 插入去重后的辅助索引 → 提交。任一失败均回滚。

更新时：生成候选新并发标记 → 以 Id/旧标记条件更新主表 → 验证影响行数为 1 → 重建受影响的集合索引 → 提交 → 更新内存实体标记。0 行即并发异常；回滚后仍保留旧实体标记。

删除时：在事务内按 Id/旧标记取得根记录写入权 → 按以下规则处理子记录与辅助表 → 删除根记录并确认 CAS 成功 → 提交。相关 Authorization 也须取得方言所需的写入锁，保证间接关联的并发写入受保护；不得只锁 Application 就假设所有间接关系不再变化。并发创建子项要么发生在删除之前并被正确处理，要么因外键/锁竞争失败；不允许产生孤儿或返回假成功。

#### 16.2.1 普通删除与关联失效

首版按 7.x 的四类实体执行删除。OpenIddict 8.x 引入 Session 后，关联失效范围会扩大，届时应以锁定的 8.x 稳定版 EF Core 非批量 DeleteAsync 路径为参照重写本节；现在不按预览版行为实现，也不预留切换删除语义的批量操作选项。

| 普通删除入口 | 必须删除 | 必须保留/清空 |
| --- | --- | --- |
| Application | Application、其 Authorization、直接或经这些 Authorization 关联的 Token、相应辅助行 | 集合外实体保留 |
| Authorization | Authorization、所有 AuthorizationId 指向它的 Token、AuthorizationScopes | 其他父实体保留 |
| Token | 该 Token | Application、Authorization 保留 |
| Scope | Scope 及其 ScopeResources 辅助行 | Scope.Resources 中的集合值保留 |
| Session、Resource | 首版不适用（7.7.2 无对应 Store） | — |

Application 删除使用以下集合，全部在同一连接、同一事务及上述锁保护下确定并处理：

- A0：ApplicationId 等于待删 Application.Id 的所有 Authorization。
- A：即 A0；7.7.2 无 Session，不存在经 Session 间接扩充的授权。
- T：ApplicationId 等于待删 Application.Id，或 AuthorizationId 属于 A 的所有 Token。各条件取并集，不要求 Token 的 ApplicationId 与 Authorization 所属应用一致。

集合按上面列出的关系计算，不沿 Token 外键反向扩展 A，也不删除其他 Application 主记录。

执行顺序固定为：

1. 确认根记录旧并发标记并取得 A 中记录所需的写入锁，确定受保护的关联集合。
2. 删除 T。
3. 删除 A 的 AuthorizationScopes 辅助行，再删除 A。
4. 删除该 Application 的两张 URI 辅助表行，再按根记录旧并发标记条件删除 Application，确认影响 1 行后提交。

因此，Token.ApplicationId 为空但 AuthorizationId 属于 A 的 Token 也会删除，而 AuthorizationId 在 A 之外但 ApplicationId 属于待删 Application 的 Token 同样删除；该 Token 引用的外部 Authorization 保留。

单独删除 Authorization 时：取得根记录写入权；删除 AuthorizationId 指向它的全部 Token，即使该 Token 的 ApplicationId 为空或属于其他应用；删除 AuthorizationScopes；按根记录旧标记删除 Authorization 并提交。所有入口都必须验证根记录删除影响行数为 1，失败则回滚。

Token、Scope 的单体删除不需要关联集合：Token 删除只按 Id 与旧并发标记删除该行；Scope 删除先删其 ScopeResources 辅助行，再按旧并发标记删除 Scope 行，Scope.Resources 中保存的集合值随 Scope 行一起消失，不反向影响任何独立实体。

协议验收场景固定为：A 与 T 均为 valid，T.AuthorizationId=A、T.ApplicationId=NULL；普通删除 A 后 T 必须不存在，携带该令牌的原有凭据在启用对应存储校验的 Server 路径中必须被拒绝。禁用实体缓存仍不追溯中止已经完成校验的在途请求。

上述流程通过共享内部 SQL/事务辅助方法实现，不调用会另开连接或独立提交的公共 Store DeleteAsync。保留行的外键清空与并发标记设置必须是同一 UPDATE；一次批量语句可为受影响行设置同一新 GUID 标记，并发标记不要求跨行唯一。任何根记录版本冲突、约束错误或中途失败均回滚全部关联变更；成功后重新查询获取新状态，旧快照的更新必须返回 0 行并转换为原生 ConcurrencyException。

#### 16.2.2 批量撤销的逐方法合同

以下规则以 7.7.2 标签的 EF Core Store 筛选谓词为依据。这里的 client 参数表示 Application 主键，不是 ClientId。可选参数为 null/空串时不应用相应条件；RevokeBy* 的指定键必须非 null/空串。表内等值/不等值均采用第 16.1.1 节的精确比较；下文 `Status <> @revoked` 表示其逻辑含义，实际方言还须补足精确比较和 NULL 处理，不能直接照抄依赖默认排序规则的 SQL。

| Store/方法 | 完整筛选条件 | 已 revoked 的匹配行 |
| --- | --- | --- |
| Token.RevokeAsync(subject, client, status, type) | 所有非空可选参数对应 Subject/ApplicationId/Status/Type 的等值条件相与 | 不自动排除，满足完整筛选即更新并计数 |
| Token.RevokeByApplicationIdAsync(id) | ApplicationId=id，且 Status 非 revoked | 排除，不更新、不计数 |
| Token.RevokeByAuthorizationIdAsync(id) | AuthorizationId=id，且 Status 非 revoked | 排除，不更新、不计数 |
| Token.RevokeBySubjectAsync(subject) | Subject=subject，且 Status 非 revoked | 排除，不更新、不计数 |
| Authorization.RevokeAsync(subject, client, status, type) | 所有非空可选参数对应 Subject/ApplicationId/Status/Type 的等值条件相与 | 不自动排除，满足完整筛选即更新并计数 |
| Authorization.RevokeByApplicationIdAsync(id) | ApplicationId=id | 更新并计数 |
| Authorization.RevokeBySubjectAsync(subject) | Subject=subject | 更新并计数 |

“Status 非 revoked”包含 Status 为 NULL，SQL 必须将条件分组为 `AND (Status IS NULL OR (Status IS NOT NULL AND NOT EqualNonNull(Status, @revoked)))` 等等价谓词，其中 EqualNonNull 为第 16.1.1 节的非空精确等值，使 NULL 分支仍受 ApplicationId/AuthorizationId/Subject 条件约束。显式 status 筛选只匹配精确相等值，不包含 NULL；省略 status 时，通用 RevokeAsync 也包含其他条件匹配的 NULL 状态行。三个 Token.RevokeBy* 的额外排除规则不能加入通用撤销或 Authorization.RevokeBy*。

每次操作使用单条条件 UPDATE，仅对满足该方法完整谓词的行设置 Status=revoked 和新的 ConcurrencyToken，并返回实际命中更新的行数。已 revoked 但依该方法仍命中的行也须轮换标记；被排除的行保留原标记。可以为一次语句的全部命中行设置同一新 GUID，不能要求并发标记跨行唯一。该标记轮换是本扩展防止旧快照覆盖的明确保证，EF Core 参考批量语句本身只设置 Status，不能据此宣称实现细节完全相同。不同驱动的匹配行/变化行差异通过参数适配和真实数据库测试统一。

必需计数示例：同一 Subject 下有一个 valid 和一个 revoked Token，Token.RevokeBySubjectAsync 首次返回 1、再次返回 0；对同样初始数据调用不带 status 筛选的 Token.RevokeAsync，两次均返回 2。对同样初始状态的 Authorization 调用 RevokeBySubjectAsync，两次均返回 2。显式 status=valid 的通用撤销首次返回 1、再次返回 0。另加入 NULL 状态行验证它在无 status 筛选和 Token.RevokeBy* 中被命中，在显式 status=valid/revoked 中不被命中；其他指定键不匹配的 NULL 状态行始终不受影响。每个示例同时断言命中行/未命中行的并发标记变化。

这些方法只更新目标 Store 的实体，不隐式级联撤销另一类实体；授权与令牌的后续协议校验仍由原生 Manager/Server 负责。

### 16.3 清理条件

每次 Prune 捕获一个 UTC now，循环至没有可删项或达到规定批次数上限。只处理 CreationDate 严格早于 threshold 的行，CreationDate 为 null 不清理。候选按 Id 稳定选择，每批至多 500 条，单次至多 1,000 批。

| 对象 | 满足阈值后还必须满足的条件 |
| --- | --- |
| Token | 状态既非 inactive 也非 valid；或者确有关联 Authorization 且其状态非 valid；或者 ExpirationDate 严格早于 now |
| Authorization | 状态非 valid 或 Type 为 ad-hoc，并且不存在关联 Token |

“状态非 valid”等不等比较必须包含应视为不相等的 NULL，不能直接依赖 SQL 三值逻辑的 `<>`/NOT IN。

Authorization 清理必须在事务与相应锁保护下重新验证完整条件，尤其是不存在关联 Token；最终 DELETE 仍携带完整清理谓词，并由限制删除的外键兜底。Prune 使用独立清理路径，不调用第 16.2.1 节会连带删除 Token 的普通 DeleteAsync，也不为了使父记录可删而删除并发出现的 Token。

Authorization 的“不存在关联 Token”指不存在 AuthorizationId 指向它的 Token。对最终不再满足清理条件的候选，不能提交中间变更。独立清理路径与普通删除路径的差异必须在使用文档中说明。

每批独立提交，删除时必须复核完整条件；并发标记只能作为附加检查，因为新建关联 Token 不会改变父记录标记。条件已不满足则保留该项；并发外键冲突等失败回滚该批并向上传播，之前已提交的批次不伪装成整次回滚。无自动重试未知写入、无忽略异常继续报告成功。

### 16.4 错误与诊断

参数错误、配置错误、并发冲突、约束错误、取消和数据库不可用要可区分。并发冲突转换为原生 ConcurrencyException；取消保留 OperationCanceledException；其他驱动错误保留原始异常链，不统一改成“未找到”或“认证失败”。

日志只记录 provider、操作名、耗时、影响行数和必要的错误分类，不记录连接串、ClientSecret、Payload、访问/刷新令牌、完整参数或授权主体个人数据。首版使用现有 ILogger，不另建诊断框架。

## 17. 架构版本与升级合同

每套表前缀有独立的架构版本表，逻辑名为 `{prefix}SchemaVersions`；单行记录 Series（7/8）、Version（从 1 递增）和 UpdatedUtc。prefix 默认 OpenIddict；表所属 schema 与主表一致。

provider 脚本目录、清单、不可变历史与版本递增遵守第 10 节，运行时校验该 provider 要求的 Series/Version。7→8 跨系列脚本单独命名，最后在结构和数据步骤成功后更新版本记录。Oracle/DM 等 DDL 可能隐式提交，不能声称外层事务可回滚所有迁移。

首次实际使用存储时检查 Series/Version，结果只缓存在该存储配置的生命周期内。数据库版本不符则拒绝操作并给出所需脚本版本；不自动修改数据库，不继续对不兼容表结构写入。更换数据库或升级 schema 后重启应用，不能用同一配置的连接工厂在不同 schema 之间轮换。

实现形态（E-030）：校验位于四个 Store 共用的建连入口，任何操作（含 Count、Prune、批量撤销）在触碰表之前先过闸门。所需版本是核心常量 `OpenIddictStoreSchema.RequiredSeries`/`RequiredVersion`，并与随包 `manifest.json` 的 `series`/`targetSeries`/`targetVersion` 由测试互锁，避免“代码要求某版本、脚本装出另一版本”的错配。校验成功走无连接的快路径，不增加每次操作的往返；成功结果记在 Scoped 的 Store 实例上，因此生命周期等于该存储配置。失败不缓存——失败后补跑脚本可在同一实例上重试成功。拒绝时抛 `OpenIddictStoreSchemaVersionException`，消息给出观测到的系列/版本、所需的系列/版本，并声明本存储不会自动建表或改表。缺表、缺行、错系列、错版本分别有独立诊断，便于按已有对象与版本记录恢复。

跨系列升级步骤固定为：备份 → 停止旧写入 → 校验当前系列/架构版本和脚本清单 → 执行已审阅脚本 → 验证数据与约束 → 部署新系列 → 运行冒烟测试。失败时保留已有版本记录及已执行步骤的证据，按数据库实际已提交的 DDL 恢复，不强制标记成功。

## 18. 构建、持续集成和发布合同

所有项目加入 `.slnx`，通过 `.NET 10 SDK` 构建。**仓库不使用 `global.json` 固定补丁版本**：换一台机器或换一个 SDK 补丁就可能因版本号对不上而无法还原构建，代价远高于收益。工程只要求 .NET 10 SDK（`net10.0` 目标框架本身即版本下限），实际使用的 SDK 在完成记录里如实登记，不作机器绑定。常规入口包括 Release build、test、pack 与格式/差异检查。

CI 分三个阶段：

1. 构建每个目标 TFM，执行纯单元测试和 SQLite 合同测试，检查包内容、依赖、公开 API 与最小消费者，以及 SQL 清单、历史不可变性和包内文件一致性；同时检查第 4.1 节的中央包管理约束（工程文件无 `Version` 属性、包依赖版本与中央清单一致、产品包不带驱动依赖）和第 4.1.1 节的逐 TFM 依赖组（7.x 低 TFM 组不得出现高代际 Microsoft.Extensions 依赖）。
2. 在专用环境执行全部声明支持的服务器/驱动组合，以及 Manager/Server、安装/升级链及结构一致性、事务与并发测试。Oracle/DM 自托管任务也是发布必需结果。
3. 只有前两阶段成功才产生可发布 nupkg/snupkg 候选；所有子包使用相同版本并精确依赖同版核心/Models。

普通 PR 可在缺少部分外部数据库时给出明确缺口，但不能获得“六数据库已验证”的发布结论。实际发布到 NuGet 由单独发布操作完成，凭据由 CI secrets 提供，不放入仓库。

测试进程虽统一 net10，针对 netstandard2.0/2.1 编译资产仍应有独立加载/调用验证，覆盖低 TFM 的读取、释放与异步接口路径。该验证说明“此资产在 net10 宿主下运行”，不能改写成已验证所有旧宿主。

## 19. 需求与验收追踪

| 需求 | 对应实现 | 必需证据 |
| --- | --- | --- |
| Sharp 包名前缀 | 第 4、14 节 | 包与程序集名称检查 |
| OpenIddict 7.7.2 基线 | 第 2、3、17 节 | 按 7.7.2 真实接口编译、不同 schema 误用拒绝 |
| 五个目标框架 | 所有发布类库 | 五 TFM 构建、包资产与消费者检查；实际运行宿主另列 |
| 依赖版本中央管理 | 第 4.1、4.1.1 节 | 工程文件无 `Version` 属性、包依赖与中央清单一致、产品包不带驱动依赖、逐 TFM 依赖组与代际匹配 |
| 六类数据库 | 六 provider、公共 Store | 七种服务器（含单独 MariaDB）的合同测试；DM 声明模式测试 |
| 数据库 SQL 持续维护 | 第 10/17/18 节、各 provider 的 Sql 目录 | 不可变历史、清单/包摘要、逐库安装/升级链、结构一致性、数据保留和恢复证据 |
| 原生协议接入 | Manager/Server 及示例，实体缓存禁用 | 发令牌、PKCE、刷新、同一及跨作用域撤销后重读；授权普通删除后其令牌被拒绝；重新启用缓存被拒绝 |
| .NET 10 测试/示例 | tests、samples | net10 运行，全部工程由 slnx 管理 |
| 无内存整表回退 | 固定 SQL、有限查询翻译器 | 不支持表达式拒绝、分页与大结果枚举测试 |
| 集合原生语义 | JSON、摘要候选、完整值 Ordinal 复核 | 超过 200 字符、大小写/尾随空格区分、强制摘要碰撞、全部 RequiredScopes 匹配 |
| 普通字符串精确比较 | 第 16.1.1 节的方言谓词与条件写入 | Subject/键/状态的大小写、重音、尾空格、规范化形式边界；撤销未命中行不变、CAS 误匹配拒绝与物理唯一键限制说明 |
| 查询 NULL 语义 | 第 9.1 节的二值补偿 | 两张真值表、可空列比较、含 NULL 的 Contains/取反、布尔组合、Oracle/DM 空串与 NULL 参数区分 |
| 逐方法撤销合同 | 第 16.2.2 节的七种批量撤销方法 | valid/revoked/NULL、显式状态筛选、重复调用计数、命中/未命中行标记变化 |
| 持久化可靠性 | CAS、事务、外键、版本 SQL | 冲突、取消、部分失败、并发关联写入和真实 LOB 测试；普通删除矩阵与 A/S/T 完整性、独立 Prune 路径、保留行标记轮换、旧快照冲突与整笔回滚 |

## 20. 设计维护规则

本文保持固定路径 `docs/design.md`。README 链接到本文；不以带日期的过程文档或会话记录替代设计。

影响公共 API、支持矩阵、schema、关键查询/事务语义的变更，必须在同一变更中更新设计与测试；涉及数据库结构时按第 10 节同步维护各库 SQL、清单和升级链。[开发与进度文档](development.md) 固定维护实施任务、复选框、验收矩阵和完成证据；它引用本文，不能悄悄重定义要求。

评审通过后将状态更新为“已确认”，再开始工程与实现；此处的评审状态只表示设计尚待用户审阅，不表示需要额外批准阅读、整理文档等已授权工作。
