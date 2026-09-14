import { beforeEach, describe, expect, it } from 'vitest';
import { createBoard, getAppState, listBoards, loadCanvas, refreshZotero, saveBoard } from './bridge';

describe('native bridge fallback', () => {
  beforeEach(() => {
    window.__LITWEAVE_MOCK__ = true;
  });

  it('returns anonymised app state without a WebView host', async () => {
    const state = await getAppState();
    expect(state.app.name).toBe('LitWeave');
    expect(state.snapshot?.items).toHaveLength(2);
  });

  it('exposes new records as refresh review candidates', async () => {
    const result = await refreshZotero();
    expect(result.diff.isBaseline).toBe(true);
    expect(result.diff.newItemKeys).toEqual(['A001', 'B002']);
  });

  it('starts a new canvas when no saved document exists', async () => {
    const loaded = await loadCanvas('root');
    expect(loaded.document).toBeNull();
  });

  it('keeps independently named boards in the fallback store', async () => {
    const created = await createBoard('Mechanism map');
    await saveBoard({ ...created.document, name: 'Mechanism map' });
    const listed = await listBoards();
    expect(listed.boards.some(board => board.id === created.document.id && board.name === 'Mechanism map')).toBe(true);
  });
});
