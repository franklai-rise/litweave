# LitWeave v0.2.1-beta.1

## Preview / 测试版

This is a pre-release for Windows 10/11 x64 and Zotero 9.x personal libraries. It does not replace the v0.1.0 stable release. Back up `%LOCALAPPDATA%\\LitWeave` before upgrading. The installer is unsigned; verify the SHA-256 asset before installation and follow Windows security guidance.

这是 Windows 10/11 x64 与 Zotero 9.x 个人文库的预发布版本，不替代 v0.1.0 稳定版。升级前请备份 `%LOCALAPPDATA%\\LitWeave`。安装包未签名，请先核对发布页 SHA-256，不要绕过 Windows 安全保护。

## Added / 新增

- Independent, browser-style research-whiteboard tabs.
- Manual Zotero refresh and read-only folder/library browsing in the sidebar.
- Paper, image, text, and group nodes with local-only labelled relations.
- Eight edge-near connection points, relation-button fallback, duplicate-paper feedback, custom title sizes, hover metadata, and PDF/item actions in Zotero.
- Export to PDF, PNG, SVG, JSON, and portable `.litweave`; the portable source includes referenced metadata snapshots and images and imports as a new board.
- Local-only autosave state, explicit Ctrl+S feedback, and source-package write errors that prevent tab switching or closing the active board.

## Fixed / 修复

- Enlarged connection snap radius and preserved per-edge handle identifiers.
- Source-package files now use a stable board identifier, avoiding stale recovery files after a whiteboard rename.
- PDF export now reports a native print failure instead of incorrectly claiming success.

## Boundaries / 边界

LitWeave does not modify Zotero, read or copy PDFs, upload library data, offer cloud sync, or include AI. It is an independent project and is not endorsed by Zotero.
