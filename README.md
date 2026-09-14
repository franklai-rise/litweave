# LitWeave

**Visual Literature Mapping for Zotero / 面向 Zotero 的可视化文献白板**

LitWeave is a local-first Windows research whiteboard. It reads selected Zotero library metadata through Zotero's local API and lets you arrange literature, images, text, and labelled relationships on independent boards. Board edits remain in LitWeave and never alter Zotero.

LitWeave 是一款 Windows 本地优先的研究白板工具。它通过 Zotero 本地 API 读取文献信息，并将文献、图片、文字与关系组织在独立白板中。所有白板编辑都只保存在 LitWeave，不会修改 Zotero 的条目、文件夹、标签、笔记或 PDF。

> **Beta notice / 测试版说明** — v0.2.1-beta.1 is a preview release. Back up `%LOCALAPPDATA%\\LitWeave` before upgrading and report reproducible issues. The installer is unsigned, so Windows SmartScreen may display a warning. Do not bypass Windows security controls; verify the published SHA-256 first.

> LitWeave is an independent open-source project. It is not affiliated with, sponsored by, or endorsed by Zotero or the Corporation for Digital Scholarship. “Zotero” is a trademark of the Corporation for Digital Scholarship; see [Zotero trademark guidance](https://www.zotero.org/support/terms/trademark).

## v0.2.1-beta.1 highlights / 主要功能

- Browser-style, independently saved whiteboard tabs; changing a Zotero folder only changes the left library browser, never the active board.
- Drag a paper from the left list or double-click it to add a neutral `#1`, `#2`… card. A duplicate highlights the existing card; use **Create copy** from the context menu when a second instance is intentional.
- Local images, text cards, groups, Morandi colour styling, title-size control, undo/redo, alignment, zoom and fit-to-content.
- Eight connection points appear only near a node edge. You can drag a point or use **Add relation** then click a target; links are labelled, directional or undirected, and local-only.
- Hover a paper for cached Zotero metadata; use the context menu to show its Zotero item or open a PDF attachment in Zotero.
- Export the current board as `.litweave`, JSON, SVG, PNG, or PDF. A `.litweave` file includes its board, referenced metadata snapshot, and saved images; importing it always creates a new board.

Read [installation and upgrades](docs/INSTALLATION.md), [privacy and data boundaries](docs/PRIVACY.md), [FAQ](docs/FAQ.md), and the [changelog](CHANGELOG.md). The Chinese community post is prepared in [docs/COMMUNITY_POST_zh-CN.md](docs/COMMUNITY_POST_zh-CN.md).

## Requirements / 运行要求

- Windows 10 or 11 x64.
- Zotero 9.x personal library for refresh and open-in-Zotero actions.
- Zotero running with its local API available at `http://127.0.0.1:23119/api/`.
- WebView2 Runtime (normally included with current Windows; install it from Microsoft if the application reports it missing).

LitWeave can still display cached whiteboards when Zotero is offline. Refresh, locate-item, and open-PDF actions require Zotero.

## Privacy and storage / 隐私与存储

LitWeave only calls Zotero's loopback local API after a user action. It does not open `zotero.sqlite`, copy PDFs, write Zotero metadata, call Zotero write endpoints, upload library data, or include AI features. Its database is `%LOCALAPPDATA%\\LitWeave\\litweave.db`; copied whiteboard images are in `%LOCALAPPDATA%\\LitWeave\\images`. The configurable workspace contains exports and recovery `.litweave` packages; it does **not** relocate the database.

## Build from source

Prerequisites: .NET SDK 10, Node.js 20+, npm, and the WebView2 Runtime.

```powershell
cd F:\path\to\LitWeave\web
npm ci
npm test
npm run build
cd ..
dotnet test LitWeave.sln -c Release
```

To create an isolated portable release output without touching an existing `artifacts\\publish` installation:

```powershell
.\packaging\build-release.ps1 -Version 0.2.1-beta.1
```

## Out of scope / 当前不支持

Group Libraries, cross-device sync, collaborative editing, mobile clients, background Zotero monitoring, freehand drawing, automatic citation-relation detection, a Zotero plugin, and AI/DeepSeek integration are not included in this beta.

## Contributing

Please read [CONTRIBUTING.md](CONTRIBUTING.md). Use anonymised test data and keep the Zotero boundary strictly read-only.
