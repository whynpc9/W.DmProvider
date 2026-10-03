# R3 EF 适配 handoff（准备模板，未发行）

- EF source pin：113014cc74dd1f751ef97226a78d2ec855b32c8c；archive SHA：9dead07c1de4220bd13bdadab0ed2713f43008af5e7c0cf7a629549a6739096d。
- EFCore10.0.12 / downstream SDK10.0.401；driver SDK10.0.203 单独核验。
- 最终候选：`<exact 0.1.0-r3.UTCtimestamp>` / package SHA `<sha>` / driver DLL SHA `<sha>` / MVID `<guid>` / canonical source-manifest JSON SHA `<sha>`。
- 输入：accepted T12 bridge/CLI/current-schema components + T19 complete add/change/delete overlay；重建目录源哈希=`<manifest>`，driver exactPackageReference，无official fallback。
- Overlay无末尾LF字节用标准marker保留；临时重建overlay dry-run/apply须`patch -p1 -E`跨平台删除明确删空文件，accepted combined原调用与逐源hash门不变。
- shared evidence：unit `<tuple>`、limited-permission `<tuple>`、legacy functional `<tuple>`、CLI nested guards `<tuple>`、scripts `<tuple>`、spec4 `<tuple>`、R3Shared `<tuple>`。
- TLS evidence：unit `<tuple>`、R3Tls支持subset `<tuple>`；Savepoint明确拒绝，不能替代shared成功。
- 每tuple包括真实exit/strictTRX/all case IDs、fresh TEST+Schema、before/after inventory、actual host+CLI SHA/MVID/deps、binarybeforeafter、locks及source/package immutability。
- 精确对象：CREATE前immutable ownership ledgers与最终fresh absence `<paths>`；pending ledger不是通过证明，不清理历史/跨Schema对象。
- 范围：shared long_resource not_verified（原T18要求TLS600）；shared大值/fullEF/R2原crosspage/历史cleanup、完整matrix仍 `<pending>`。
- SourceLink不声明；来源用精确archive/manifest/hash。production_release_accepted=false，未NuGet/tag/GitHub Release。
- 回退：pin root确认的旧候选或关闭新Pooling，重建connection/dataSource；Unknown提交不自动重试。
