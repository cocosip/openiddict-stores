# OpenIddict 7.7.2 Store 合同清单（BASE-02）

文档版本：1.0。创建日期：2026-10-19。对应设计：[项目设计文档 v1.7](../design.md)。对应任务：BASE-02。

本清单冻结 `OpenIddict.Abstractions` **7.7.2**（`PublicKeyToken=35a561290d20de2f`）中四个 Store 合同的全部成员，作为实现与合同测试的唯一比对基线。清单由反射生成并人工核对；成员签名以本清单为准，实现完成时把对应行的“状态”改为“已实现”并附测试名，不得删除行或改变顺序。

## 1. 基线与边界

| 项目 | 内容 |
| --- | --- |
| 包与版本 | `OpenIddict.Abstractions` 7.7.2（稳定版，无预览依赖） |
| 程序集标识 | `OpenIddict.Abstractions, Version=7.7.2.0, Culture=neutral, PublicKeyToken=35a561290d20de2f` |
| Store 数量 | 4：Application、Authorization、Scope、Token |
| 成员总数 | 145（42 + 32 + 28 + 43） |
| 实体约束 | 每个接口均为 `where T : class`；无 Resource、Session 接口，无 `Token.SessionId` |
| 上游参照 | 标签 `7.7.2`（提交 `950cb13b`），EF Core Store 用于确认行为而非扩展合同 |

`IOpenIddictScopeStore` 仍以 `string` 表达 resource：`GetResourcesAsync`、`SetResourcesAsync`、`FindByResourceAsync(string resource, ...)`。资源不是独立实体，本项目不为它建表，只把它作为 Scope 上的集合值处理。

## 2. 与 8.x 的差异（只作说明，不实施）

以下差异是本项目不依赖 8.x 预览包的原因，也是将来单独立项时需要重做的部分：

- `CountAsync<TResult>` 增加 `TState` 重载；本清单中四个 `CountAsync` 查询重载均无 `TState` 参数。
- Authorization 与 Token 的 `FindAsync` 使用**位置**字符串参数（Authorization 另含 `ImmutableArray<string>? scopes`），不是元组参数。
- 无 `IOpenIddictResourceStore`、`IOpenIddictSessionStore`，Token 无 Session 相关成员。
- `RevokeByApplicationIdAsync`、`RevokeBySubjectAsync` 的 `CancellationToken` 带 `= default`，`RevokeByAuthorizationIdAsync` 不带；实现时不得因此改变接口形状。

## 3. 本项目附加合同

这些不是上游接口成员，但每个 Store 的实现都必须满足，并作为合同测试的一部分（引用设计章节）：

1. **NULL 补偿**：查询回调按设计 9.1 的 C# NULL 真值表生成谓词，不能用 SQL 三值逻辑或含 NULL 的 `NOT IN` 直接决定结果。
2. **精确字符串比较**：普通有界字符串的等值/不等值/成员匹配按设计 16.1.1，不依赖数据库排序规则；`alice`、`Alice`、`alice ` 互不命中。
3. **集合身份**：JSON 是权威数据，辅助表只做 SHA-256 候选筛选，返回前做完整值 Ordinal 复核（设计 7）。
4. **并发**：`UpdateAsync`、单体 `DeleteAsync` 按 Id 与旧并发标记精确 CAS，0 行抛原生 `ConcurrencyException`（设计 8）。
5. **删除矩阵**：Application/Authorization 的关联删除按设计 16.2.1，不走数据库级联；Scope 删除只连带其 ScopeResources 辅助行。
6. **批量撤销**：七个 Revoke 方法按设计 16.2.2；仅三个 `Token.RevokeBy*` 隐含排除精确 `revoked`。
7. **清理**：`PruneAsync` 走独立路径，按设计 16.3 的条件与批次限制，不以 CAS 代替条件重检。
8. **未实现成员不得占位**：任何尚未实现的成员必须保持“未实现”，不能返回默认值冒充通过。

## 4. 成员清单

状态取值：`未实现`（默认）、`已实现`（实现并有用例）、`不适用`（上游合同在该 TFM 下不可达，需注明原因）。

### IOpenIddictApplicationStore

成员数：42

