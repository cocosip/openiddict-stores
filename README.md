# Sharp.OpenIddict.Dapper

OpenIddict 的 Dapper 关系数据库存储扩展，规划支持 SQL Server、PostgreSQL、MySQL/MariaDB、SQLite、Oracle 和达梦。

[项目设计文档](docs/design.md) 是后续开发、测试与验收的依据。设计覆盖 OpenIddict 7.x/8.x 分版本兼容、包结构、公共接口、数据库模型、查询、事务、迁移、测试和示例；第 10 节规定各数据库 SQL 脚本的源码目录、版本与升级链、随包交付和持续维护规则。

[开发与进度文档](docs/development.md) 将设计拆分为可勾选的实施任务，记录阶段进度、完成条件、分版本/数据库验收矩阵及提交与验证证据；MIG-03/04 和第 5.1 节独立追踪各库 SQL 脚本的交付、升级与维护验收。后续开发完成一项，应同步更新任务状态和完成记录。

当前处于设计阶段，尚未提供可运行实现或正式发布包。
