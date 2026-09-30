# T12 current-schema migration-script 补丁

状态：implementation_ready / revalidation_pending。作者没有运行 dotnet、数据库连接或脚本；四项测试是契约数量，不是通过数。Low 的原 R1 scripts 轮次为 1/4 通过，3项失败集中于 named migration ID 解析；原失败证据保留，14位 ID 修复后的 fresh snapshot 待重验。

## 固定输入与应用顺序

- EF clean commit：`113014cc74dd1f751ef97226a78d2ec855b32c8c`。
- 只读参考：`.local/t11/ef/20260930-t11-public/O`；独立生成副本：`.local/t12/script-work`。
- 原脚本模型/断言文件 SHA-256：`b848c4858c2c8695b013a2cef4ae64ae3f14d4cc00bb8084f77a4251b5260220`。
- 原 `DamengScriptExecutor.cs` SHA-256：`a58f202a383f51d579d56d81cd89ffeb7f221b688354b158a9a998f68a18c376`。
- 先向新的 clean 官方 archive 应用本 patch，再由 main 应用 W package/namespace/TEST connection bridge；新文件保留 `using Dm`，不自行替换 provider SQL generator 或 modification batch。
- TEST runtime connection 来自 `DamengTestEnvironment.GetRequiredConnectionString()`。main 的 bridge 负责统一 typed W PlaintextAllowed、PersistSecurityInfo=false、TEST schema；本补丁不加载或复制 secrets。

补丁只新增两个文件，原 admin fixture、原 migration-script tests、executor、生产 provider 及其他现有源文件均保持原样：

| 新文件 | SHA-256 |
| --- | --- |
| `test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengCurrentSchemaScriptDatabase.cs` | `1d80aff9e9391fc438006859567ef53cbc4b552ac7e5e7886c1f0f0b6600930b` |
| `test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengCurrentSchemaScriptFunctionalTests.cs` | `8f0ed49cc2b5cd2a3db6271e44c7e5fa4752a9c8adc660d8b2fe96975a35dd63` |

Patch SHA-256：`6603ae6481c9683e50b7cf5db648206da35107fff8ed8afae31269bd3aca9398`。

## 四项正确契约

类：`DamengCurrentSchemaScriptFunctionalTests`；Category：`CurrentSchemaScripts`。使用普通 `[Fact]`，环境缺失/权限不足真实失败，不以 DamengFact 缺环境 skip 冒充验收。

1. **GeneratedCreateScriptAppliesSupportedCurrentSchemaModel**：原模型 identity(10,2)、sequence(41,3)、虚拟 UPPER 列、VARBINARY、主/唯一/检查/外键、级联删除及 descending index；原 Unicode/撇号种子值、identity 22 和 sequence 41/44；GenerateCreateScript 不写历史表。
2. **NonIdempotentGeneratedUpAndDownScriptsPreserveHistoryAndSeed**：原 create + alter 操作完整生成与执行；列/索引/表重命名、STATUS 默认1、sequence 增量3→5；原 seed 与生成值43/48；完整 Down 后 table/sequence 消失、owned history 保留且行数0。
3. **IdempotentGeneratedUpAndDownScriptsApplyExactlyOnce**：同一 Up 生成脚本执行两次，种子总数与每个 MigrationId 均只有1行；同一 Down 幂等脚本再执行两次，owned objects 不重复创建/删除，history 行数0。
4. **GeneratedMigrationRangesUpgradeAndDowngradeCurrentSchema**：0→create→alter→create→0 分段脚本，逐阶段以新连接核实 seed、history、ProductVersion=10.0.12、列/索引与对象状态。Down 是新增测试契约，原 fixture 的 Down 原本明确未覆盖。