| # | 签名 | 状态 |
| --- | --- | --- |
| 1 | `ValueTask<long> CountAsync(CancellationToken cancellationToken)` | 未实现 |
| 2 | `ValueTask<long> CountAsync(Func<IQueryable<TApplication>, IQueryable<TResult>> query, CancellationToken cancellationToken)` | 未实现 |
| 3 | `ValueTask CreateAsync(TApplication application, CancellationToken cancellationToken)` | 未实现 |
| 4 | `ValueTask DeleteAsync(TApplication application, CancellationToken cancellationToken)` | 未实现 |
| 5 | `ValueTask<TApplication> FindByClientIdAsync(string identifier, CancellationToken cancellationToken)` | 未实现 |
| 6 | `ValueTask<TApplication> FindByIdAsync(string identifier, CancellationToken cancellationToken)` | 未实现 |
| 7 | `IAsyncEnumerable<TApplication> FindByPostLogoutRedirectUriAsync(string uri, CancellationToken cancellationToken)` | 未实现 |
| 8 | `IAsyncEnumerable<TApplication> FindByRedirectUriAsync(string uri, CancellationToken cancellationToken)` | 未实现 |
| 9 | `ValueTask<string> GetApplicationTypeAsync(TApplication application, CancellationToken cancellationToken)` | 未实现 |
| 10 | `ValueTask<TResult> GetAsync(Func<IQueryable<TApplication>, TState, IQueryable<TResult>> query, TState state, CancellationToken cancellationToken)` | 未实现 |
| 11 | `ValueTask<string> GetClientIdAsync(TApplication application, CancellationToken cancellationToken)` | 未实现 |
| 12 | `ValueTask<string> GetClientSecretAsync(TApplication application, CancellationToken cancellationToken)` | 未实现 |
| 13 | `ValueTask<string> GetClientTypeAsync(TApplication application, CancellationToken cancellationToken)` | 未实现 |
| 14 | `ValueTask<string> GetConsentTypeAsync(TApplication application, CancellationToken cancellationToken)` | 未实现 |
| 15 | `ValueTask<string> GetDisplayNameAsync(TApplication application, CancellationToken cancellationToken)` | 未实现 |
| 16 | `ValueTask<ImmutableDictionary<CultureInfo, string>> GetDisplayNamesAsync(TApplication application, CancellationToken cancellationToken)` | 未实现 |
| 17 | `ValueTask<string> GetIdAsync(TApplication application, CancellationToken cancellationToken)` | 未实现 |
| 18 | `ValueTask<JsonWebKeySet> GetJsonWebKeySetAsync(TApplication application, CancellationToken cancellationToken)` | 未实现 |
| 19 | `ValueTask<ImmutableArray<string>> GetPermissionsAsync(TApplication application, CancellationToken cancellationToken)` | 未实现 |
| 20 | `ValueTask<ImmutableArray<string>> GetPostLogoutRedirectUrisAsync(TApplication application, CancellationToken cancellationToken)` | 未实现 |
| 21 | `ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(TApplication application, CancellationToken cancellationToken)` | 未实现 |
| 22 | `ValueTask<ImmutableArray<string>> GetRedirectUrisAsync(TApplication application, CancellationToken cancellationToken)` | 未实现 |
| 23 | `ValueTask<ImmutableArray<string>> GetRequirementsAsync(TApplication application, CancellationToken cancellationToken)` | 未实现 |
| 24 | `ValueTask<ImmutableDictionary<string, string>> GetSettingsAsync(TApplication application, CancellationToken cancellationToken)` | 未实现 |
| 25 | `ValueTask<TApplication> InstantiateAsync(CancellationToken cancellationToken)` | 未实现 |
| 26 | `IAsyncEnumerable<TApplication> ListAsync(int? count, int? offset, CancellationToken cancellationToken)` | 未实现 |
| 27 | `IAsyncEnumerable<TResult> ListAsync(Func<IQueryable<TApplication>, TState, IQueryable<TResult>> query, TState state, CancellationToken cancellationToken)` | 未实现 |
| 28 | `ValueTask SetApplicationTypeAsync(TApplication application, string type, CancellationToken cancellationToken)` | 未实现 |
| 29 | `ValueTask SetClientIdAsync(TApplication application, string identifier, CancellationToken cancellationToken)` | 未实现 |
| 30 | `ValueTask SetClientSecretAsync(TApplication application, string secret, CancellationToken cancellationToken)` | 未实现 |
| 31 | `ValueTask SetClientTypeAsync(TApplication application, string type, CancellationToken cancellationToken)` | 未实现 |
| 32 | `ValueTask SetConsentTypeAsync(TApplication application, string type, CancellationToken cancellationToken)` | 未实现 |
| 33 | `ValueTask SetDisplayNameAsync(TApplication application, string name, CancellationToken cancellationToken)` | 未实现 |
| 34 | `ValueTask SetDisplayNamesAsync(TApplication application, ImmutableDictionary<CultureInfo, string> names, CancellationToken cancellationToken)` | 未实现 |
| 35 | `ValueTask SetJsonWebKeySetAsync(TApplication application, JsonWebKeySet set, CancellationToken cancellationToken)` | 未实现 |
| 36 | `ValueTask SetPermissionsAsync(TApplication application, ImmutableArray<string> permissions, CancellationToken cancellationToken)` | 未实现 |
| 37 | `ValueTask SetPostLogoutRedirectUrisAsync(TApplication application, ImmutableArray<string> uris, CancellationToken cancellationToken)` | 未实现 |
| 38 | `ValueTask SetPropertiesAsync(TApplication application, ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken)` | 未实现 |
| 39 | `ValueTask SetRedirectUrisAsync(TApplication application, ImmutableArray<string> uris, CancellationToken cancellationToken)` | 未实现 |
| 40 | `ValueTask SetRequirementsAsync(TApplication application, ImmutableArray<string> requirements, CancellationToken cancellationToken)` | 未实现 |
| 41 | `ValueTask SetSettingsAsync(TApplication application, ImmutableDictionary<string, string> settings, CancellationToken cancellationToken)` | 未实现 |
| 42 | `ValueTask UpdateAsync(TApplication application, CancellationToken cancellationToken)` | 未实现 |

