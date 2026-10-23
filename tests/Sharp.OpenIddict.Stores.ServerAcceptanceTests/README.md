# ServerAcceptanceTests — 真实数据库验收(独立项目)

这个项目**不在 `Sharp.OpenIddict.Stores.slnx` 中,CI 不会碰它**。它是仓库里**唯一**的真库测试入口:
六种数据库的同一套合同都在这里跑,通过一个配置文件切换目标,不需要换命令、换项目或换环境变量。

它覆盖 SQLite、SQL Server、PostgreSQL、MySQL、MariaDB、Oracle、DM8(原生/Oracle 兼容/MySQL 兼容三种声明模式),
每套 15 项同一合同:安装 9 表与版本行、重装拒绝、Unicode/50 000 字符大值、微秒日期、精确比较(大小写/重音/尾随空格)、
唯一键 NULL 相异、CAS、分页(含无 count 的 offset)、关联删除、批量撤销(NULL status)、Prune、前置条件
(服务器版本、DM 的 `COMPATIBLE_MODE` 与 `CASE_SENSITIVE` 行为探测)。

它不是单元测试,也不替代单元测试:仓库的单元与契约测试不碰任何服务器,本项目只做真库验收。

## 切换数据库:一个文件

复制模板并填写要跑的那台服务器:

```bash
cp tests/Sharp.OpenIddict.Stores.ServerAcceptanceTests/databases.json.template \
   tests/Sharp.OpenIddict.Stores.ServerAcceptanceTests/databases.json
```

```jsonc
{
  // all = 跑所有已填连接串的条目;填 Sqlite / SqlServer / PostgreSql / MySql / MariaDb /
  // Oracle / DmNative / DmOracle / DmMySql 之一则只跑那一套
  "provider": "PostgreSql",
  "connectionStrings": {
    "SqlServer": "Server=.;Database=master;Integrated Security=true;TrustServerCertificate=true",
    "PostgreSql": "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=***"
  },
  // DM 各模式安装到哪个 schema;不填则用连接串的默认 schema
  "schemas": { "DmNative": "OIDC", "DmOracle": "OIDC", "DmMySql": "OIDC" }
}
```

- `databases.json` 已被 `.gitignore` 忽略,连接串不会进仓库;仓库里只有 `databases.json.template`。
- 填了但连不上 = **失败**(那才叫"已配置");没填 = **如实跳过并说明缺哪个键**;
  `provider` 没选中 = **跳过并说明选的是谁**。三种跳过原因分开写,因为修法不同。
- SQLite 不需要任何配置:它的目标是本次运行创建的临时文件,跑完即删,所以**没有任何数据库服务器的机器上
  仍然有 15 项真库证据**。

## 运行

```bash
# 全部已配置的目标(未配置的如实跳过)
dotnet test tests/Sharp.OpenIddict.Stores.ServerAcceptanceTests -c Release -f net8.0

# 只跑一个套件(provider 也指向它时才有意义)
dotnet test tests/Sharp.OpenIddict.Stores.ServerAcceptanceTests -c Release -f net8.0 --filter "FullyQualifiedName~PostgreSql"
```

**宿主必须按驱动资产选**:

| 套件 | 宿主 | 原因 |
| --- | --- | --- |
| SQLite / PostgreSQL / MySQL / MariaDB | net8.0、net9.0、net10.0 | 驱动有对应资产 |
| SQL Server | **net8.0 或 net9.0** | `Microsoft.Data.SqlClient` 7.1.1 无 net10 资产,net10 上会静默用 net9 资产 |
| Oracle | net8.0 或 net9.0 优先 | 驱动最高到 net8.0,net9/net10 走回退 |
| DM8 三模式 | **net8.0** | `DM.DmProvider` 8.3.1 无 net10 资产 |

SQL Server 与 DM 的套件在 net10 宿主上**直接拒跑并注明原因**,而不是给出一个不算数的绿。这是设计 5.1 的要求。

## 隔离与清理承诺

- **SQL Server** 自己 `CREATE DATABASE` 一个 `Oit` + 12 位 GUID 片段的库,跑完 `DROP DATABASE`;隔离是数据库边界。
- 其余服务器给不了独立库,于是用**本次运行唯一的表前缀**(`Oit` + 12 位 GUID 片段,如 `Oit0a1b2c3d4e5fApplications`),
  与默认前缀 `OpenIddict` 的任何既有部署互不干扰。
- 清理时先读数据字典,再**只删**"目录里存在 ∧ 属于本次运行九张表"的对象;目录读不出来就不删,
  让失败浮出来,绝不猜测性 DROP。
- 删除前还会用 `IsOwnedByThisTestRun` 复核名字(前缀 + GUID 片段),手写的 `Oit...` 前缀不会被当成自己的。
- SQLite 的库文件用同样的命名规则,删前复核文件名,清理失败时也会释放文件。
- 安装脚本不含 `IF EXISTS` 跳过:重复安装会在第一个同名对象处被拒绝(design 10.3)。
- 达梦套件还会先断言实例 `COMPATIBLE_MODE` 与所声明模式一致、`CASE_SENSITIVE=1`(见 DM `Sql/README.md`),
  不满足直接失败,而不是带着错误前提跑完。

## 2026-10-20 运行记录(E-044)

| 套件 | 服务器 | 宿主 | 结果 |
| --- | --- | --- | --- |
| PostgreSqlStoreContractTests | PostgreSQL(用户提供的实例) | net8.0/net9.0/net10.0 | 各 15/15 通过 |
| OracleStoreContractTests | Oracle 19c 级 SID 实例(用户提供) | net8.0 | 15/15 通过 |
| DmNativeStoreContractTests | DM8 COMPATIBLE_MODE=0(用户提供) | net8.0 | 15/15 通过 |
| DmOracleCompatibleStoreContractTests | DM8 COMPATIBLE_MODE=2(用户提供) | net8.0 | 15/15 通过 |

该轮验收暴露并已在仓库修复的缺陷:Oracle/达梦模板裸 `NULL` 约束(ORA-00922/达梦语法错误)、运行时 SQL 的
`AS` 表/视图别名(ORA-00907/00933)、`SELECT DISTINCT` 作用在含 LOB 的主表(ORA-00932)、达梦空白填充比较
(已加 `CHAR_LENGTH` 守卫)。达梦两实例当时缺少 `hidos` 模式,已按用户给定名称新建。清理复核:四台服务器
0 残留。详见 `docs/development.md` E-044。

## 2026-10-23 合并为统一入口(E-049)

此前真库入口有两个:本项目的 user-secrets 套件,和 `IntegrationTests` 里靠 `DSH_SQLSERVER` 环境变量的
SQL Server 套件。现在只剩这一个:

- 配置从 user-secrets 改为 `databases.json`,切换数据库是一次改值,而不是换环境变量或换项目。
- SQL Server 15 项从 `IntegrationTests` 迁入,并补上"自定义前缀 + schema"一项(16 项)。
- SQLite 15 项新增:它零配置,是唯一能在没有服务器的机器上产出真库证据的套件。
- `IntegrationTests` 不再引用 `Microsoft.Data.SqlClient`/`Npgsql`/`MySqlConnector`/`DM.DmProvider`
  (只留 SQLite 与 Oracle 参数绑定契约),不再有 `ServerFactAttribute`,不再有真库套件。
