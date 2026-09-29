# 任务执行报告模板

Task ID：
源代码 commit（实施前/后）：
状态：not_started / in_progress / offline_verified / integration_verified / downstream_verified / blocked
本次范围：
不在本次范围：

## 行为变化

修改的文件与入口：
旧行为：
新契约：
关联规范和验收ID：
新增/变更公开API或配置：
是否修改来源快照（应为否）：

## 实际执行

| 命令 | 退出码 | 通过 | 失败 | 跳过 | 环境manifest | 报告路径 |
|---|---:|---:|---:|---:|---|---|

没有执行的命令及原因：
不得把“预期输出”放入实际执行表。

## 证据

真实driver asset sha256/MVID：
server build / profile：
.NET runtime / OS / RID：
Contract / Characterization / Improvement差异：
最终数据库状态验证：
取消/超时/失败后的connection/transaction状态：
内存/句柄/线程/往返指标（适用时）：
敏感数据检查：

## 审核与交接

已审阅安全/事务/协议/池化边界：
下游EF所需独立变更：
未解决问题与后续任务：
满足的release gate：
不得仅以“代码已生成”填写已验证。
