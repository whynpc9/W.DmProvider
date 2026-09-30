# T08 — 命令、Reader 和 EF 保存契约

状态：实施中。T07 已独立验收并按其 evidence 保存到 `.local/t07/accepted-source/`；后续源码变更不覆盖该历史证据。

## 范围与顺序

以 S05/CMD-01–08 为准。先记录固定官方 O 与已验收 T07 W 的真实响应行为，再修改候选 W；候选验证必须使用当前源码/实际包，不能拿旧 W 结果替代。保留整句 SQL/tokenizer，不使用分号 Split，也不在 DML 与 ROWCOUNT/identity 回读之间插入辅助 SQL。

先支持 CommandType.Text 的选定 CRUD、复合保存 SQL 和 Prepare。未单独验证的 StoredProcedure、输出参数、RefCursor 明确拒绝。SchemaOnly/KeyInfo 在没有可靠 describe 路径前，在任何发送和副作用之前拒绝；所有 flags 逐位判断，Default=0 单独处理。

## 执行计划与并发

`DmCommandPlan` 冻结用户 SQL、有效事务引用、参数 metadata/值和 timeout。Command/Parameter/Collection 的可变入口使用统一命令状态门；Reader 仍占有执行时不开放这些字段的并发修改。参数不能被另一 collection 接管而绕过冻结，Clone 不共享参数父关系。

已支持的 byte[]/char[] 等可变内存值按需要复制，明确其他对象的所有权边界；不能调用任意 ICloneable/IConvertible 用户代码于状态锁内。状态锁只捕获/更新内存状态，网络和用户回调在锁外。Prepare 和内部清理不能清空或临时替换用户 CommandText。

Command gate 与 T05 物理 session gate 是不同层：前者保证逻辑命令稳定，后者防止同一连接协议交织。不得用其中一个冒充另一个。内部 metadata/初始化的 borrowed lease 也保留显式 owned 路径。

## 结果游标

`DmResultCursor` 对接已验证的服务端响应，区分 rowset、DML count 和终结。terminal 必须由源码及真实表征证明，不猜一个 ret type 或吞掉负 sqlCode。

- ExecuteReader 返回首个可读 rowset；跳过/累计 update count，不伪造数据行。空 rowset 仍是结果。
- NextResult 更新完整 metadata/getter/cache/游标/计数状态，避免第二结果使用第一结果的类型或 LOB codec。
- RecordsAffected 只累计 DML，未知保持未知；checked int 溢出策略明确记录，不能把总计数代替 EF 逐命令并发检查结果。
- ExecuteScalar 取第一可读 rowset 的首行首列；该 rowset 为空时返回 null，不跳到后面的非空 rowset。SQL NULL 返回 DBNull。其他结果安全完成/终止，不隐藏后续错误。
- ExecuteNonQuery 只在整个命令完成、后续错误已处理后返回。
- Reader 早关只清理已启动协议执行；不得为了 drain 执行尚未发送的后续有副作用 SQL。无法证明会话同步时断开捕获的物理会话。

## 元数据边界调整

S05 要求 Reader.GetSchemaTable 不额外执行业务/目录 SQL。T05 的 owned 目录查询虽已验证所有权，仍会影响会话 SQL 值；T08 改为只使用当前响应已有 metadata，未知 IsKey/IsUnique 等为 DBNull。KeyInfo 不支持时明确早拒绝。T05 探针中依赖查询填出 IsKey=true 的断言应按此新契约更新，并增加“GetSchemaTable 零额外 wire”的证明；历史 T05 结果不回写。

## 分工与验收

| lane | 文件/职责 |
| --- | --- |
| 主 agent | 设计、跨层 review、进度和验收证据 |
| Sol High frames | Command、Reader、ResultCursor 和结果生命周期 |
| Sol High transport | CommandPlan、Parameter/Collection 冻结和复制 |
| Sol High session（复用） | statement/B/c/DmInfo/result cache 的协议表征及定点适配 |
| Sol High tests | CommandTests、CommandProbe、O/W 固定表征、候选 nupkg 消费、eng/t08 |
| Luna Max | 逻辑结果序列与 SQL/flags 合成数据 |
| Sol Low | 冻结后独立验证 CMD-01–08 和必要回归 |

实库使用 TEST wrapper，唯一对象、失败 finally 清理及独立读回。覆盖 UPDATE/DELETE 0/1、identity/sequence、多生成列、空结果与混合类型/LOB、后续错误、分号和参数名在引用/注释中、用户事务、重复 Prepare/Execute、flags、早关和并发 mutation。完成后构建真实候选 nupkg 并独立消费选定保存契约；完整下游 EF 交接仍在 T12。构建和数据库窗口由测试 lane 统一安排。