### IOpenIddictAuthorizationStore

成员数：32

| # | 签名 | 状态 |
| --- | --- | --- |
| 1 | `ValueTask<long> CountAsync(CancellationToken cancellationToken)` | 未实现 |
| 2 | `ValueTask<long> CountAsync(Func<IQueryable<TAuthorization>, IQueryable<TResult>> query, CancellationToken cancellationToken)` | 未实现 |
| 3 | `ValueTask CreateAsync(TAuthorization authorization, CancellationToken cancellationToken)` | 未实现 |
| 4 | `ValueTask DeleteAsync(TAuthorization authorization, CancellationToken cancellationToken)` | 未实现 |
| 5 | `IAsyncEnumerable<TAuthorization> FindAsync(string subject, string client, string status, string type, ImmutableArray<string>? scopes, CancellationToken cancellationToken)` | 未实现 |
| 6 | `IAsyncEnumerable<TAuthorization> FindByApplicationIdAsync(string identifier, CancellationToken cancellationToken)` | 未实现 |
| 7 | `ValueTask<TAuthorization> FindByIdAsync(string identifier, CancellationToken cancellationToken)` | 未实现 |
| 8 | `IAsyncEnumerable<TAuthorization> FindBySubjectAsync(string subject, CancellationToken cancellationToken)` | 未实现 |
| 9 | `ValueTask<string> GetApplicationIdAsync(TAuthorization authorization, CancellationToken cancellationToken)` | 未实现 |
| 10 | `ValueTask<TResult> GetAsync(Func<IQueryable<TAuthorization>, TState, IQueryable<TResult>> query, TState state, CancellationToken cancellationToken)` | 未实现 |
| 11 | `ValueTask<DateTimeOffset?> GetCreationDateAsync(TAuthorization authorization, CancellationToken cancellationToken)` | 未实现 |
| 12 | `ValueTask<string> GetIdAsync(TAuthorization authorization, CancellationToken cancellationToken)` | 未实现 |
| 13 | `ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(TAuthorization authorization, CancellationToken cancellationToken)` | 未实现 |
| 14 | `ValueTask<ImmutableArray<string>> GetScopesAsync(TAuthorization authorization, CancellationToken cancellationToken)` | 未实现 |
| 15 | `ValueTask<string> GetStatusAsync(TAuthorization authorization, CancellationToken cancellationToken)` | 未实现 |
| 16 | `ValueTask<string> GetSubjectAsync(TAuthorization authorization, CancellationToken cancellationToken)` | 未实现 |
| 17 | `ValueTask<string> GetTypeAsync(TAuthorization authorization, CancellationToken cancellationToken)` | 未实现 |
| 18 | `ValueTask<TAuthorization> InstantiateAsync(CancellationToken cancellationToken)` | 未实现 |
| 19 | `IAsyncEnumerable<TAuthorization> ListAsync(int? count, int? offset, CancellationToken cancellationToken)` | 未实现 |
| 20 | `IAsyncEnumerable<TResult> ListAsync(Func<IQueryable<TAuthorization>, TState, IQueryable<TResult>> query, TState state, CancellationToken cancellationToken)` | 未实现 |
| 21 | `ValueTask<long> PruneAsync(DateTimeOffset threshold, CancellationToken cancellationToken)` | 未实现 |
| 22 | `ValueTask<long> RevokeAsync(string subject, string client, string status, string type, CancellationToken cancellationToken)` | 未实现 |
| 23 | `ValueTask<long> RevokeByApplicationIdAsync(string identifier, CancellationToken cancellationToken)` | 未实现 |
| 24 | `ValueTask<long> RevokeBySubjectAsync(string subject, CancellationToken cancellationToken)` | 未实现 |
| 25 | `ValueTask SetApplicationIdAsync(TAuthorization authorization, string identifier, CancellationToken cancellationToken)` | 未实现 |
| 26 | `ValueTask SetCreationDateAsync(TAuthorization authorization, DateTimeOffset? date, CancellationToken cancellationToken)` | 未实现 |
| 27 | `ValueTask SetPropertiesAsync(TAuthorization authorization, ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken)` | 未实现 |
| 28 | `ValueTask SetScopesAsync(TAuthorization authorization, ImmutableArray<string> scopes, CancellationToken cancellationToken)` | 未实现 |
| 29 | `ValueTask SetStatusAsync(TAuthorization authorization, string status, CancellationToken cancellationToken)` | 未实现 |
| 30 | `ValueTask SetSubjectAsync(TAuthorization authorization, string subject, CancellationToken cancellationToken)` | 未实现 |
| 31 | `ValueTask SetTypeAsync(TAuthorization authorization, string type, CancellationToken cancellationToken)` | 未实现 |
| 32 | `ValueTask UpdateAsync(TAuthorization authorization, CancellationToken cancellationToken)` | 未实现 |

