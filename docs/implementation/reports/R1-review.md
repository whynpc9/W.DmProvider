# R1 PR #1 review 修复

状态：integration_verified；当前批次修复已独立验收，PR 复审与合并另行跟进。基线提交 `ce1ded35347491793fc84cac51541955f0219f6c`，用户要求处理当前 review；本轮只修 R1 既有行为，T13 未启动。旧 T12 包、源码及证据已按哈希保存于 `.local/verification/r1-review/historical-baseline/`，历史结果不回写。

## 评论与行为

| 评论 | 问题 | 修复契约 |
| --- | --- | --- |
| Cursor 两条词法意见 | 两处嵌套块注释扫描可隐藏后续 COMMIT 或参数 | R1 非嵌套词法在首个 `*/` 截止；指定 compound rollback 拒绝，普通注释/引号继续正确识别 |
| Codex P1 | GetInt32 对 SQL NULL 返回 0 | 与其他整数 getter 使用同一 NULL 转换错误；真实 0 保持 0，GetFieldValue<int> 同样覆盖 |
| Codex P2 | CLOB null destination 长度探测抛异常 | 返回完整已解码 UTF-16 字符长度；长度探测不消耗顺序读取位置，超物化范围明确拒绝 |
| Codex P2 | Connection=null 未解绑旧连接 | 清理本命令的 prepared handle 和连接状态；活动执行/Reader/显式事务及原会话忙时拒绝变化，保留原 owner |

连接切换不得释放另一命令或事务的 statement。调用方可以先清 command.Transaction 再解绑，事务对象仍由原连接管理。准备与重绑必须在新连接创建新的 server handle。

## 分工及验收

Sol High 分别拥有词法、Reader、连接解绑与对应测试代码；Luna Max 的逻辑数据保留 not_run 标记，不能代替实际执行。Sol Low 独占构建与实库窗口，root 负责设计和 code review。

拟执行旧实际包的安全复现与新唯一候选包回归。旧包不运行可提交事务的复合 COMMIT payload；当前服务器注释语义用只读 SELECT 区分。新候选验证事务守卫拒绝后仍可回滚，并以独立连接确认本次数据及对象已清理。所有实库运行只用 TEST wrapper、随机对象和显式 PlaintextAllowed，不加载管理员 secret，不改变实例设置。

驱动离线、包/API/来源审计和固定 EF 下游候选验收完成后，写入新的独立证据并更新本报告；未执行项不得写成通过。产品 README 已改为指向当前 R1 和 review 证据入口，不沿用旧包的历史完成描述。

用户追加授权：当前修复提交后每 15 分钟检查 PR 后续 review，继续处理并在同一最新 head 的审查和必要检查全部通过后合并。合并成功后停止定期检查；仍不启动 T13。

## 旧包实际复现

旧 `0.1.0-r1.20260930064454` 的实际 PackageReference probe 已执行，DLL SHA 与 T12 一致：NULL Int32 正确契约失败、CLOB null-buffer 长度探测抛 ArgumentNullException、prepared 命令解绑失败。`baseline_characterized` 的退出 0 仅说明表征完成，不把这些失败计为正确性通过。安全注释 SELECT 返回 42，确认当前 8.1.5.60 profile 的首 close 语义；没有向旧包发送包含 COMMIT 的复合 payload。TEST 身份通过，独立新连接确认本次表数量 0。原始报告 `.local/verification/r1-review/baseline-probe/report.json`。

## 新候选与独立回归

候选 `0.1.0-r1.20260930151754`；nupkg SHA `f640946324d2ee5e6c5f9929049c534132bac87023ffbe447f5f1cefbdecee46`，加载 DLL SHA `ddd54affb9e13d2c18acb2cbc213ba6a48a223cd5082760833f99881be098e86`，MVID `56d3b648-53a8-4003-8cee-9dd92da5a17a`。278 项来源 SHA `c7ab53176d5170786b91883b22d29ee7ebc37da4007f8bb6e6ea0763525dbdd7`，公开 API SHA 保持 `f914653da6908a767a3760e6b7470cefeffdbfc62b539e48c7689a2b30cb4bbd`。SourceLink 未启用；本包以构建时源清单追溯，交付提交在外部关联。

| 实际执行 | 退出码 | 结果 |
| --- | ---: | --- |
| 新版本 Release 八套 offline | 0 | 326/326，零非通过计数 |
| 实际包身份、版本、API、来源/SBOM审计 | 0 | 通过；离线工具不冒充实库验收 |
| 新包 PackageReference fixed probe | 0 | 12 项实际检查通过，含非 NULL empty CLOB；最终表数量 0 |
| 固定 EF commit 113014 的 unit | 0 | 307/307 |
| 同副本普通 functional | 0 | 76/76，含迁移与 reverse engineering |
| 同副本当前 schema scripts | 0 | 4/4 |
| 同副本 specification 切片 | 0 | 4/4，不等于完整上游 conformance |
| 同副本 CLI | 0 | 1/1，完整 migrations/scaffold/model 链 |

[完整验证汇总](../evidence/R1-review/validation-summary.json) 含实际命令、全部 TRX 计数、加载 DLL、source/tool 哈希和实库前后状态；[fixed probe](../evidence/R1-review/fixed-probe.json) 记录客户端 NotSupportedException 拒绝后事务仍 Active、行仍存在，随后 RolledBack、独立最终行数 0。所有下游实库 lane 验证 TEST 身份和 0→0 的精确对象清单，历史 evidence 哈希不变。

首次审计用了包含临时文件的通用 source inventory，受严格哈希门拒绝；过滤后 278 项清单与构建候选一致。下游输出路径和未 build lane 的调用前置错误分别保留；没有将这些退出码写成产品测试通过。最终产品测试失败为零。

原 EF 工作区只读，新副本 `.local/t12/ef/20260930-r1-review-20260930151754` 仍固定同一基线。既有 CLI/当前 schema 补丁未改变，接入采用新的实际候选版本。当前库只读 SELECT 的 42 结果只证明已验证的非嵌套 profile，不概括其他达梦配置。R1 的 Async、池化及流式 LOB 边界保持原定义。
