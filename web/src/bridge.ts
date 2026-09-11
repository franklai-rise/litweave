import type { CanvasDocument, RefreshResult, ZoteroSnapshot, ZoteroStatus } from './types';

type BridgeResponse<T> = { id: string; ok: boolean; payload?: T; error?: { code: string; message: string } };

type NativeWebView = {
  postMessage: (value: unknown) => void;
  addEventListener: (type: 'message', listener: (event: MessageEvent<BridgeResponse<unknown>>) => void) => void;
};

declare global {
  interface Window {
    chrome?: { webview?: NativeWebView };
    __LITWEAVE_MOCK__?: boolean;
  }
}

const pending = new Map<string, { resolve: (value: unknown) => void; reject: (error: Error) => void }>();
let listening = false;

function mockSnapshot(): ZoteroSnapshot {
  return {
    libraryKey: 'personal', refreshedAt: new Date().toISOString(), contentHash: 'mock',
    collections: [
      { key: 'root', name: 'Example research', parentKey: null, itemCount: 2 },
      { key: 'methods', name: 'Methods', parentKey: 'root', itemCount: 1 },
      { key: 'results', name: 'Results', parentKey: 'root', itemCount: 1 },
    ],
    items: [
      { key: 'A001', itemType: 'journalArticle', title: 'Physics-informed structural analysis', year: '2024', publicationTitle: 'Example Journal', firstAuthor: 'A. Chen', creators: [{ creatorType: 'author', name: 'A. Chen' }], collectionKeys: ['root', 'methods'], tags: ['PINN'], attachments: [{ key: 'P001', parentItemKey: 'A001', contentType: 'application/pdf' }], hasPdf: true },
      { key: 'B002', itemType: 'journalArticle', title: 'A review of explainable mechanics', year: '2023', publicationTitle: 'Example Review', firstAuthor: 'B. Li', creators: [{ creatorType: 'author', name: 'B. Li' }], collectionKeys: ['root', 'results'], tags: [], attachments: [], hasPdf: false },
    ],
  };
}

function mockInvoke<T>(type: string, payload: unknown): Promise<T> {
  const snapshot = mockSnapshot();
  if (type === 'GetAppState') {
    return Promise.resolve({ app: { name: 'LitWeave', subtitle: 'Visual Literature Mapping for Zotero', version: '0.1.0' }, snapshot, lastRefresh: snapshot.refreshedAt, status: { isRunning: true, apiEnabled: true, message: 'Mock API', checkedAt: snapshot.refreshedAt } } as T);
  }
  if (type === 'RefreshZotero') {
    return Promise.resolve({ snapshot, diff: { isBaseline: true, newItemKeys: snapshot.items.map(i => i.key), updatedItemKeys: [], removedItemKeys: [], collectionChanges: snapshot.collections.map(c => c.key) }, status: { isRunning: true, apiEnabled: true, message: 'Mock API', checkedAt: snapshot.refreshedAt } } as T);
  }
  if (type === 'LoadCanvas') return Promise.resolve({ document: null } as T);
  if (type === 'SaveCanvas' || type === 'SetLanguage' || type === 'SaveCanvasExport') return Promise.resolve({ saved: true } as T);
  if (type === 'OpenZoteroItem' || type === 'OpenZoteroPdf') return Promise.resolve({ opened: true } as T);
  void payload;
  return Promise.reject(new Error(`Mock bridge does not implement ${type}`));
}

export function isNativeBridgeAvailable(): boolean {
  return Boolean(window.chrome?.webview) && !window.__LITWEAVE_MOCK__;
}

export function invoke<T>(type: string, payload: unknown = {}): Promise<T> {
  if (!isNativeBridgeAvailable()) return mockInvoke<T>(type, payload);
  const webview = window.chrome!.webview!;
  if (!listening) {
    webview.addEventListener('message', (event) => {
      const response = event.data;
      const entry = pending.get(response.id);
      if (!entry) return;
      pending.delete(response.id);
      if (response.ok) entry.resolve(response.payload);
      else entry.reject(new Error(response.error?.message ?? 'Native bridge error'));
    });
    listening = true;
  }
  const id = crypto.randomUUID();
  return new Promise<T>((resolve, reject) => {
    pending.set(id, { resolve: resolve as (value: unknown) => void, reject });
    webview.postMessage({ id, type, payload });
  });
}

export type AppState = {
  app: { name: string; subtitle: string; version: string };
  storagePath?: string;
  snapshot: ZoteroSnapshot | null;
  lastRefresh?: string | null;
  status: ZoteroStatus;
  capabilities?: { jsonExport?: boolean; svgExport?: boolean; pngExport?: boolean; ai?: boolean; zoteroWrite?: boolean };
};

export async function getAppState() { return invoke<AppState>('GetAppState'); }
export async function refreshZotero() { return invoke<RefreshResult>('RefreshZotero'); }
export async function loadCanvas(rootCollectionKey: string) { return invoke<{ document: CanvasDocument | null }>('LoadCanvas', { rootCollectionKey }); }
export async function saveCanvas(document: CanvasDocument) { return invoke<{ savedAt?: string }>('SaveCanvas', document); }
