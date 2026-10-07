# T19 精确 R3 候选 EF 下游隔离验收

固定EF commit113014cc74dd1f751ef97226a78d2ec855b32c8c，archive SHA9dead07c1de4220bd13bdadab0ed2713f43008af5e7c0cf7a629549a6739096d，EFCore10.0.12，downstream SDK10.0.401。Provider自己的SDK10.0.203是另一门。最终driver为`0.1.0-r3.<UTC14timestamp>`的精确PackageReference，包/DLL SHA/MVID/root source-manifest.json实际SHA必须一致，无driver ProjectReference、official fallback或SourceLink虚构。

```sh
python3 tools/DownstreamAcceptance/T19/prepare.py \
  --output .local/verification/r3/t19/ef/<fresh-run> --ef-archive <readonly-pinned-archive.tar> \
  --candidate <final.nupkg> --candidate-sha256 <sha> --version <exact-version> \
  --driver-dll-sha256 <sha> --driver-mvid <guid> \
  --source-manifest <root-final-public/source-manifest.json> --source-manifest-sha256 <file-sha>
python3 tools/DownstreamAcceptance/T19/run.py build --output <fresh-run> --profile shared --lane unit
python3 tools/DownstreamAcceptance/T19/run.py run --output <fresh-run> --profile shared --lane unit
```

prepare不调用dotnet/DB/Git、不读secret，只读取已有固定archive和冻结包/metadata。每个lane重新解archive，应用只读accepted T12 combined patch及列明组件hash，再加T19 overlay；删除旧driver锁/guard，after∪before生成完整add/change/delete patch。新临时目录再解固定archive+acceptedpatch+overlay逐源hash证明可重建。所有输出仅新`.local/verification/r3/t19/ef/<run>`，旧T12/source/evidence/EF workspace只读。

Overlay保留原文件EOF字节并使用标准无末尾LF标记；跨平台临时重建对overlay的dry-run/apply均使用`patch -p1 -E`正确删除其明确删空文件，accepted combined调用不变，严格source hash对照仍必须通过。

shared declared lanes为unit、有限权限audit、原functional、CLI、current-schema scripts、原四项specification切片和r3_shared。原legacy filter与全部accepted case名字保存在legacy-case-baseline.json（unit307/functional76/spec4/scripts4/CLI1）及原TRX/source-gate hash；只剥离新增R3类独立lane，不缩旧集合或禁Savepoint。unit再含3个真实EF strategy sync/async Unknown synthetic注入attempt1、sameexception/IsTransientfalse/numberoverride拒绝；这是下游异常契约，不冒充driver→EF网络故障端到端。

tls只declared unit+r3_tls支持subset：异步CRUD/生成键/concurrency/40KiB LOB，以及Savepoint明确before-send拒绝。旧direct证书配置保持Pooling=false；R3 tests用独立DataSource(Pooling=true,max1)并将caller-owned DbConnection交EF，避免已接受direct-TLS-files池化拒绝与虚假每连接owner。shared旧功能开启真实受控registry。所有新conn仍TEST/同Schema，默认trace明确OFF、原Connect/Command20秒；CLI内部300、unit host600、其他test host900、build600、audit90秒保持。

固定策略 `t19-shared-functional-host-v2` 仅将新候选 `(shared,functional)` host 上限修订为1500秒。依据、同环境ABBA事实及预先公式见 [T19 shared host 预算修订](../../../docs/implementation/maintenance/T19-shared-host-budget.md)：`(889.628586747 + 6 * 27.1) * 1.2 = 1262.6743040964`，按300秒粒度向上取整为1500。27.1仅是预算估计项，不成为新增case断言。原76 case、1266 seed、SQL、payload、filter、Savepoint和每调用20秒不改；旧v5的900秒失败记录保持只读。1500秒仍失败时停止，不重试或自动扩大。

