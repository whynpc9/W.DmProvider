# T02 可编译 R 基线恢复设计

日期：2026-09-29。状态：已按本设计验收，见 [T02 报告](reports/T02.md)。前置 T01 已验收。来源：[S01](v1/specs/01-baseline-and-build.md)及 [T02](v1/tasks.json)。

## 恢复边界

R 是隔离的内部对照基线，保留官方 namespace/assembly identity 以运行相同调用；不作为产品包分发。W 的独立身份和包在 T03 建立。本轮不修上游行为缺陷。

保持 `packages/`、`decompiled/` 以及 T01 来源 manifest 只读。以确定的恢复工具和改动记录生成 `.local/t02/restored/`，避免将第二份可修改的大型源码树长期放入主线。项目名 `DM.DmProvider.Restored.csproj`，目标优先 net9.0，固定 SDK 10.0.203，`AssemblyName=DM.DmProvider`、`IsPackable=false`。生成路径与原始源码路径记录在 source-map 中。

原始 C# 的 `A.A` 等类型包含 CLR 允许而 C# 不允许的字段重名、返回类型重载及成员与类型同名。不得按文本出现顺序猜替换。允许在临时程序集副本中，依据元数据 token 规范化必要的内部名称，再对受影响源码进行机械生成；这属于解决编译障碍，不重做已完成的逆向调查。记录每个改名的 token、原类型/签名、新名称、理由和引用映射，检查方法体逻辑未被归一化工具改写。

额外 C# 编译修复必须有明确的原 IL 依据。不能删除出错方法、改成返回默认值、注释掉功能或修复已知缺陷来获得绿色构建。工具、依赖和语言版本锁定；保留资源 LogicalName 和原卫星资源来源，原生库不新增或分发。

## O/R 验证

O 与 R 使用同一套场景代码，分别编译到 net10.0 宿主、独立进程运行；输出与包缓存分开。O 引用固定官方 NuGet 包，R 仅引用恢复工程，不装载官方核心 DLL。必须输出并核对各自资产路径/哈希/MVID，防止测到同一官方二进制。

覆盖 S01 的 9 类场景：打开/关闭、参数 SELECT、Unicode CRUD、显式 Commit/Rollback、DML 影响行数、生成键回读、失败后下一条 SELECT、有限大小 LOB、已知官方缺陷表征。实例 SQL 仅限项目测试 Schema；使用随机对象名、finally 清理，并用独立连接核对事务最终状态与对象清理。配置和异常输出延续 T01 的脱敏要求。对已有错误行为比较 O/R 观察结果，不将它标为新驱动正确契约。

独立校验中性资源及 en/zh-CN/zh-HK/zh-TW 的可读取性，并记录公共 API 差异。源 DLL 与恢复 DLL 的 MVID/哈希通常不同；静态元数据对比不能代替运行差分。任何 O/R 差异都需要解释或修正后才能通过。

## 分工

- 主 agent：恢复设计、疑难修复与差异审阅、最终验收报告/进度。
- Sol High / restoration：`tools/RestoreBaseline/`、来源映射和 API/恢复记录；生成树仅放 `.local/t02/restored/`。生成阶段不连接实例；定位恢复差异时通过独立宿主使用项目测试账号，保持诊断脱敏。
- Sol High / harness：`tools/BaselineComparison/`、`eng/t02.sh`、`eng/T02.md`；使用同一场景源码建立 O/R 独立宿主、比较器与资源/API检查。不得修改恢复工具和 fixture。
- Luna Max：`tests/fixtures/restoration/` 的短 Unicode CRUD 数据、LOB 确定生成描述及独立 hash，不连接数据库。
- Sol Low：独立构建/验证、全场景差分、异常与清理证据；仅写 `.local/verification/t02/`，缺陷交编码 agent 修复。

## 完成条件

从只读来源用锁定工具可重复生成并构建 R；源映射覆盖全部机械改名及编译修复；原始快照与 v1 原件不变。独立验证 O/R 9 类场景与声明的资源范围，原始观察与差分结果分别保存，产物无秘密。实际进度独立记录；BAS-02 和产品 pack/发布门槛留到 T03及后续，T02 不创建 W 生产包。
