import { beforeEach, describe, expect, it } from 'vitest';
import { getAppState, loadCanvas, refreshZotero } from './bridge';

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
});
