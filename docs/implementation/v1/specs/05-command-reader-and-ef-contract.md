# S05 — 命令、Reader、多结果和 EF 保存契约

状态：实施规范。优先级：P0/P1。依赖：S03、S04基础。任务：T08。

## 1. 首要范围

CommandType.Text：参数化 SELECT、INSERT、UPDATE、DELETE；现有 EF 的 DML+SELECT 组合；Prepare 在受支持语句上不改变语义。StoredProcedure/输出参数/RefCursor 若未通过单独测试，明确拒绝，不能靠静默 text 转換假装支持。

第一版必须满足 [R06] 中的生成 SQL，不要求通用 DbBatch。`SingularModificationCommandBatch` 限制修改命令数，不代表只有一条 SQL。

```sql
UPDATE "APP"."Widgets" SET "Name" = :p0
WHERE "Id" = :p1 AND "Version" = :p2;
/*EFCOREROWCOUNT*/SELECT SQL%ROWCOUNT;
```

```sql
INSERT INTO "APP"."Widgets" ("Name") VALUES (:p0);
SELECT "Id", "Stamp" FROM "APP"."Widgets"
WHERE SQL%ROWCOUNT = 1 AND "Id" = SCOPE_IDENTITY();
```

序列键路径相应使用限定序列名 `.CURRVAL`。这些是需要保留的下游契约示例，不作为随意 SQL 拼接模板。

## 2. 执行模型

内部概念 `DmCommandPlan`：固定 SQL、参数 metadata snapshot、事务引用、timeout snapshot；Execute 开始后参数集合和 CommandText 不得并发更改。不能为了支持操作而把用户命令上的 CommandText 临时改成内部 SQL。

`DmResultCursor` 对接已验证 wire 响应，维护：当前 rowset metadata、当前行、是否有后续结果、受影响行计数、输出值状态、底层 statement 所有权。

强制先为现有 reader 对公开方法的行为做 characterization。不要凭报告对 magic comment 的简写重新定义 NextResult：源码检查条件为 `!EFCoreNextResult`，默认开关与注释计数影响必须分场景验证。[R10]

## 3. SQL 处理边界

保留已证明的 SQL 解析/协议执行路径，**不得使用 Split(';')** 实现复合命令。字符串、注释、引用标识符、DMSQL 块中的分号不是独立命令。

参数识别使用 tokenizer，不替换字符串/注释中的 `:p`、`@p` 或 `?`。第一版对选定复合 SQL 有测试不等于承诺任意 DMSQL/任意多语句。若整体执行依赖服务端支持，保留该前提，不用客户端偷偷改事务边界实现。

不允许为了“校验连接活着”在 DML 与 `SQL%ROWCOUNT` 或 identity 回读之间插入 SELECT 1、schema 查询或其他业务 SQL。这些辅助语句可能改变待消费会话值。整个组合共享同一 execution lease。

## 4. Reader 契约

- `FieldCount/GetName/GetFieldType/GetDataTypeName` 描述当前 rowset；`GetValue` 的实际类型必须与所声明 getter 契约一致，DBNull 单独处理。
- ExecuteReader 成功返回时定位到该命令首个可读 rowset。DML 的计数是计数，不能制造有一行空数据的 rowset。
- `Read` 只移动当前结果的行；无行返回 false，不自动跳下个 rowset。
- `NextResult` 按协议顺序移动到下一可读 rowset；空 rowset 也必须保留。只有 update count 的结果如何跳过/累计，要与现有 EF 消费方式一致。
- `RecordsAffected` 非 DML为 -1；未知计数为未知，不伪造 0。实际累计规则与溢出行为必须文档化；总计数不能代替 EF 逐命令并发检查结果。
- 已关闭 reader 的读取与 stream 操作报明确关闭错误。Close/Dispose 幂等。
- `ExecuteScalar` 返回第一可读 rowset 第一行第一列：无行为 null，SQL NULL 为 DBNull.Value；其余结果需安全完成/关闭。
- `ExecuteNonQuery` 不忽略后续服务器错误；只有整个命令完成且状态可知才返回受影响数。

Reader 早关：只消费/终止已经启动的协议执行。不能为了“清空”而执行尚未发送的后续有副作用 SQL。无法有限时间同步协议时销毁连接。不允许仍有悬挂结果就归还池。

输出参数属于后续明确支持范围，若开放必须说明何时可见（通常读完/关闭后），不得留旧执行值。

## 5. CommandBehavior

逐位判断 `(behavior & flag) != 0`，不能用 `behavior == flag` 判组合。Default=0 特别处理，不按 HasFlag(Default) 判业务功能。[R02]

支持范围：Default、CloseConnection、SingleResult、SingleRow、SequentialAccess；SchemaOnly/KeyInfo 要用已验证 describe/metadata 路径，无法保证时在发送前明确拒绝而不执行有副作用 SQL。

`SingleRow/SingleResult` 只约束可见结果，不赋予跳过事务清理的权利；关闭仍遵循会话恢复规则。SequentialAccess 的完整流式语义到 S10 后才声明；R1 即使修位标志，也必须诚实注明尚未实现 LOB 流式，不伪造 memory bound。

`GetSchemaTable` 不递归在持有 reader 的 public connection 上启动第二命令。已有元数据足够则本地生成；缺失的键/基表信息标 unknown/DBNull，不伪造唯一键。无法满足请求的 KeyInfo 前置明确报错；不得偷偷另开连接改变事务视图。

## 6. EF 交接契约（ef-savechanges-v1）

必测：UPDATE/DELETE 影响 0/1 行；INSERT 标识键和序列键；读回多个生成列；组合 SQL 中第二语句失败；空 SELECT；字符串/注释内的分号；重复执行 command；用户事务中的相同路径。

`/*EFCOREROWCOUNT*/` 暂作兼容输入保留，但不建议新增依赖它的设计。未来移除需单独下游变更，首版不能自行删注释或用新的返回约定代替现有 EF 协议。

S07 的隔离问题调查以此执行层为重点之一：单 UPDATE ExecuteNonQuery 对照 UPDATE+SELECT ExecuteReader，再对照 EF 保存点。不能预先宣布根因已定位。

## 7. 验收

CMD-01：UPDATE/DELETE 各 0/1 行，返回与数据库最终状态一致。
CMD-02：identity/sequence 与非键生成列准确读回；无额外 probe 污染 SQL%ROWCOUNT。
CMD-03：多 rowset、空 rowset、DML count 混合的 Read/NextResult 顺序正确。
CMD-04：复合 SQL 中后续错误不能丢失；失败后会话处于可证明状态。
CMD-05：复合标志至少验证 SingleRow|CloseConnection、SequentialAccess|CloseConnection、SchemaOnly|KeyInfo（支持则正确，不支持则明确早拒绝）。
CMD-06：早关 reader 不泄漏句柄，不执行额外尚未发送的写入；关闭后下一命令正常或明确需重开。
CMD-07：CommandText 在 Prepare/事务/重复执行后未被内部清理置空。
CMD-08：EF 官方基线的正确功能在 W 候选包上通过；官方缺陷断言另作 characterization。
