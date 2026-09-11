# LitWeave

**Visual Literature Mapping for Zotero**

LitWeave is a local-first Windows desktop application for turning a Zotero
collection into a visual literature map. It reads metadata through Zotero's
local HTTP API, keeps the map and annotations in its own SQLite database, and
opens items or PDFs in Zotero when requested. Dragging, grouping, connecting,
labelling and deleting nodes in LitWeave never changes Zotero.

> LitWeave is an independent open-source project. It is not affiliated with,
> sponsored by, or endorsed by Zotero or the Corporation for Digital
> Scholarship. “Zotero” is a trademark of the Corporation for Digital
> Scholarship; see the [Zotero trademark guidance](https://www.zotero.org/support/terms/trademark).

## Current release

The first public milestone is `v0.1.0` for Windows 10/11 x64 and Zotero 9.x
personal libraries. The release line is deliberately conservative:

- manual `Refresh Zotero` reads the local API and presents new records in a
  `Refresh Review` tray;
- nested Collection frames and paper cards are laid out on a persistent canvas;
- directed or undirected labelled relationships and free-text notes are local
  to LitWeave;
- the context menu can locate a Zotero item or open a PDF with the attachment
  key (`zotero://open-pdf/library/items/{attachmentKey}`);
- JSON and SVG canvas export are available; PDF files and Zotero's database are
  never copied into the repository or the LitWeave database.

There is no Zotero plugin in v0.1. The reserved future name is **LitWeave
Connector for Zotero**. There is also no network AI or DeepSeek integration;
the provider-neutral **LitWeave AI Suggestions** interface is reserved for a
future, opt-in feature.

## Privacy and data boundary

LitWeave calls `http://127.0.0.1:23119/api/` only when the user clicks refresh
or asks to open a Zotero item. It reads Collections, item metadata, creators,
tags and PDF attachment keys. It does not access `zotero.sqlite`, copy PDF
content, or call Zotero write endpoints. Local map data is stored at
`%LOCALAPPDATA%\\LitWeave\\litweave.db`. The public repository contains only
constructed test data and source code.

Zotero must be running with its local API enabled for refresh and open actions.
Existing cached data and maps remain viewable when Zotero is offline; the UI
shows the last successful refresh and the failure reason.

## Build from source

Prerequisites: .NET SDK 10, Node.js 20+, npm, and the WebView2 runtime.

```powershell
cd F:\Codex\Library_Frank\LitWeave
cd web
npm ci
npm run build
cd ..
dotnet build LitWeave.sln -c Release
dotnet test LitWeave.sln -c Release
dotnet publish src\LitWeave\LitWeave.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The GitHub Actions workflow performs the same checks on a Windows runner. The
installer is intentionally unsigned in this first release, so Windows
SmartScreen may show a warning. Verify the SHA-256 file published with each
release before installing.

## Roadmap

The v0.1 boundary excludes Group Libraries, cross-device sync, collaboration,
mobile clients, automatic citation-relation detection and background Zotero
watching. A future AI provider may suggest labels or summaries only from items
explicitly selected by the user; it must never create an edge automatically.
