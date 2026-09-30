# T11 — 隔离级别与 SaveChanges 失败调查

状态：实施中。T09/T10 已独立验收并保存各自历史快照。以 S07/TX-06 和 CMD-07 为主；不能将 Begin 成功或旧缺陷断言通过当作隔离语义/正确性通过。

## 输入与边界

- O：固定官方 DM.DmProvider 8.3.1.47463 net9.0。
- R：已恢复基线 DLL，SHA-256 `1bbedef16e8720227fd416421e744b7894df79082f3ddd7d5a9537c1d424fba3`，单独进程，不能修改原恢复树。
- W：T10 历史 DLL `51043edb57710cb5c4aa7e13a514e1672e4783150babb9af3ddd0ed1b329323b` 为只读对照；当前候选另标识。
- EF：从 clean commit `113014cc74dd1f751ef97226a78d2ec855b32c8c` 导出隔离 archive，原仓库只读。SDK 10.0.401 用现有 `/Users/wanghongyi/.dotnet/dotnet`，主驱动仍遵守自己的 SDK pin。
- 仅 TEST wrapper，唯一表/序列、finally 清理和新连接确认。不得运行 EF launcher 的 all/admin 或复制其秘密文件；不改共享实例配置。

## 已验证的缺陷机制与待验收修复

原实现中 SetTransactionIsolation 以空 DmCommand 创建控制 statement 并保存到 transaction.Stmt；用户 Command.Transaction setter 又会采用它。固定 O/R 的最小复现已经确认：RU/Serializable 的参数化和非参数化 UPDATE+ROWCOUNT 均在 GetCommandText → do_NextResult → NextResult 失败，owner SQL 长度为 0。相同二进制只清除新用户命令继承的 statement 引用后，两次执行和独立连接回滚状态均正确。RC 对照成功。证据：`.local/verification/t11/20260930T030043Z-baseline.json`，原始矩阵 `.local/t11/runs/20260930T030043Z-94295-20057`。

固定 EF archive 的官方驱动 24 项保存场景进一步复现：RC 8 项成功，RU/Serializable 各 8 项失败，覆盖 INSERT/UPDATE、自动保存点开关和 Commit/Rollback，均完成清理。此结果是 OfficialCharacterization，不是候选验收。证据：`.local/verification/t11/20260930T030443Z-ef-O.json`。候选独立 statement/owner 与有界关闭修复已编码，等待 ADO、并发 litmus 和实际包消费验证。

O/R/W 对每次命令记录用户 SQL 长度/哈希、参数数目、是否继承事务 statement、statement owner 是否为当前命令、owner SQL 长度/哈希、准备状态及安全异常类型/方法栈。不得输出 SQL 正文、真实值、连接串或原始异常消息。

可在明确标记的隔离 causal probe 中，仅清除新用户命令继承的 statement 引用作单变量干预，并与相同二进制的未干预路径比较；这不是发布实现，也不改变只读 DLL。若假设成立，生产实现使用独立、SQL 明确且有界释放的隔离控制 statement，禁止把它借给用户命令。

## 实施中发现的第二个确认边界

round4 数值探针（`.local/verification/t11/candidate-round4-inputs/focused.json`）确认：RU/Serializable 的 SET 返回 req5、response187、SQL code 0、body length 4，decoded statement type 为 150，返回隔离值分别为 0/3。此时连接默认隔离缓存仍为 RC。既有 decoder 对 type150 处理事务标记，对 type166 才更新会话默认值；不能把二者等同。

候选 Begin 原新增检查把 `ConnProperty.IsolationLevel == requested` 当作配置确认，因而在合法 SET 后误失败。批准修复为：只从本次隔离控制 statement 的已验证响应取得事务级隔离 receipt，要求 type150 和请求值严格匹配，再设置 manual-commit 并有界关闭控制 handle，最后激活事务。不通过改写连接默认缓存来迎合检查。缺失、类型不符、未知或不匹配 receipt 必须失败并关闭捕获会话。公开 RU/Serializable 门仍等待完整验收。

RC 实库矩阵也发现 EF 的 `ROLLBACK TO "name"` 被旧守卫拒绝。现已用完整语句的窄词法识别接受 `ROLLBACK TO [SAVEPOINT] <single identifier>`；支持双引号转义及注释。包含额外语句的保存点控制仍拒绝，SQL 原文不改写。round3 已通过 29 项离线用例及 RC 12 项矩阵；这不代表完整 T11 通过。

## ADO 与服务器语义矩阵

同一 TEST profile 比较 RC/RU/Serializable：参数化单 UPDATE、UPDATE+SQL%ROWCOUNT、INSERT+identity/sequence 回读、保存点前后复合命令、重复 Prepare/执行、不同结束路径。成功必须核影响值和独立连接最终状态；失败必须核命令文本及事务/连接状态。不插入影响 ROWCOUNT 的辅助 SQL。

另用两连接、barrier 和有界命令期限验证未提交可见性、已提交再读及 Serializable 并发冲突/阻塞，记录真实观察，不把一种具体实现策略当全部合法语义。固定 sleep 不能充当同步证据。每个案例独立最终读回，不能只断言异常。

W 当前公开入口只支持 RC。需要执行未开放级别时，可以新增明确 internal、单连接/单调用的 profile 探测入口，复用同一个事务核心；不得新增公共配置或修改全局默认来偷开能力。只有 ADO、服务器语义和 EF 保存链均通过后，才对已验证 profile 开放公开 RU/Serializable，再以正常公共入口复验；失败则保留明确禁用及证据。

## EF 路径

在隔离 archive 上做最小 W 包接入，保持 EF 10.0.12、保存 SQL、单命令修改批次和类型映射。分别记录官方 characterization 与候选正确契约。验证 tracked UPDATE/INSERT SaveChanges、自动保存点开/关、用户显式事务和最后数据库状态。EF 使用原始 SAVEPOINT/ROLLBACK TO/RELEASE SQL，不等于已测的 ADO Save API，须单独通过。

候选包以唯一版本、源清单及包内/加载 DLL SHA 固定；full EF suite 和最终 R1 包交接仍属 T12。不得为绕过失败直接禁用保存点或改写 EF 业务 SQL。

## 分工

root 设计与 review；binding_61 负责 Command 所有权相关定点修复；transaction_61 负责 isolation 核心、受控 profile 入口和能力门；harness_61 编写 IsolationTests/IsolationProbe/eng T11 及服务器 litmus；downstream_prep_61 编写隔离 EF archive/包消费与最小 EF 验收；verify_61（Low）独占实际构建/数据库窗口。编码可在互不重叠文件并行，构建前全部涉及 owner 停写。原始规范/历史证据不修改。