### IOpenIddictScopeStore

成员数：28

| # | 签名 | 状态 |
| --- | --- | --- |
| 1 | `ValueTask<long> CountAsync(CancellationToken cancellationToken)` | 未实现 |
| 2 | `ValueTask<long> CountAsync(Func<IQueryable<TScope>, IQueryable<TResult>> query, CancellationToken cancellationToken)` | 未实现 |
| 3 | `ValueTask CreateAsync(TScope scope, CancellationToken cancellationToken)` | 未实现 |
| 4 | `ValueTask DeleteAsync(TScope scope, CancellationToken cancellationToken)` | 未实现 |
| 5 | `ValueTask<TScope> FindByIdAsync(string identifier, CancellationToken cancellationToken)` | 未实现 |
| 6 | `ValueTask<TScope> FindByNameAsync(string name, CancellationToken cancellationToken)` | 未实现 |
| 7 | `IAsyncEnumerable<TScope> FindByNamesAsync(ImmutableArray<string> names, CancellationToken cancellationToken)` | 未实现 |
| 8 | `IAsyncEnumerable<TScope> FindByResourceAsync(string resource, CancellationToken cancellationToken)` | 未实现 |
| 9 | `ValueTask<TResult> GetAsync(Func<IQueryable<TScope>, TState, IQueryable<TResult>> query, TState state, CancellationToken cancellationToken)` | 未实现 |
| 10 | `ValueTask<string> GetDescriptionAsync(TScope scope, CancellationToken cancellationToken)` | 未实现 |
| 11 | `ValueTask<ImmutableDictionary<CultureInfo, string>> GetDescriptionsAsync(TScope scope, CancellationToken cancellationToken)` | 未实现 |
| 12 | `ValueTask<string> GetDisplayNameAsync(TScope scope, CancellationToken cancellationToken)` | 未实现 |
| 13 | `ValueTask<ImmutableDictionary<CultureInfo, string>> GetDisplayNamesAsync(TScope scope, CancellationToken cancellationToken)` | 未实现 |
| 14 | `ValueTask<string> GetIdAsync(TScope scope, CancellationToken cancellationToken)` | 未实现 |
| 15 | `ValueTask<string> GetNameAsync(TScope scope, CancellationToken cancellationToken)` | 未实现 |
| 16 | `ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(TScope scope, CancellationToken cancellationToken)` | 未实现 |
| 17 | `ValueTask<ImmutableArray<string>> GetResourcesAsync(TScope scope, CancellationToken cancellationToken)` | 未实现 |
| 18 | `ValueTask<TScope> InstantiateAsync(CancellationToken cancellationToken)` | 未实现 |
| 19 | `IAsyncEnumerable<TScope> ListAsync(int? count, int? offset, CancellationToken cancellationToken)` | 未实现 |
| 20 | `IAsyncEnumerable<TResult> ListAsync(Func<IQueryable<TScope>, TState, IQueryable<TResult>> query, TState state, CancellationToken cancellationToken)` | 未实现 |
| 21 | `ValueTask SetDescriptionAsync(TScope scope, string description, CancellationToken cancellationToken)` | 未实现 |
| 22 | `ValueTask SetDescriptionsAsync(TScope scope, ImmutableDictionary<CultureInfo, string> descriptions, CancellationToken cancellationToken)` | 未实现 |
| 23 | `ValueTask SetDisplayNameAsync(TScope scope, string name, CancellationToken cancellationToken)` | 未实现 |
| 24 | `ValueTask SetDisplayNamesAsync(TScope scope, ImmutableDictionary<CultureInfo, string> names, CancellationToken cancellationToken)` | 未实现 |
| 25 | `ValueTask SetNameAsync(TScope scope, string name, CancellationToken cancellationToken)` | 未实现 |
| 26 | `ValueTask SetPropertiesAsync(TScope scope, ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken)` | 未实现 |
| 27 | `ValueTask SetResourcesAsync(TScope scope, ImmutableArray<string> resources, CancellationToken cancellationToken)` | 未实现 |
| 28 | `ValueTask UpdateAsync(TScope scope, CancellationToken cancellationToken)` | 未实现 |

