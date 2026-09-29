# T03 独立产品身份与候选包设计

日期：2026-09-29。状态：已按本设计验收，见 [T03 报告](reports/T03.md)。T02 已验收。来源：[T03](v1/tasks.json)、[S01](v1/specs/01-baseline-and-build.md)、[S02](v1/specs/02-api-and-configuration.md)。

## 本轮契约

- 创建 `src/W.DmProvider/W.DmProvider.csproj`，`TargetFramework=net10.0`，`AssemblyName=PackageId=W.DmProvider`，主公开 namespace `W.Dm`。这是 T03 内部开发候选包，不是 R1 完整预览版。
- 将 T02 已恢复的 C# 输入语法级迁入产品源码树，保留 Legacy 结构，原始 `packages/`、`decompiled/`、T01/T02工具与证据只读。排除 R 的 bin/obj、旧项目及 AssemblyInfo、参考 DLL 和生成日志。迁移工具记录逐文件来源 hash、目标 hash 与显式变更，默认拒绝覆盖已存在的产品改动。日常产品 build/pack 不调用恢复器，不依赖 `.local/t02/` 或原官方 DLL。
- `Dm.*` 迁至 `W.Dm.*`，内部 `A` / `NetTaste` 移至 `W.Dm.Internal.Legacy` 下；原全局公开事件参数类型归入 `W.Dm`。不得直接全文件字符串替换 SQL、资源名或反射字面量。必要反射/资源查找调整逐项登记。
- 中性和各文化资源保留精确 LogicalName，由资源源码生成新的 `W.DmProvider.resources.dll`。候选包不包含官方 `DM.DmProvider.dll`、官方卫星 DLL 或原生库。原生调用声明与实际分发资产分开登记。
- 移除旧产品身份元数据；包包含明确来源、版权及许可声明。未提交的新源码不能用旧 commit 的 SourceLink 假装可回溯，来源用当前源码清单 hash 固定。

## API 清单与阶段边界

对原 3,651 条可见 API 输出逐项旧签名、新签名或去向、分类、验证状态和理由；另列 W 新增 API，遗漏或重复不得通过。核心 ADO 门面保留类名，类型属于新程序集，不承诺二进制替换。

分类使用 `preserved`、`obsolete`、`internalized`、`unsupported`。`preserved` 不自动表示全部行为已验证，验证状态单独记录。旧扩展/内部辅助公共类型可先列兼容遗留 `obsolete`，对应其兼容地位和待处理任务。只有实际不可用或已有拒绝路径的入口才可标 `unsupported`，不能把仍可调用的旧代码伪报为已禁用。

完整不可变配置、安全默认值、InitialCatalog 语义与能力守卫是 T04 的任务；TLS 加固及其他行为修复遵循后续依赖。本轮 API 清单必须明确未测试范围和 T04/R1 阻断项。T03 候选包只用于本机包隔离验证，不发布，也不认定 CFG-01–04/06–07 或 R1 完成。

## 包与验证

1. `eng/t03.sh` 生成唯一预发布版本的候选 nupkg，不覆盖同版本既有包；文件存 `.local/t03/`。包内仅有 W 主程序集、重新构建的 W 卫星资源和必要包说明。
2. 独立 net10.0 消费者仅通过该 nupkg 的 PackageReference 恢复，不用 ProjectReference，使用隔离缓存。核对包 hash、实际加载文件、assembly/namespace、API 类型归属、依赖图、包内文件及资源，确认没有官方核心托底。
3. 官方 O、恢复 R 与 W 消费者各用独立进程。W 通过受限测试账号运行有界连接、参数/Unicode CRUD 和显式事务 smoke，验证命名和资源迁移未破坏已声明路径；使用独立连接检查最终状态，只清理本次随机对象。旧高级行为和完整下游 EF 本轮不验收。
4. 资源覆盖 neutral/en/zh-CN/zh-HK/zh-TW；默认离线，不加载真实库凭据。真实模式只经测试 wrapper，诊断不输出口令、连接串或原始错误文本。
5. 来源快照、v1 规范包、T01/T02 已验证实现保持不变；候选包没有上传动作。

## 分工

- 主 agent：设计、范围裁决、code review、最终报告和 progress。
- Sol High / product：`src/W.DmProvider/`、`tools/ImportRestoredSource/`、`docs/compatibility/`、产品来源 notice/license。
- Sol High / package：`tools/ProductPackageProbe/`、`eng/t03.sh`、`eng/T03.md`，独立包消费者及验证工具。
- Luna Max：`tests/fixtures/product-identity/` 的身份/工厂/资源契约和纯合成 smoke 数据，不连接实例。
- Sol Low：独立 build/pack/消费、API 覆盖、包隔离和真实 smoke；仅写 `.local/verification/t03/`，实现缺陷交 Sol High 修复。

T03 验收仅覆盖 BAS-02、CFG-05及本轮资源/包消费范围；后续安全配置和功能门槛仍待各自任务。
