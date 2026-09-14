# Privacy and data boundary / 隐私与数据边界

LitWeave is local-first. It reads only through Zotero's local HTTP API after a user chooses refresh, show-item, or open-PDF. It reads collection structure, item metadata, creators, tags, and attachment keys needed to ask Zotero to open a PDF.

It does not open or write `zotero.sqlite`; change Zotero folders, tags, notes, titles, authors, or related items; copy PDF contents; upload library data; or send data to AI providers. There is no DeepSeek integration in this release.

Local board data stays under `%LOCALAPPDATA%\\LitWeave`. Exported `.litweave` files may contain the metadata snapshot and images selected for that board, so treat them as research files and share only after reviewing their contents.

The public repository and its automated tests use constructed, anonymised fixtures only. Please remove titles, author names, identifiers, screenshots of private papers, PDFs, credentials, and logs containing private information before filing an issue.
