# 本地达梦开发与测试环境

状态：2026-09-29 已在用户提供的测试实例上创建并验证。本页不含口令或完整连接串。

## 项目隔离单元

| 用途 | 数据库账号 / 默认 Schema | 本地 secret | 允许的用途 |
|---|---|---|---|
| 自动化集成测试 | `WDM_PROVIDER_TEST` | `.local/secrets/dameng-test.env` | 可重复的真实库验证；默认连接 |
| 手工开发探测 | `WDM_PROVIDER_DEV` | `.local/secrets/dameng-dev.env` | 临时 SQL、协议和兼容性探测 |
| 维护 | 用户提供的 SA | `.local/secrets/dameng-admin.env` | 仅用于专用用户的创建及明确的授权维护 |

达梦在单个现有实例上以用户和 Schema 隔离对象；创建用户会建立同名默认 Schema。这里的两个“项目测试库”是独立的逻辑工作空间，并非两个新的物理数据库实例或表空间。[达梦用户与模式说明](https://eco.dameng.com/document/dm/zh-cn/pm/management-pattern.html)

两个项目用户的直接系统权限为 `CREATE SESSION`、`CREATE TABLE`、`CREATE INDEX`、`CREATE VIEW`、`CREATE SEQUENCE`、`CREATE PROCEDURE`、`CREATE TRIGGER`。2026-09-30 经用户单独批准，仅 TEST 增加 `SOI` 角色，用于 EF 迁移所需的非审计/安全系统目录读取；TEST 新连接确认 `ADMIN_OPTION=N`、目录查询及迁移锁可用。DEV 未变。不授予 DBA、`ANY` 权限或其他业务 Schema 的对象权限。限定维护与原失败后置检查记录见 [T12 catalog access](maintenance/T12-test-catalog-access.md)。需要扩展到 FLDR、HA、分布式事务等高级测试时先核对其单独权限要求，不静默提升账号权限。

## 加载连接

自动化测试只读取 `DAMENG_TEST_CONNECTION_STRING`。从仓库根目录运行：

```bash
scripts/with-dameng-test.sh <测试命令及参数>
```

脚本加载本机 secret 后执行命令，不输出连接串。测试项目尚未建立时，可先用它执行独立的探测程序；不要把连接串作为命令行参数、写入项目配置或提交到 Git。手工开发探测需要在受控 shell 中显式 `source .local/secrets/dameng-dev.env`，其变量名为 `DAMENG_DEV_CONNECTION_STRING`。SA secret 不应由普通开发命令加载。

本机 `.local/` 已加入 `.gitignore`。secret 目录权限为 `0700`，三个 env 文件为 `0600`。切换主机或用户时，由环境所有者以安全渠道重新提供凭据，不从 Git 恢复。CI 应使用自己的受保护 secret 和专用测试实例。

从 T04 起，新 W 的默认 `RequireTls` 会在已验证 TLS 传输完成前拒绝连接。已授权的本地真实库检查使用 `eng/t04.sh real`，由探针读取原 secret 后通过 typed W Builder 显式选择 `PlaintextAllowed` 并设置测试 Schema；不改共用 secret，不影响官方 O / 恢复 R 的解析。该选择不允许跳过证书校验；服务器要求未实现的 TLS 或原生加密模式时仍拒绝。此测试不证明 TLS 模式可用。

## 已完成的实例验证

- 使用仓库内固定的官方 `DM.DmProvider 8.3.1.47463` net9.0 资产，在 .NET 10 临时探测程序中连接 SA，确认可创建用户，且创建前没有 `WDM%` 项目用户。
- 创建 `WDM_PROVIDER_TEST` 和 `WDM_PROVIDER_DEV`，授予上述权限。
- 分别用两个新账号登录，在各自默认 Schema 中执行建表、插入、查询和删表，结果均成功；探测表已清理。
- 二次核对两个账号的直接系统权限恰为上述 7 项，两个账号的探测表均不存在；从测试账号读取开发账号的探测表被服务器拒绝，该表随后已清理。
- 未验证 TLS 模式、故障/取消、批处理或新驱动行为；当前结果只说明官方驱动对这两个账号的基本连接与 CRUD 可用。

后续实际测试仍须按 [AGENTS.md](../../AGENTS.md) 先检查登录身份、只清理本次对象并记录最终数据库状态。新驱动的集成测试与官方驱动表征测试须分开报告。
