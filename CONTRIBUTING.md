# Contributing to LitWeave

Please keep the Zotero boundary read-only. Do not add code that opens or
mutates `zotero.sqlite`, writes Collection membership, uploads PDF content, or
stores credentials in the repository. Use anonymised fixtures for tests.

Before opening a pull request, run `npm test`, `npm run build`, `dotnet build`
and `dotnet test` from the repository root. New native bridge messages need a
corresponding frontend error state and an automated test.
