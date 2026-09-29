# 当前调查基线与来源

核对日期：2026-09-29。工作树在记录前为干净状态，`main` HEAD：`860988296cb95deeb600328cb7bcd2160237a94f`。

| 对象 | SHA-256 |
|---|---|
| `packages/dm.dmprovider.8.3.1.47463.nupkg` | `62ec22acef319847ee59610359f02f1d3e08dc76af39b92ba50b23cc25e22e75` |
| `packages/extracted/lib/net9.0/DM.DmProvider.dll` | `8f6e59680d0a076df53bea50d5a2bdbd288535cd85b2d7ca5064c02adc9c6e6b` |
| `packages/extracted/lib/net8.0/DM.DmProvider.dll` | `7d7487f346eb818bc20ba8a657ae555addfd2dd5f3cc932f970cb516a7df2c75` |

来源包及静态分析：[`docs/reverse/README.md`](../reverse/README.md)、[`docs/reverse/net9-verification.md`](../reverse/net9-verification.md)、[`docs/ef-adapter-backlog.md`](../ef-adapter-backlog.md)。产品规划的直接来源是 ChatGPT 对话《规划Dm Provider开发》（conversation ID `6aba2c15-0058-83ec-8c60-e84443ac04b7`）；用户提供的完整实现规范 ZIP 已迁入 [`v1/`](v1/README.md)。规范内锁定的来源引用见 [`v1/SOURCES.md`](v1/SOURCES.md)。

这些校验值用于确认**当前本地样本**，不证明 NuGet 在线包或下游运行时加载资产在其他环境仍相同。T01 应进一步记录下游项目 `project.assets.json` 的 compile/runtime 资产、实际加载路径、程序集版本、MVID、运行时、OS/RID，并固定复现所需的数据库版本和连接条件；任何新事实必须附命令、结果与日期。

`packages/`、`decompiled/` 是来源快照。实现任务不得直接把逆向目录当作产品源码修改；需要修复时在独立产品树中记录来源映射和行为差异。
