# Installation and upgrade / 安装与升级

## Install / 安装

Download either the Windows x64 setup program or the portable ZIP from the matching GitHub Release. The setup program installs LitWeave for the current Windows user. The portable build can run from any writable folder after extracting the ZIP.

Before opening a beta build, copy `%LOCALAPPDATA%\\LitWeave` to a safe folder. This contains the SQLite board database, copied whiteboard images, logs, and automatic recovery packages. Do not copy Zotero's own database for LitWeave.

## Upgrade / 升级

1. Close LitWeave only after the status says **Saved / 已保存**.
2. Back up `%LOCALAPPDATA%\\LitWeave`.
3. Install or extract the new version. A new portable folder does not move your existing local database.
4. Open LitWeave and verify one board before deleting any older program copy.

The **workspace directory / 保存目录** controls exports and `.litweave` recovery packages. It does not move `%LOCALAPPDATA%\\LitWeave\\litweave.db`.

## Zotero connection / Zotero 连接

Open Zotero and ensure its local API is available. LitWeave uses the local loopback address only. If it cannot connect, existing whiteboards and cached metadata remain available; use **Refresh Zotero** when the application is running again.

## Portable source files / 源文件

Use **Export source file / 导出源文件** to create `.litweave`. It contains one board, its local links, text, styles, referenced Zotero metadata snapshot, and whiteboard images. **Import source file / 导入源文件** always creates a new board and never replaces an existing one. Keep this file with your project as an extra backup; it is not a copy of PDFs or your complete Zotero library.
