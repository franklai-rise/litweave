# 【开源工具分享】LitWeave：连接 Zotero 的多标签文献研究白板（测试版）

大家好，我在开发一个面向 Zotero 用户的本地桌面工具 **LitWeave**。它不是 Zotero 插件，而是一个独立的 Windows 文献研究白板：从 Zotero 本地读取文献元数据后，可以把主动选择的文献拖到不同白板中，用文字、图片和带标签的连线整理阅读线索。

项目地址：<https://github.com/franklai-rise/litweave>

测试版下载：<https://github.com/franklai-rise/litweave/releases/tag/v0.2.1-beta.1>

## 适合的场景

- 将同一课题中的关键论文、方法和实验结果组织成白板；
- 用“引用”“对比”“支持”等自己定义的本地关系连接卡片；
- 从 Zotero 的不同文件夹主动挑选文献，而不是自动铺满整个画布；
- 使用多张标签式白板分别梳理研究问题、方法路线或写作结构。

## 当前功能

- 左侧浏览 Zotero 文件夹和文献，手动刷新；右侧是独立、多标签白板；
- 拖入文献后默认命名为 `#1`、`#2`，可双击改名；重复加入会提示并定位已有卡片；
- 支持文献、图片、文字和分组框，可从边缘连接点拖线，也可点选“建立关联”；
- 连线支持标签、备注、箭头、颜色、粗细与线型；
- 悬停卡片显示 Zotero 缓存的题名、作者、期刊、DOI 等信息；可在 Zotero 中定位或打开 PDF；
- 支持 PDF、PNG、SVG、JSON 及 `.litweave` 源文件导出；导入源文件始终新建白板。

## 数据与隐私

LitWeave 只调用 Zotero 本地 API，不修改 Zotero 条目、文件夹、标签、笔记或 PDF；不读取 `zotero.sqlite`，不上传文库，也没有 AI 或 DeepSeek 功能。白板数据保存在 LitWeave 自己的本地目录。

## 测试版提示

目前仅支持 Windows 10/11 x64 与 Zotero 9.x 个人文库。安装包未签名，请先核对 Release 页面提供的 SHA-256。升级前建议备份 `%LOCALAPPDATA%\\LitWeave`。

欢迎反馈可复现问题和使用建议。提交截图或日志前，请删除文献题名、作者、PDF、凭据及其他私人信息。

LitWeave 是独立开源项目，与 Zotero 官方及 Corporation for Digital Scholarship 没有从属、赞助或背书关系。