迁移 ID 修复：原参考 fixture 使用12位前缀，但 EF 10.0.12 的 MigrationsIdGenerator 要求 yyyyMMddHHmmss（14位）用于 named from/to 解析。本补丁仅将新 migration const/attributes/history预期统一为14位，不改原 admin fixture 或生产 generator。依据 [EF10.0.12 官方实现](https://raw.githubusercontent.com/dotnet/efcore/v10.0.12/src/EFCore.Relational/Migrations/Internal/MigrationsIdGenerator.cs)。

模型/migration types 均使用新的 CurrentSchema 名称和独立 DbContext，migration IDs 为 `20260930000100_CurrentSchemaCreate`、`20260930000200_CurrentSchemaAlter`，不会接入旧 admin DbContext 或复用其 static prefix。

种子/转义向量仅来自原 fixture：`种子`、`O'Brien`、`新行`、`dameng`、`[0,0xA5,0xFF]` 与已有 identity/sequence 数值。没有新增合成 Unicode、quote、semicolon 或敏感 sentinel 数据集。

## 当前 TEST schema 与清理

- 每次 Open 都校验 `USER` 和 `SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID())` 均为 `WDM_PROVIDER_TEST`。
- 每项测试生成随机 `T12S_<GUID>` 前缀；对 exact table/sequence/index/constraint/history 名先查当前用户目录，全部不存在后才登记拥有权。发生碰撞不会授权清理该对象。
- DDL 不包事务，不声称可回滚；复用原 executor 的普通语句拆分和 DMSQL 块拆分。`BEGIN...END;` 整块发送，单独 `/` 客户端行不入 CommandText；代码检查不生成 CREATE/DROP SCHEMA、USER、TABLESPACE 或 BEGIN TRANSACTION。
- 每项测试 finally 先清理本次 child/lookup/parent/history 表及序列，随后用新连接验证 exact owned table/sequence/index/constraint 全部不存在。未用 EnsureDeleted、通配清理或固定 `__EFMigrationsHistory`。
- collection fixture 的 Dispose 逐组重试所有尚未完成的已登记对象；每组失败独立累计安全类型/可用数字code，全部尝试后仍有失败则抛出摘要，不吞错当pass。管理账号、授权和实例设置不参与。
- xUnit 输出仅含本次 synthetic owned names 和 fresh cleanup 状态；异常为类型/数字code/安全方法行号摘要，无 inner exception/raw message。既有 executor 的 redactor 返回固定摘要，避免它输出整个 SQL + Exception.ToString。

Low 需运行 main 合并后的新 snapshot，用 `Category=CurrentSchemaScripts` 选择本类，并保存 test count/exit/TRX/零skip、实际命令、candidate DLL/package hash、身份以及 finally/fresh final-state 输出。不要运行旧 `DamengMigrationScriptFunctionalTests` admin collection。

## 明确未覆盖

原 admin 创建/删除 user、tablespace、grant、物理文件路径、新 schema 和跨 schema lookup 场景不执行。新的模型/迁移仅移除这些管理边界（EnsureSchema/new-schema table mapping）；其余原对象 SQL/seed 契约沿用，provider 的生产生成器不改。

静态 analyzer 修复：固定摘要 Redact 与无实例依赖的 ExecuteScriptAsync 使用 static，方法 group 引用类型，不抑制 CA1822 或放宽断言。旧 integrated-v3 snapshot 未改，下一 fresh snapshot 待 Low 构建验收。

本脚本 lane 不替代 Database.Migrate/DBMS_LOCK、CLI migration/scaffold、reverse engineering 或所有上游 migration conformance；这些由 T12 各自 gate 验证。原 descending catalog 辅助断言只有在服务器目录提供 DESCEND 列时才核该目录字段，脚本 DESC 形态和实际 index 存在性始终断言；任何目录权限错误仍真实失败。

依据 archive 的 `skills/dameng-sql/SKILL.md`、`references/dmsql.md` 和 `skills/dameng-ef-migrations/SKILL.md`。其中管理员初始化示例未执行。本补丁不提交或发布包；应用和验收由 main/Low 统一安排。
