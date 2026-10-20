# ServerAcceptanceTests — 真实服务器验收(独立项目)

这个项目**不在 `Sharp.OpenIddict.Stores.slnx` 中,CI 不会碰它**。它只在被显式调用时运行,专门用于把
PostgreSQL / Oracle / DM8 方言中"SQLite 上证明不了"的声明放到真实服务器上验证(精确比较、微秒精度、
各模式分页子句、唯一键、级联删除、批量撤销等)。

它不是单元测试,也不替代单元测试:仓库的单元与契约测试不碰任何服务器,本项目只做真库验收。

## 配置(连接串只进 user-secrets,不进仓库)

```bash
dotnet user-secrets set --id OpenIddictStoresServerAcceptance --project tests/Sharp.OpenIddict.Stores.ServerAcceptanceTests "PostgreSql" "Host=...;Port=5432;Database=...;Username=...;Password=..."
dotnet user-secrets set --id OpenIddictStoresServerAcceptance "Oracle"      "User Id=...;Password=...;Data Source=(DESCRIPTION=...)"
dotnet user-secrets set --id OpenIddictStoresServerAcceptance "DmNative"    "Server=...;Port=...;User Id=...;Password=..."
dotnet user-secrets set --id OpenIddictStoresServerAcceptance "DmNativeSchema" "hidos"
dotnet user-secrets set --id OpenIddictStoresServerAcceptance "DmOracle"    "Server=...;Port=...;User Id=...;Password=..."
dotnet user-secrets set --id OpenIddictStoresServerAcceptance "DmOracleSchema" "hidos"
```

secrets.json 位于用户目录(`%APPDATA%\Microsoft\UserSecrets\OpenIddictStoresServerAcceptance\`),
仓库文件、构建产物、CI 变量里都不会出现连接串。未配置的套件在结果中如实显示为 skipped 并注明原因。

## 运行

```bash
# PostgreSQL / Oracle(net8 或 net10 均可)
dotnet test tests/Sharp.OpenIddict.Stores.ServerAcceptanceTests -c Release -f net8.0 --filter "FullyQualifiedName~PostgreSql"
dotnet test tests/Sharp.OpenIddict.Stores.ServerAcceptanceTests -c Release -f net8.0 --filter "FullyQualifiedName~Oracle"

# DM8 必须在 net8.0 宿主执行:DM.DmProvider 没有 net10 资产,net10 上会静默退回 netstandard 资产(design 5.1)。
# DmAcceptanceFact 会在 net10 宿主上直接拒跑并注明原因。
dotnet test tests/Sharp.OpenIddict.Stores.ServerAcceptanceTests -c Release -f net8.0 --filter "FullyQualifiedName~Dm"
```

## 隔离与清理承诺

- 每次运行用 **本次运行唯一的表前缀**(`Oit` + 12 位 GUID 片段,如 `Oit0a1b2c3d4e5fApplications`),
  与默认前缀 `OpenIddict` 的任何既有部署互不干扰。
- 清理时先读数据字典,再**只删**"目录里存在 ∧ 属于本次运行九张表"的对象;目录读不出来就不删,
  让失败浮出来,绝不猜测性 DROP。
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
