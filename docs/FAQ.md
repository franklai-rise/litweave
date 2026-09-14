# FAQ / 常见问题

## Does LitWeave need a Zotero plugin?

No. This beta uses Zotero's local API and Zotero URI handlers. It has no installed or bundled Zotero plugin.

## Does moving a node move the Zotero item?

No. Nodes, links, groups, colours, text, and board names are local LitWeave data only.

## Why cannot I refresh while Zotero is closed?

Refreshing reads Zotero's local API, which is available only while Zotero is running. Cached whiteboards remain available offline.

## How do I connect two cards?

Move to a card edge until its connection points appear, then drag a point onto another card. Alternatively, select **建立关联 / Add relation** and click the target. A paper cannot connect to itself or to another copy of the same Zotero item.

## What should I do if saving reports an error?

Do not close or switch away from the active board. Copy the workspace and `%LOCALAPPDATA%\\LitWeave` to a safe location, then attach only anonymised diagnostic details to an issue.

## Why do I see a Windows warning?

The beta installer is not code-signed. Verify the SHA-256 value published on the matching GitHub Release. Do not disable or bypass Windows security features.
