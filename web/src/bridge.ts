import type { BackupSummary, BoardSummary, CanvasDocument, RefreshResult, ZoteroSnapshot, ZoteroStatus } from './types';

type BridgeResponse<T> = { id: string; ok: boolean; payload?: T; error?: { code: string; message: string } };
type NativeWebView = { postMessage: (value: unknown) => void; addEventListener: (type: 'message', listener: (event: MessageEvent<BridgeResponse<unknown> | string>) => void) => void; };
declare global { interface Window { chrome?: { webview?: NativeWebView }; __LITWEAVE_MOCK__?: boolean; } }

const pending = new Map<string, { resolve: (value: unknown) => void; reject: (error: Error) => void }>();
let listening = false;
const mockBoards = new Map<string, CanvasDocument>();
const mockBoardMeta = new Map<string, { isPinned: boolean; isProtected: boolean; archivedAt?: string | null; trashedAt?: string | null }>();
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
  if (type === 'ListBoards') {
    const filter = payload?.filter ?? 'all';
    const boards = [...mockBoards.values()].map(board => {
      const meta = mockBoardMeta.get(board.id) ?? { isPinned: false, isProtected: board.name === 'Operator Learning' };
      return { id: board.id, name: board.name, isLegacy: board.isLegacy, updatedAt: board.updatedAt, ...meta, nodeCount: board.nodes.length, edgeCount: board.edges.length, thumbnailImageId: board.nodes.find(node => node.imageId)?.imageId ?? null };
    }).filter(board => filter === 'all' || filter === 'active' && !board.trashedAt && !board.archivedAt || filter === 'archived' && !board.trashedAt && Boolean(board.archivedAt) || filter === 'trash' && Boolean(board.trashedAt));
    return Promise.resolve({ boards } as T);
  }
  if (type === 'CreateBoard') { const document = emptyBoard(payload?.name); mockBoards.set(document.id, document); mockBoardMeta.set(document.id, { isPinned: false, isProtected: document.name === 'Operator Learning' }); return Promise.resolve({ document } as T); }
  if (type === 'LoadBoard') return Promise.resolve({ document: mockBoards.get(payload?.boardId) ?? null } as T);
  if (type === 'SaveBoard' || type === 'SaveCanvas') { const document = payload as CanvasDocument; document.updatedAt = new Date().toISOString(); mockBoards.set(document.id, structuredClone(document)); if (!mockBoardMeta.has(document.id)) mockBoardMeta.set(document.id, { isPinned: false, isProtected: document.name === 'Operator Learning' }); return Promise.resolve({ savedAt: document.updatedAt, sourcePath: 'mock://workspace/boards' } as T); }
  if (type === 'GetBoardSession') return Promise.resolve({ value: mockSession } as T);
  if (type === 'SaveBoardSession') { mockSession = payload?.value ?? '{}'; return Promise.resolve({ saved: true } as T); }
  if (type === 'SaveImage') return Promise.resolve({ imageId: 'mock-image.png', imageUrl: 'data:image/svg+xml,%3Csvg xmlns="http://www.w3.org/2000/svg" width="180" height="120"%3E%3Crect width="100%25" height="100%25" fill="%23d9dce8"/%3E%3C/svg%3E' } as T);
  if (type === 'GetImageData') return Promise.resolve({ dataUrl: '' } as T);
  if (type === 'GetWorkspaceDirectory') return Promise.resolve({ path: 'mock://workspace' } as T);
  if (type === 'SetWorkspaceDirectory') return Promise.resolve({ path: payload?.path } as T);
  if (type === 'ImportBoardPackage') { const document = emptyBoard('Imported board'); mockBoards.set(document.id, document); return Promise.resolve({ imported: true, document } as T); }
  if (type === 'DeleteBoard') { const meta = mockBoardMeta.get(payload?.boardId); if (meta?.isProtected) return Promise.reject(new Error('这个白板已启用防删除保护，请先在白板管理中解除保护。')); if (meta) { meta.trashedAt = new Date().toISOString(); meta.archivedAt = null; } return Promise.resolve({ deleted: true, trashed: true } as T); }
  if (type === 'RestoreBoard') { const meta = mockBoardMeta.get(payload?.boardId); if (meta) { meta.trashedAt = null; meta.archivedAt = null; } return Promise.resolve({ restored: true } as T); }
  if (type === 'ArchiveBoard') { const meta = mockBoardMeta.get(payload?.boardId); if (meta) meta.archivedAt = payload?.archived ? new Date().toISOString() : null; return Promise.resolve({ archived: Boolean(payload?.archived) } as T); }
  if (type === 'SetBoardProtected') { const meta = mockBoardMeta.get(payload?.boardId); if (meta) meta.isProtected = Boolean(payload?.protected); return Promise.resolve({ isProtected: Boolean(payload?.protected) } as T); }
  if (type === 'SetBoardPinned') { const meta = mockBoardMeta.get(payload?.boardId); if (meta) meta.isPinned = Boolean(payload?.pinned); return Promise.resolve({ isPinned: Boolean(payload?.pinned) } as T); }
  if (type === 'ListBackups') return Promise.resolve({ backups: [] } as T);
  if (type === 'CreateBackup') return Promise.resolve({ backup: { id: 'mock-backup', path: 'mock://backup', kind: payload?.kind ?? 'manual', createdAt: new Date().toISOString(), boardCount: mockBoards.size, imageCount: 0, isHealthy: true, healthMessage: '校验通过' } } as T);
  if (type === 'GetAppInfo') return Promise.resolve({ version: '0.2.1-beta.1', build: 'mock', dataPath: 'mock://data', databasePath: 'mock://data/litweave.db', backupPath: 'mock://data/backups', workspacePath: 'mock://workspace' } as T);
  if (type === 'SetLanguage' || type === 'SaveCanvasExport' || type === 'ExportCanvasPdf') return Promise.resolve({ saved: true } as T);
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
export type AppState = { app: { name: string; subtitle: string; version: string }; storagePath?: string; workspacePath?: string; backupPath?: string; snapshot: ZoteroSnapshot | null; lastRefresh?: string | null; status: ZoteroStatus; capabilities?: { jsonExport?: boolean; svgExport?: boolean; pngExport?: boolean; pdfExport?: boolean; ai?: boolean; zoteroWrite?: boolean }; };
export const getAppState = () => invoke<AppState>('GetAppState');
export const checkZotero = () => invoke<ZoteroStatus>('CheckZotero');
export const refreshZotero = () => invoke<RefreshResult>('RefreshZotero');
export const listBoards = (filter = 'all') => invoke<{ boards: BoardSummary[] }>('ListBoards', { filter });
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
export const boardAction = (type: 'DeleteBoard' | 'RestoreBoard' | 'ArchiveBoard' | 'SetBoardProtected' | 'SetBoardPinned', boardId: string, value?: boolean) => {
  const payload = type === 'ArchiveBoard' ? { boardId, archived: Boolean(value) } : type === 'SetBoardProtected' ? { boardId, protected: Boolean(value) } : type === 'SetBoardPinned' ? { boardId, pinned: Boolean(value) } : { boardId };
  return invoke<Record<string, boolean>>(type, payload);
};
export const listBackups = () => invoke<{ backups: BackupSummary[] }>('ListBackups');
export const createBackup = (kind = 'manual') => invoke<{ backup: BackupSummary }>('CreateBackup', { kind });
export const restoreBackup = (path: string, destinationRoot: string) => invoke<{ path: string }>('RestoreBackup', { path, destinationRoot });
export const getAppInfo = () => invoke<{ version: string; build: string; dataPath: string; databasePath: string; backupPath: string; workspacePath: string }>('GetAppInfo');