### IOpenIddictTokenStore

成员数：43

| # | 签名 | 状态 |
| --- | --- | --- |
| 1 | `ValueTask<long> CountAsync(CancellationToken cancellationToken)` | 未实现 |
| 2 | `ValueTask<long> CountAsync(Func<IQueryable<TToken>, IQueryable<TResult>> query, CancellationToken cancellationToken)` | 未实现 |
| 3 | `ValueTask CreateAsync(TToken token, CancellationToken cancellationToken)` | 未实现 |
| 4 | `ValueTask DeleteAsync(TToken token, CancellationToken cancellationToken)` | 未实现 |
| 5 | `IAsyncEnumerable<TToken> FindAsync(string subject, string client, string status, string type, CancellationToken cancellationToken)` | 未实现 |
| 6 | `IAsyncEnumerable<TToken> FindByApplicationIdAsync(string identifier, CancellationToken cancellationToken)` | 未实现 |
| 7 | `IAsyncEnumerable<TToken> FindByAuthorizationIdAsync(string identifier, CancellationToken cancellationToken)` | 未实现 |
| 8 | `ValueTask<TToken> FindByIdAsync(string identifier, CancellationToken cancellationToken)` | 未实现 |
| 9 | `ValueTask<TToken> FindByReferenceIdAsync(string identifier, CancellationToken cancellationToken)` | 未实现 |
| 10 | `IAsyncEnumerable<TToken> FindBySubjectAsync(string subject, CancellationToken cancellationToken)` | 未实现 |
| 11 | `ValueTask<string> GetApplicationIdAsync(TToken token, CancellationToken cancellationToken)` | 未实现 |
| 12 | `ValueTask<TResult> GetAsync(Func<IQueryable<TToken>, TState, IQueryable<TResult>> query, TState state, CancellationToken cancellationToken)` | 未实现 |
| 13 | `ValueTask<string> GetAuthorizationIdAsync(TToken token, CancellationToken cancellationToken)` | 未实现 |
| 14 | `ValueTask<DateTimeOffset?> GetCreationDateAsync(TToken token, CancellationToken cancellationToken)` | 未实现 |
| 15 | `ValueTask<DateTimeOffset?> GetExpirationDateAsync(TToken token, CancellationToken cancellationToken)` | 未实现 |
| 16 | `ValueTask<string> GetIdAsync(TToken token, CancellationToken cancellationToken)` | 未实现 |
| 17 | `ValueTask<string> GetPayloadAsync(TToken token, CancellationToken cancellationToken)` | 未实现 |
| 18 | `ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(TToken token, CancellationToken cancellationToken)` | 未实现 |
| 19 | `ValueTask<DateTimeOffset?> GetRedemptionDateAsync(TToken token, CancellationToken cancellationToken)` | 未实现 |
| 20 | `ValueTask<string> GetReferenceIdAsync(TToken token, CancellationToken cancellationToken)` | 未实现 |
| 21 | `ValueTask<string> GetStatusAsync(TToken token, CancellationToken cancellationToken)` | 未实现 |
| 22 | `ValueTask<string> GetSubjectAsync(TToken token, CancellationToken cancellationToken)` | 未实现 |
| 23 | `ValueTask<string> GetTypeAsync(TToken token, CancellationToken cancellationToken)` | 未实现 |
| 24 | `ValueTask<TToken> InstantiateAsync(CancellationToken cancellationToken)` | 未实现 |
| 25 | `IAsyncEnumerable<TToken> ListAsync(int? count, int? offset, CancellationToken cancellationToken)` | 未实现 |
| 26 | `IAsyncEnumerable<TResult> ListAsync(Func<IQueryable<TToken>, TState, IQueryable<TResult>> query, TState state, CancellationToken cancellationToken)` | 未实现 |
| 27 | `ValueTask<long> PruneAsync(DateTimeOffset threshold, CancellationToken cancellationToken)` | 未实现 |
| 28 | `ValueTask<long> RevokeAsync(string subject, string client, string status, string type, CancellationToken cancellationToken)` | 未实现 |
| 29 | `ValueTask<long> RevokeByApplicationIdAsync(string identifier, CancellationToken cancellationToken = default)` | 未实现 |
| 30 | `ValueTask<long> RevokeByAuthorizationIdAsync(string identifier, CancellationToken cancellationToken)` | 未实现 |
| 31 | `ValueTask<long> RevokeBySubjectAsync(string subject, CancellationToken cancellationToken = default)` | 未实现 |
| 32 | `ValueTask SetApplicationIdAsync(TToken token, string identifier, CancellationToken cancellationToken)` | 未实现 |
| 33 | `ValueTask SetAuthorizationIdAsync(TToken token, string identifier, CancellationToken cancellationToken)` | 未实现 |
| 34 | `ValueTask SetCreationDateAsync(TToken token, DateTimeOffset? date, CancellationToken cancellationToken)` | 未实现 |
| 35 | `ValueTask SetExpirationDateAsync(TToken token, DateTimeOffset? date, CancellationToken cancellationToken)` | 未实现 |
| 36 | `ValueTask SetPayloadAsync(TToken token, string payload, CancellationToken cancellationToken)` | 未实现 |
| 37 | `ValueTask SetPropertiesAsync(TToken token, ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken)` | 未实现 |
| 38 | `ValueTask SetRedemptionDateAsync(TToken token, DateTimeOffset? date, CancellationToken cancellationToken)` | 未实现 |
| 39 | `ValueTask SetReferenceIdAsync(TToken token, string identifier, CancellationToken cancellationToken)` | 未实现 |
| 40 | `ValueTask SetStatusAsync(TToken token, string status, CancellationToken cancellationToken)` | 未实现 |
| 41 | `ValueTask SetSubjectAsync(TToken token, string subject, CancellationToken cancellationToken)` | 未实现 |
| 42 | `ValueTask SetTypeAsync(TToken token, string type, CancellationToken cancellationToken)` | 未实现 |
| 43 | `ValueTask UpdateAsync(TToken token, CancellationToken cancellationToken)` | 未实现 |