prepare在manifest冻结完整budget_policy及各lane policy_id/host_timeout_seconds。runner在任何业务进程开始前核对固定策略，拒绝旧/篡改预算；不提供环境变量或CLI预算覆盖。每个capture记录policy/profile/lane/execution_phase、host_timeout_seconds、timed_out、真实host_exit_code和单调时钟actual_elapsed_seconds。timeout仍返回协调员exit_code124，并保留真实被终止host的退出码。validator将策略、过程结果与TRX、完整case身份、实际DLL tuple和TEST前后独立inventory核对，单独status不能通过。纯离线预算合同可由独立验证者运行 `python3 -B -m unittest discover -s tools/DownstreamAcceptance/T19 -p test_budget_contract.py`；它不需要dotnet、TEST连接或数据库。

每lane独立feed/cache/bin/obj/锁/CLI子项目，force-evaluate→locked restore与实际assets/deps/package类型校验。testhost/audit/嵌套CLI各独立CreateNew guardproof核version/SHA/MVID+host目录加载；CLI复制同一guard且每子host独立proof。Schema2 guard从该实际进程的已加载 `driver.Location` 打开的stream留存DLL至runner新建的private `loaded-drivers`，不是复制NuGet/cache中的另一份文件。文件为0600/CreateNew且root0700，CLI launch显式传 `DAMENG_T19_DRIVER_ASSET_ROOT`；既有目标/root、escape和symlink拒绝。proof记录相对单filename、独立SHA/MVID及固定来源 `loaded_driver_location`，validator逐份host proof核对应asset；CLI自己的临时目录可正常清理，不再错误要求其清理后仍有DLL。实际host/deps/version身份门保持。MissingAsset、SHA篡改、MVID不符使用各自固定安全code。有限Schema前后hash inventory只能证明本lane没有新遗留对象，不能删除或宣称T17旧对象恢复。原admin/user/tablespace/schema creation/capability/official DateTimeOffset边界仍排除，不调用旧all/admin launcher。

R3 Facts在CREATE前写不可变`*.owned.json`（TEST身份+精确随机表fresh absence），供timeout后按名字精确恢复；最终case另外写`*.json`，owned ledger不计pass/额外case。source/hash/strictTRX/case IDs/auditbeforeafter/testhost+nestedCLIproof/binariesbeforeafter/locks全部进summary tuple，不因status alone接受。缺环境、失败或cleanup未知保持pending，worker实际日志在内存先脱敏再写，原始SQL/凭据/消息不入公共输出。

`validate.py --output <run> --profile shared|tls`核相应完整declared范围。TLS不能代替shared全合同；shared long_resource=`not_verified`不新增T18TLS设计之外门，但shared streaming/fullEF/R2原crosspage/精确cleanup仍在最终R3矩阵pending。所有summary明确upstream_pending与production_release_accepted=false；root逐项readback后收口，不自动发布。

CLI retention 修订采用同一不可变 v6 包的严格工具 overlay，revision=`t19-cli-retention-v1`，不生成新的driver包或重跑LOB/资源门。producer仍绑定原source-manifest文件SHA和包/DLL/MVID；execution manifest/archive逐字节和mode验证完整old/new tree，只有显式批准的8个T19工具文件可modify/add，禁止其他变更、delete、symlink、escape或自动扩大allowlist。prepare增加 `--execution-overlay <execution-overlay.json> --producer-source <readonly-v6-source-tree> --execution-source <verified-execution-tree> --base-output <readonly-v6-EF-run>`，四项必须同时给出；它冻结overlay及manifest/archive副本/hash、原base EF manifest/summary/legacybaseline的before-SHA，并保留原v6 source-manifest。此overlay运行窗口只允许shared CLI lane，既有case/全部nested EF命令/预算保持。

`validate.py --output <new-cli-run> --profile shared --base-output <readonly-v6-EF-run> --execution-overlay <frozen-overlay.json>`将新CLI和原nonCLI分开核查。旧nonCLI使用原producer工具hash/source/compiled binaries/锁/完整TRX/case/audit门，并从真实原lane.json及每个proof/audit/TRX文件重读与摘要核对，原schema1不补造asset；新CLI的每份schema2 guardproof强制对应独立留存资产。聚合另存于新run的 `aggregate-cli-retention-<profile>.json`，标明各lane原工具或新execution revision及原/新manifest、记录和proof文件SHA，不覆盖旧summary，旧CLI不能继承。没有完整lineage或原evidence文件不得因status alone接受。
