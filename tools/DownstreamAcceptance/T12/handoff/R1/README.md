# R1 下游交接（已独立验收，限定范围）

候选 `W.DmProvider/0.1.0-r1.20260930064454`，包 SHA256 `a5528d682283c343511a5343275c1420e6ff1b2867afe9e5384fbb8f0f8136c8`，加载 DLL SHA256 `d3eda7c4c0156f89495b9c6540c39188d44ca4e2c4cae572345da7a1398692ed`。驱动 SourceHash `a2f637dd96d37ad89bf480b0ec742b40c0c0fe01929aa90de75d00c3fa587f8b`，准确278个输入文件，precommit `8e42481e12fd7fd23ce25074fc6b1ca01686167e`。**SourceLink=false**：包在未提交源上构建；随后交付commit只能外部关联，不能声称包内含该新commit。src开发默认README/csproj历史值保留，实际本候选以此交接和R1报告为准；未来发行须新版本/新包。

EF基线 `113014cc74dd1f751ef97226a78d2ec855b32c8c`，archive SHA256 `9dead07c1de4220bd13bdadab0ed2713f43008af5e7c0cf7a629549a6739096d`，EF10.0.12/SDK10.0.401；driver SDK10.0.203。原EF仓库只读，未checkout/reset或写入；结束观察原HEAD已经5b5bb25且有8项用户修改，本补丁仍从固定113014导出，不覆盖它们。

| 已接受门 | 结果 | 独立输入 |
| --- | --- | --- |
| driver Release离线8suite与包/公开surface审计 | 298/298、package audit通过 | producer064454实际包 |
| unit | 307/307 | full-v5 |
| 普通functional（含query/CRUD/types、事务/保存点、迁移和reverse engineering） | 76/76 | full-v5 |
| specification既有切片 | 4/4 | full-v5，非全上游conformance |
| 当前TEST Schema脚本create/up/down/幂等 | 4/4 | full-v5 |
| dotnet-ef migrations/scaffold/生成模型与DLL身份 | 1/1 | cli-final-v6 |

全部宿主exit0，完整16项TRX、零skip/error/abort/timeout；真实testhost/CLI子进程DLL hash与本nupkg一致。DB使用WDM_PROVIDER_TEST，前后独立连接确认当前Schema对象inventory 0→0，本次制品无敏感命中。SOI为明确用户授权的独立维护所得，ADMIN_OPTION=N；普通测试不加载SA。原-5504、原日期失败和各CLI失败证据保留。CLI-v6仅改checker读取IDesignTimeModel，comment/facet原断言保留；其余功能门源/产品包未改变，验收证据分别绑定两份manifest。

## 补丁与复现

`combined.patch` 对固定clean EF commit一次应用，包含准确W namespace/package、TEST typed工厂、R1安全重试guard、W改善测试、CLI安全/currentSchema脚本和受测依赖锁。unit/spec锁取自full-v5实际通过的restore graph，CLI/functional/src锁来自v6，版本相同。`cli-safety.patch`、`current-schema-scripts.patch`、`bridge.patch`是分阶段源补丁，顺序为clean→CLI→scripts→bridge；初次restore更新锁再locked restore，最终以combined为准。生成NuGet.Config、本机绝对feed、T12Audit和所有bin/obj/cache不嵌入combined；由受控prepare生成。原管理员launcher不在已验证graph内，不运行all/admin或复制其secrets。

在固定commit的**新隔离副本**先 `git apply --check <combined.patch>` 再 `git apply <combined.patch>`。原用户工作区不能直接替换。实际重现使用仓库 `tools/DownstreamAcceptance/t12_prepare.py`：传上述真实nupkg四identity和两个owner patch，固定commit/hash不跟HEAD；EF checkout可由非秘密 `DAMENG_T12_EF_REPO` 指定，.401 host由 `DAMENG_T12_DOTNET_HOST` 指定。root目录、SDK/feed/cache/加载DLL与锁都会记录，候选版本不能复用覆盖。

Low实际入口 `t12_run.py build|run --output <fresh .local/t12/ef/run> --lane audit|unit|functional|specification|scripts|cli`；scripts/cli共享functional build。每次dotnet设可写DOTNET_CLI_HOME、禁首次体验/telemetry，single-node/no-reuse/no-shared-compilation/disable-build-servers。DB仅由TEST wrapper注入唯一DAMENG_TEST_CONNECTION_STRING，认证前核typed User、连接后核USER/currentSchema；W测试显式PlaintextAllowed，生产RequireTls默认保留。CLI fixed host目录prepend子PATH，SDK pin不放宽；scaffold no-onconfiguring/named内存配置、不落连接串；所有子进程有界期限和安全输出。

R1重试只允许driver IsTransient=true且非CommitUnknown；当前普通和Unknown错误都不推荐重放。非空additionalErrorNumbers在builder/strategy明确NotSupportedException，null/empty仍保留。保留SQL/单修改命令batch、SQL%ROWCOUNT、生成键、GetString时间映射和自动保存点语义；官方失败断言和raw表示characterization不混作W成功。

## 回退

回退只作用于隔离EF接入副本/接入分支：

```sh
git apply --reverse --check <combined.patch>
git apply --reverse <combined.patch>
# 核对恢复DM.DmProvider/8.3.1.47463及原lock，再用已有.401host:
"$DAMENG_T12_DOTNET_HOST" restore src/W.EntityFrameworkCore.Dameng/W.EntityFrameworkCore.Dameng.csproj --locked-mode --disable-parallel -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers
```

回退前停止消费者并检查是否有用户后续修改；check失败就人工review，不reset覆盖。移除运行时W专用配置，恢复原官方连接配置/官方包；O/R/W继续独立进程，不运行fallback。上述代码回退不回滚已隐式提交的DDL，也不能把官方已知行为写成W安全契约。

## 验收边界

只证明本机macOS arm64/.NET10、DM8.1.5.60和EF10.0.12；TLS/其他profile以独立T07证据为准。没有Windows/Linux全RID、所有DM8或EF10.x、生产/Staging、远程NuGet发行证明。四项specification不是完整EF conformance。R1 Async调用兼容不是真异步I/O/取消；pool、streaming LOB、native复杂类型/ambient或分布式事务未启用。未来async/pool/streaming合成fixtures均not_run准备材料；R1收口后停止，不进入T13/R2。
