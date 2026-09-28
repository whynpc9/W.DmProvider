# net9.0 构建逆向与结论再验证

> 日期：2026-09-28。对象：`packages/extracted/lib/net9.0/DM.DmProvider.dll`（885,760 字节），反编译产物独立存放于 `decompiled/net9.0/`（239 个 .cs）。

## 1. 与 net8.0 构建的全量 diff 结果

`diff -rq net8.0 net9.0` 仅 4 处差异，**全部为编译器代码生成差异，零功能性差别**：

| 差异 | 内容 | 判定 |
|---|---|---|
| `DM.DmProvider.csproj` | TargetFramework net8.0 → net9.0 | 构建配置 |
| `--y__InlineArray5.cs`（net9.0 新增） | `[InlineArray(5)]` 编译器生成结构体 | C# 13 编译器对 params 集合表达式的代码生成产物 |
| `Dm/DmSchema.cs` | 4 处 `AppendFormat`/`string.Format` 多出 `default(ReadOnlySpan<object>)` 实参 | 同上，span 重载解析差异，语义不变 |
| `Dm/DmTrace.cs` | `string.Format` 改用 InlineArray 缓冲 + `ReadOnlySpan<object?>` | 同上 |

其余 234 个 .cs 文件两版逐字节相同。

## 2. 结论再验证（逐条复核于 net9.0 树）

因源文件逐字节相同，行号级引用在 net9.0 全部成立。对关键项做了重新抽查：

| 原结论 | net9.0 复核 | 结果 |
|---|---|---|
| 无 `DbBatch`/`CanCreateBatch`/`CreateBatch` | 全库 grep 无匹配 | ✅ 成立 |
| 无任何 `*Async` ADO 覆写 | grep 仅命中非 ADO 用途（socket/FLDR 内部） | ✅ 成立 |
| RepeatableRead 非 MySQL 兼容模式抛错、Snapshot 走 default 抛错 | `Dm/DmConnInstance.cs:294-306` 逐字节一致 | ✅ 成立 |
| P0-1 `GetByte`/`GetSByte` 对 DOUBLE 列误调 `GetDate` | `Dm/DmGetValue.cs:348-349`（GetSByte 路径，case 11→GetDate）bug 原样存在；同文件 GetInt32(:254)/GetInt16(:421) 路径正确 | ✅ 成立 |
| P0-3 `FldrStatement.setMaxRows` 必抛 | `Dm/FldrStatement.cs:1078-1085` 一致 | ✅ 成立 |
| P0-4 `InitialCatalog` 塞值给 user+password | `Dm/DmConnectionStringBuilder.cs:1068-1082` 一致 | ✅ 成立 |

其余 P0/P1/P2 项（过滤器单例污染、MSG 新式路径无锁、SSL 证书恒真、超时键单位等）所在文件均不在 diff 清单内，逐字节相同，结论全部延续。

## 3. 结论

**net8.0 与 net9.0 的 DM.DmProvider.dll 是同一份源码在两个 TFM 下的编译产物，全部逆向结论、缺陷清单、EF 适配待做清单对 net9.0 完全适用，无需任何修正。**

对 EF 适配的实际含义：限制登记（`docs/ef-adapter-backlog.md`）不按 TFM 区分；针对 net8.0 产物做的 Harmony patch / fork 修复，在 net9.0 上方法签名与行号一致，可直接平移。
