# LitWeave architecture

LitWeave has three deliberately separate layers:

1. A .NET 10 WPF shell hosts WebView2 and owns all native operations.
2. A React 18 + TypeScript + Vite frontend renders the graph with
   `@xyflow/react` and talks to the shell through a small JSON bridge.
3. A local SQLite repository stores both a normalized Zotero snapshot and
   LitWeave-only canvas documents.

Whiteboard lifecycle metadata is kept in a separate `board_metadata` table so
pinning, deletion protection, archive state and recoverable trash cannot be
overwritten by an older canvas save. `personal:` records remain legacy cache
records and are excluded from the visible board list. SQLite-native backups
include committed WAL data, images and a checksum manifest; restore always
writes to a new data directory.

The browser surface never talks directly to Zotero. The native
`IZoteroReadClient` sends API v3 requests, follows pagination, normalizes
records and computes a SHA-256 snapshot hash. `RefreshDiff` classifies new,
updated and removed items. The read client has no write method.

`CanvasDocument` uses independent node IDs. A Zotero item key remains the
identity, while an Alias node can represent that item in more than one visible
Collection. Group nodes are visual frames and are never written back to
Zotero. A `IRelationshipSuggestionProvider` empty implementation is included
as a compile-time seam for the future **LitWeave AI Suggestions** module; it
does not contain an SDK, API key or network call.

## Native bridge messages

`GetAppState`, `RefreshZotero`, `LoadCanvas`, `SaveCanvas`, `OpenZoteroItem`,
`OpenZoteroPdf`, board-management actions, backup/recovery actions and
`SaveCanvasExport` are the native messages used by the current app. Every
request receives a JSON response with `ok`, `requestId`, `payload` and an
optional `error` object. Bridge failures are displayed in the UI rather than
silently discarded.
