import type { BoardSummary, CanvasDocument, RefreshResult, ZoteroSnapshot, ZoteroStatus } from './types';

type BridgeResponse<T> = { id: string; ok: boolean; payload?: T; error?: { code: string; message: string } };
type NativeWebView = { postMessage: (value: unknown) => void; addEventListener: (type: 'message', listener: (event: MessageEvent<BridgeResponse<unknown> | string>) => void) => void; };
declare global { interface Window { chrome?: { webview?: NativeWebView }; __LITWEAVE_MOCK__?: boolean; } }

const pending = new Map<string, { resolve: (value: unknown) => void; reject: (error: Error) => void }>();
let listening = false;
const mockBoards = new Map<string, CanvasDocument>();
let mockSession = JSON.stringify({ openBoardIds: [], activeBoardId: null });

function mockSnapshot(): ZoteroSnapshot {
  return { libraryKey: 'personal', refreshedAt: new Date().toISOString(), contentHash: 'mock',
    collections: [{ key: 'root', name: 'Example research', parentKey: null, itemCount: 2 }, { key: 'methods', name: 'Methods', parentKey: 'root', itemCount: 1 }, { key: 'results', name: 'Results', parentKey: 'root', itemCount: 1 }],
    items: [
      { key: 'A001', itemType: 'journalArticle', title: 'Physics-informed structural analysis', year: '2024', publicationTitle: 'Example Journal', firstAuthor: 'A. Chen', correspondingAuthor: 'A. Chen', firstAffiliation: 'Example University', creators: [{ creatorType: 'author', name: 'A. Chen' }], collectionKeys: ['root', 'methods'], tags: ['PINN'], attachments: [{ key: 'P001', parentItemKey: 'A001', contentType: 'application/pdf' }], hasPdf: true },
      { key: 'B002', itemType: 'journalArticle', title: 'A review of explainable mechanics', year: '2023', publicationTitle: 'Example Review', firstAuthor: 'B. Li', creators: [{ creatorType: 'author', name: 'B. Li' }], collectionKeys: ['root', 'results'], tags: [], attachments: [], hasPdf: false }
    ] };
}
function emptyBoard(name = 'Untitled board'): CanvasDocument { const now = new Date().toISOString(); return { id: `board:mock-${crypto.randomUUID()}`, name, layoutVersion: 2, nodes: [], edges: [], textNodes: [], viewportX: 0, viewportY: 0, viewportZoom: 1, updatedAt: now, createdAt: now }; }
function mockInvoke<T>(type: string, payload: any): Promise<T> {
  const snapshot = mockSnapshot();
  if (type === 'GetAppState') return Promise.resolve({ app: { name: 'LitWeave', subtitle: 'Visual Literature Mapping for Zotero', version: '0.2.1-beta.1' }, snapshot, lastRefresh: snapshot.refreshedAt, status: { isRunning: true, apiEnabled: true, message: 'Mock API', checkedAt: snapshot.refreshedAt }, capabilities: { jsonExport: true, svgExport: true, pngExport: true, pdfExport: true, ai: false, zoteroWrite: false } } as T);
  if (type === 'RefreshZotero') return Promise.resolve({ snapshot, diff: { isBaseline: true, newItemKeys: snapshot.items.map(i => i.key), updatedItemKeys: [], removedItemKeys: [], collectionChanges: snapshot.collections.map(c => c.key) }, status: { isRunning: true, apiEnabled: true, message: 'Mock API', checkedAt: snapshot.refreshedAt } } as T);
  if (type === 'CheckZotero') return Promise.resolve({ isRunning: true, apiEnabled: true, apiVersion: '3', message: 'Mock API', checkedAt: snapshot.refreshedAt } as T);
  if (type === 'LoadCanvas') return Promise.resolve({ document: null } as T);
  if (type === 'ListBoards') return Promise.resolve({ boards: [...mockBoards.values()].map(board => ({ id: board.id, name: board.name, isLegacy: board.isLegacy, updatedAt: board.updatedAt })) } as T);
  if (type === 'CreateBoard') { const document = emptyBoard(payload?.name); mockBoards.set(document.id, document); return Promise.resolve({ document } as T); }
  if (type === 'LoadBoard') return Promise.resolve({ document: mockBoards.get(payload?.boardId) ?? null } as T);
  if (type === 'SaveBoard' || type === 'SaveCanvas') { const document = payload as CanvasDocument; document.updatedAt = new Date().toISOString(); mockBoards.set(document.id, structuredClone(document)); return Promise.resolve({ savedAt: document.updatedAt, sourcePath: 'mock://workspace/boards' } as T); }
  if (type === 'GetBoardSession') return Promise.resolve({ value: mockSession } as T);
  if (type === 'SaveBoardSession') { mockSession = payload?.value ?? '{}'; return Promise.resolve({ saved: true } as T); }
  if (type === 'SaveImage') return Promise.resolve({ imageId: 'mock-image.png', imageUrl: 'data:image/svg+xml,%3Csvg xmlns="http://www.w3.org/2000/svg" width="180" height="120"%3E%3Crect width="100%25" height="100%25" fill="%23d9dce8"/%3E%3C/svg%3E' } as T);
  if (type === 'GetImageData') return Promise.resolve({ dataUrl: '' } as T);
  if (type === 'GetWorkspaceDirectory') return Promise.resolve({ path: 'mock://workspace' } as T);
  if (type === 'SetWorkspaceDirectory') return Promise.resolve({ path: payload?.path } as T);
  if (type === 'ImportBoardPackage') { const document = emptyBoard('Imported board'); mockBoards.set(document.id, document); return Promise.resolve({ imported: true, document } as T); }
  if (type === 'DeleteBoard' || type === 'SetLanguage' || type === 'SaveCanvasExport' || type === 'ExportCanvasPdf') return Promise.resolve({ saved: true } as T);
  if (type === 'OpenZoteroItem' || type === 'OpenZoteroPdf') return Promise.resolve({ opened: true } as T);
  return Promise.reject(new Error(`Mock bridge does not implement ${type}`));
}
export function isNativeBridgeAvailable(): boolean { return Boolean(window.chrome?.webview) && !window.__LITWEAVE_MOCK__; }
export function invoke<T>(type: string, payload: unknown = {}): Promise<T> {
  if (!isNativeBridgeAvailable()) return mockInvoke<T>(type, payload);
  const webview = window.chrome!.webview!;
  if (!listening) {
    webview.addEventListener('message', (event) => {
      let raw: Record<string, unknown>; try { raw = (typeof event.data === 'string' ? JSON.parse(event.data) : event.data) as Record<string, unknown>; } catch { return; }
      const response: BridgeResponse<unknown> = { id: String(raw.id ?? raw.Id ?? ''), ok: Boolean(raw.ok ?? raw.Ok), payload: raw.payload ?? raw.Payload, error: (raw.error ?? raw.Error) as BridgeResponse<unknown>['error'] };
      const entry = pending.get(response.id); if (!entry) return; pending.delete(response.id);
      response.ok ? entry.resolve(response.payload) : entry.reject(new Error(response.error?.message ?? 'Native bridge error'));
    }); listening = true;
  }
  const id = crypto.randomUUID();
  return new Promise<T>((resolve, reject) => { pending.set(id, { resolve: resolve as (value: unknown) => void, reject }); webview.postMessage({ id, type, payload }); });
}
export type AppState = { app: { name: string; subtitle: string; version: string }; storagePath?: string; workspacePath?: string; snapshot: ZoteroSnapshot | null; lastRefresh?: string | null; status: ZoteroStatus; capabilities?: { jsonExport?: boolean; svgExport?: boolean; pngExport?: boolean; pdfExport?: boolean; ai?: boolean; zoteroWrite?: boolean }; };
export const getAppState = () => invoke<AppState>('GetAppState');
export const checkZotero = () => invoke<ZoteroStatus>('CheckZotero');
export const refreshZotero = () => invoke<RefreshResult>('RefreshZotero');
export const listBoards = () => invoke<{ boards: BoardSummary[] }>('ListBoards');
export const createBoard = (name?: string) => invoke<{ document: CanvasDocument }>('CreateBoard', { name });
export const loadBoard = (boardId: string) => invoke<{ document: CanvasDocument | null }>('LoadBoard', { boardId });
export const loadCanvas = (rootCollectionKey: string) => invoke<{ document: CanvasDocument | null }>('LoadCanvas', { rootCollectionKey });
export const saveBoard = (document: CanvasDocument) => invoke<{ savedAt?: string; sourcePath?: string; sourceError?: string }>('SaveBoard', document);
export const getBoardSession = () => invoke<{ value?: string | null }>('GetBoardSession');
export const saveBoardSession = (value: string) => invoke<{ saved: boolean }>('SaveBoardSession', { value });
export const saveImage = (dataUrl: string) => invoke<{ imageId: string; imageUrl: string }>('SaveImage', { dataUrl });
export const getImageData = (imageId: string) => invoke<{ dataUrl: string }>('GetImageData', { imageId });
export const getWorkspaceDirectory = () => invoke<{ path: string }>('GetWorkspaceDirectory');
export const setWorkspaceDirectory = (path: string) => invoke<{ path: string }>('SetWorkspaceDirectory', { path });
export const importBoardPackage = () => invoke<{ imported: boolean; document?: CanvasDocument }>('ImportBoardPackage');
