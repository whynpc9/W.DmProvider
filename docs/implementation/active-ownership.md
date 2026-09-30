# 当前状态：R1 已验收，停止后续实施

用户最新指令：R1 完成后停止推进，root 提交推送并将现有 PR #1 改为 R1；T13 不启动。

T01–T12 已验收；T11 最终 443 项输入与二进制已保存 `.local/t11/accepted-source/` 和 `accepted-binaries/`，旧证据不回写。用户指定 GPT-6.1 Sol High 编码、Sol Low 实际测试；root 负责设计和 review，Luna Max 仍只在明确需要合成向量时派发。

| agent | 当前角色 | 唯一写入范围 |
| --- | --- | --- |
| root | 设计、review、证据/进度 | docs/implementation、兼容性和根入口文档 |
| downstream_prep_61（High） | T12 主接入/runner | tools/DownstreamAcceptance/t12*、自身 bridge patch、T12 README；新的隔离 EF archive |
| codec_61 / harness_61（High） | CLI 补丁已冻结 | codec 初版安全接入；harness 最终固定 host 与设计时模型检查 |
| transaction_61（High） | 当前 Schema 脚本 fixture | 自己的 .local/t12/script-work 下新增测试/helper；T12/patches/current-schema-scripts.patch 与说明 |
| binding_61（High） | 产品保留 owner | 暂无新写入；具体失败由 root 定点派发 |
| harness_61（High） | R1 离线 CI / release guard | .github/workflows/r1-offline.yml、eng/t12-offline.sh、eng/T12.md、必要 tools/R1ReleaseAudit/；T11 全部冻结 |
| verify_61（Low） | 唯一 build/test/DB 执行 | ignored 验证输出/快照；不修改实现或弱化测试断言 |

## 规则

- 方案见 [T12-design.md](T12-design.md)。原 EF 工作区和所有历史 archive/evidence 只读；在 clean pinned archive 先应用各自 patch，再统一 W bridge。具体路径/接口先与 main downstream agent 对齐，禁止交叉修改。
- High 不执行 dotnet/DB；阶段初段 ready 可单独新快照进入 Low 窗口，后续 patch 在另一新副本整合，不能修改冻结输入。新功能缺陷修复必须先释放实际验证窗口，再重新冻结新源/包；旧失败不算通过。
- Low 控制单一构建/数据库窗口，全部 dotnet 设置可写 cli-home 与单节点/noReuse/noSharedCompilation/disable-build-servers。无显式 TEST 时 release gate 失败，不把skip算pass。
- 仅TEST wrapper/唯一对象/精确清理，新连接验证身份和最终状态。无 SA、无原EF all/admin、无跨Schema DDL/DML、无全Schema清理。秘密不进入patch/源码/配置/日志/制品。权限不足如实记录，不静默提权。
- 每次 candidate 版本唯一，manifest/package内/实际加载DLL hash一致。原官方特征与W正确契约分别分类。发行不在范围；用户已授权R1通过后由root提交/推送和更新现有PR，工作agent不得自行执行。
- 已结束的agent通过 followup_task 启动新任务，send_message 不会唤醒已结束任务。T13尚未激活，其 .local 准备文档不代表实现或通过。

## R1 收口

最终 CLI 已独立通过，所有运行输入冻结；root 仅完成证据/文档和 Git/PR 收口。没有新的编码任务，T13 不启动。后续实施必须由用户再次指示。
