# T12 — R1 下游正确性与交接

状态：已执行并完成限定 R1 验收，实际结果见 [T12 报告](reports/T12.md)。用户最新指令：R1 完成后停止后续 DAG，随后由 root 提交、推送并将现有 PR #1 改为 R1 范围；不启动 T13。依赖 T07/T09/T10/T11 已验收；T11 最终 443 输入与包 DLL 已保存不可变快照。原始 v1 S12/QA-01–04/REL-01 是门槛，本文确定本轮实施范围。

## 输入与交付

- EF 固定 clean commit `113014cc74dd1f751ef97226a78d2ec855b32c8c`，archive SHA `9dead07c1de4220bd13bdadab0ed2713f43008af5e7c0cf7a629549a6739096d`。原 EF 工作区只读。所有下游补丁在新的隔离副本中生成，保留可应用 patch；不复制整套 EF 测试进驱动项目。
- 初始实际候选为 T11 public `0.1.0-t11.20260930041722`，包 SHA `76d824d134d334bd3f95489d73b7c8a275a96e6e4f34dc918e724523e5fb813e`，DLL SHA `c3269ce6b28576d932dd29e14a19a9af6dd3a02b5cf6df41e5df634e27321709`。需要修产品时每次冻结新源和唯一新包，绝不覆盖版本或回写历史证据。
- 驱动 SDK 10.0.203，EF SDK 10.0.401/EF Core 10.0.12；实际报告 SDK/依赖锁/加载 DLL 与包内哈希。生成 R1 handoff、支持矩阵、来源/包清单、回退说明和离线 CI/缺库拒绝发布检查。远程发行不在本任务。

## 下游接入范围

先应用分别审阅的有限 patch，再统一官方到 W 的 namespace/package/测试连接工厂切换。生产 SQL generator、单命令修改批次、影响行数和生成键读取、时间类型文本契约保持原行为；不能关闭自动保存点绕过失败。旧官方缺陷断言独立标 OfficialCharacterization；W 使用正确契约/Improvement。

覆盖 unit、普通 functional、现有四项 specification 切片、迁移和 reverse engineering、dotnet-ef CLI、当前 schema 的脚本/幂等执行。四项 specification 不代表全上游 conformance。设计时 capability probes 单独记观察，不因正常退出就算能力通过。

未知提交不得被 EF 重试策略或 additionalErrorNumbers 覆盖为可重试。未完成真异步、取消、池化、流式 LOB 等仍明确声明；R1 的 Async 调用兼容不证明 R2 异步 I/O。

## TEST 与迁移安全

仅 WDM_PROVIDER_TEST、唯一 wrapper 和本次随机对象。不得加载原 EF secrets/launcher all/admin。管理员建用户/表空间 fixture 与 CREATE/DROP SCHEMA probe 不运行；为普通脚本契约建立当前 TEST schema 的 owned fixture，记录原管理场景未覆盖。权限不足必须作为真实前置条件报告，不能静默授权或把失败改成 skip。

CLI 必须使用唯一 owned history 名，禁止清理固定 __EFMigrationsHistory；scaffold 使用 --no-onconfiguring，优先运行时 named environment connection；不把秘密写入生成源码/配置/异常/日志/制品。subprocess 的超时从启动开始生效，输出读取和等待受同一期限约束；超时终止自身进程树并记录安全摘要。nested build/restore 同样遵守 single-node/no-reuse/no-shared-compiler/关闭 build servers 和固定 host/feed/cache。

所有 migration DDL 默认抑制事务；不把 DDL 当可回滚。DMSQL 块完整发送，客户端分隔符不入 CommandText。精确对象清理后独立连接核实，不能用 EnsureDeleted 清理整个 schema。

## 分工与窗口

- root：设计、review、证据、进度和 R1 交付边界。
- downstream_prep_61（High）：T12 prepare/run、隔离 archive、主要 W 依赖/namespace/连接工厂/异常与测试分类补丁及最终 handoff patch；拥有 tools/DownstreamAcceptance/t12* 与自己声明的 bridge patch。
- codec_61（High）：只生成 CLI 安全/候选引用补丁，文件 scope 单一 DamengDotNetEfCliFunctionalTests.cs；独立工作副本与 cli patch。
- transaction_61（High）：当前 schema migration-script fixture 补丁与必要 helper，独立工作副本/单独 patch，不改旧 admin fixture 行为。
- harness_61（High）：R1 干净 checkout 离线 CI/命令、缺数据库 release gate 与候选包审计工具，不改产品或下游 runner。
- verify_61（Low）：唯一真实 build/test/DB 窗口；等待所有输入 ready，逐项 source freeze，完整 TRX/退出码/skip/最终状态/包 identities 核对。
- 其他产品 owner 待具体失败再指派修复；不得先猜测扩大产品实现。

补丁作者不运行 dotnet/DB。最终源码冻结后任何修复先告 Low 释放窗口，再重新冻结并产生新证据。T13 未激活，现有 .local/t13 准备材料仅供后续设计。
