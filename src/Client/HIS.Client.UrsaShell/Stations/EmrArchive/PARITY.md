# 电子病历 OFD 归档工作站 (EmrArchive) 功能对等清单

> 对应旧站：`MainWindow.axaml` Carousel Slot 5 (病历 OFD 归档与双轨防篡改中心) + 方案第 1 节 R6 / **D4 确认项**（原门诊病历页的 "导出国标 OFD 文件"、"执行防篡改哈希完整性校验" 技术归档动作统一收敛至本专业工作站，不干扰门诊接诊流）。
> 目标：保留国标 GB/T 33190-2016 OFD 封包签署、导出、防篡改 SHA-256/SM2 校验、版式预览与 XML 语义逆向解析全量能力，全屏遮罩抽屉改造为右侧 `RightDrawer`。

| # | 旧版能力 | 新站对应实现 | 差异/增强 |
|---|---|---|---|
| 1 | 归档台账列表 (`OfdArchiveRecords`) | `FilteredRecords` 支持归档号/患者/文书类型/科室筛选 | 增加统计概览卡片与实时关键词过滤 |
| 2 | 病历审签生成 OFD 包 (`SignOfd_Click` / `ExecutePrintAndArchiveCurrentEmr`) | 顶部 `生成并归档 OFD` 按钮 | 直接调用 `ExecutePrintAndArchiveCurrentEmr()` + `ToastHost` 即时回执 |
| 3 | 导出国标 `.ofd` 文件 (`ExportOfd_Click`) | 顶部与行内 `导出 OFD` 按钮（含原门诊页迁入能力 【D4】） | 导出至 `OfdExports/` 目录并提示完整路径 |
| 4 | 防篡改哈希完整性校验 (`VerifyTamper_Click` + `OfdDocumentPackage.VerifyOfdPackage`) | 顶部与行内 `防篡改验签 (F3)` | Toast 明确给出文档 ID 与 SHA-256 摘要校验结论 |
| 5 | 国标 OFD 版式预览 (`PreviewOfd_Click` / `IsOfdViewerDrawerOpen`) | `预览版式 (F2)` 呼出右侧 `RightDrawer` | 展示纸张规格、ZIP 内部目录树、完整 SHA-256 与签名槽位，无全屏遮罩 |
| 6 | 逆向解析语义 XML (`ReverseXml_Click`) | 行内 `逆向 XML` 呼出右侧 `RightDrawer` | 解析 `Doc_0/CustomTags/EmrStructuredData.xml` 并在抽屉内可滚动展示 |

