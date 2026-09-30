# T12 TEST 系统目录查询前置

状态：用户于 2026-09-30 在本线程明确回复“批准该限定 SOI 授权”。限定 GRANT 已执行且 ACK；独立 TEST 只读核验角色/无 ADMIN OPTION、目录与迁移锁均完成。管理员窗口已关闭，没有重复授权。

## 实证

2026-09-30 使用 T11 已验收实际包进行 T12 audit，TEST 用户和当前 schema 均已确认，现有 EF `IHistoryRepository.ExistsAsync()` 对 `SYS.SYSOBJECTS` 的只读查询返回 -5504。没有到达迁移锁或迁移 DDL。独立 W 下游 unit 307/307 通过。

证据：`.local/t12/ef/20260930-core-v3/results-audit-20260930T045620647796Z/audit-permissions.json`。固定 EF 的技能和兼容文档记录：历史表存在性查询需要系统目录权限，该基线使用 SOI。

官方说明：SOI 具有非审计/安全系统表的查询权限，并非 DBA；它的范围比仅 SYS.SYSOBJECTS 单对象更广。参见[自主访问控制](https://eco.dameng.com/document/dm/zh-cn/pm/discretionary-access-control.html)。本实例对单系统表直接 GRANT 的可行性未验证，不能宣称某个更窄 GRANT 已证明可用。

## 拟批准操作

仅在明确批准后，以本机现有管理员 secret 建立一次限定维护连接，执行：

```sql
GRANT SOI TO "WDM_PROVIDER_TEST";
```

不加 ADMIN OPTION；不授予 DBA、ANY 或业务 Schema 对象权限；不改 DEV、其他用户、表空间或实例配置。该权限保留给本项目后续迁移开发验收。管理员连接立即关闭，普通测试继续只用 TEST wrapper；不复制 secret 到 CI/仓库，不记录原始异常消息或连接串。

随后以新 TEST 连接重新核身份、历史目录查询与原迁移锁，完整重跑相关验收。只有实测成功才能更新环境规则和 R1 状态；授权失败不自动改为更大权限。

如后续明确撤销该能力，可执行下面的单独维护操作（本次不执行）：

```sql
REVOKE SOI FROM "WDM_PROVIDER_TEST";
```

依据 AGENTS.md：普通开发/测试/agent 任务不得加载 SA，新增用户或变更授权需另行明确限定操作。因此本步骤独立于测试代码。用户已对上述明确范围授权，Low 仅能执行固定维护工具；普通测试仍不得读取管理员 secret。原七项 CREATE 权限与新增 SOI 角色将在实际读回成功后分别记录。

## 实际执行与独立复核

- `.local/verification/t12/20260930-SOI-limited-maintenance.json`：原命令退出1，原因是工具最初仅识别 `ADMIN_OPTION=NO`；记录保留，不改成退出0。实际 GrantAttempted/GrantAcknowledged/RolePresentAfter/AdminClosed 均为 true。
- `.local/verification/t12/SOI-test-inspect.json`：首个 TEST 只读检查使用了不支持的 schema 表达式，-2111；未触角色查询，连接关闭，失败保留。
- `.local/verification/t12/SOI-test-inspect-v2.json`：普通 TEST wrapper 新连接核对 USER/schema，ROLE 行数1，ADMIN_OPTION 为字符串 `N`，退出0并关闭 TEST 连接。这说明实际无转授权限，维护工具随后仅补识别已证的 N 表示，没有再连接管理员或重复 GRANT。
- `.local/t12/ef/20260930-r1-integrated-v1/results-audit-20260930T053351217153Z/audit-permissions.json`：实际 R1 包，TEST 身份/schema确认，随机不存在的 history 目录查询成功，原锁20260723取得与释放均确认。

最终权限状态由独立 TEST 读回验证；不把原维护程序的后置检查失败计为测试通过。普通开发规则不因此开放 SA 给测试；后续权限变更仍需单独明确限定。
