import {
  addEdge,
  applyEdgeChanges,
  applyNodeChanges,
  Background,
  Controls,
  Handle,
  MarkerType,
  MiniMap,
  Position,
  ReactFlow,
  ReactFlowProvider,
  useReactFlow,
  type Connection,
  type EdgeChange,
  type NodeChange,
  type NodeProps,
  type NodeTypes,
  type EdgeTypes,
  type Node,
  type Edge,
} from '@xyflow/react';
import { BookOpen, ChevronDown, ChevronRight, FolderOpen, GitBranch, Languages, Maximize2, MoreHorizontal, Plus, Redo2, RefreshCw, Search, Settings2, StickyNote, Trash2, Undo2, Upload, X, Zap } from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState, type MouseEvent as ReactMouseEvent } from 'react';
import '@xyflow/react/dist/style.css';
import { getAppState, invoke, loadCanvas, refreshZotero, saveCanvas, type AppState } from './bridge';
import type {
  CanvasDocument, CanvasEdgeRecord, CanvasNodeRecord, LitFlowEdge, LitFlowNode, PaperNodeData,
  RefreshDiff, ZoteroCollection, ZoteroItem, ZoteroSnapshot, ZoteroStatus,
} from './types';
import './styles.css';

type Language = 'zh-CN' | 'en-US';
type MenuState = { x: number; y: number; nodeId: string; itemKey?: string } | null;
type DialogState =
  | { kind: 'edge'; edgeId?: string; source: string; target: string; label: string; note: string; directed: boolean; color: string; lineStyle: 'solid' | 'dashed' | 'dotted' }
  | { kind: 'text'; text: string }
  | null;

const labels: Record<Language, Record<string, string>> = {
  'zh-CN': {
    search: '搜索题目、作者、DOI…', collections: 'Zotero 文件夹', library: '全部文库', recursive: '递归显示子文件夹', direct: '仅直接文献', refresh: '刷新 Zotero', refreshing: '正在刷新…', review: '刷新审核', addAll: '全部加入画布', add: '加入', noNew: '没有待审核的新文献', offline: 'Zotero 未连接', ready: '已连接', lastRefresh: '上次刷新', emptyTitle: '选择一个文件夹开始编织', emptyBody: '左侧选择 Collection，文献卡片会在右侧画布中展开。所有连线和文字只保存在 LitWeave。', connect: '拖动连接点建立关联', note: '新建文字', export: '导出', openItem: '在 Zotero 中定位条目', openPdf: '在 Zotero 中打开 PDF', noPdf: '没有 PDF 附件', remove: '从画布移除', addRelation: 'Add Relation', relationHint: '请选择另一张文献卡片', alias: 'Alias', ghost: 'Ghost', outScope: 'Out of Zotero Scope', papers: '篇文献', collectionsCount: '个文件夹', saved: '已保存', addEdge: '建立关联', edgeLabel: '关系标签', edgeNote: '详细备注（可选）', edgeColor: '线条颜色', directed: '有向边', save: '保存', cancel: '取消', close: '关闭', language: '语言', demo: '脱敏演示数据', allItems: '文献', online: '本地 API 正常', baseline: '已建立刷新基线', newItems: '新增', updatedItems: '更新', removedItems: '移除', select: '选择', exportJson: '导出 JSON', exportSvg: '导出 SVG', aiOff: 'AI 建议（v0.1 未启用）', noSelection: '尚未选择文件夹', libraryScope: '全部文库范围', addText: '添加文字卡片', deleteEdge: '删除关联', dotted: '点线', dashed: '虚线', solid: '实线', directHint: '仅显示当前文件夹直接文献', recursiveHint: '包含所有子文件夹文献',
  },
  'en-US': {
    search: 'Search title, author, DOI…', collections: 'Zotero Collections', library: 'All library', recursive: 'Include nested Collections', direct: 'Direct items only', refresh: 'Refresh Zotero', refreshing: 'Refreshing…', review: 'Refresh Review', addAll: 'Add all to canvas', add: 'Add', noNew: 'No new items to review', offline: 'Zotero offline', ready: 'Connected', lastRefresh: 'Last refresh', emptyTitle: 'Choose a Collection to begin weaving', emptyBody: 'Select a Collection on the left. Cards will fan out on the canvas; relationships and notes stay in LitWeave.', connect: 'Drag a handle to create a relationship', note: 'New text', export: 'Export', openItem: 'Show item in Zotero', openPdf: 'Open PDF in Zotero', noPdf: 'No PDF attachment', remove: 'Remove from canvas', addRelation: 'Add Relation', relationHint: 'Select another paper card', alias: 'Alias', ghost: 'Ghost', outScope: 'Out of Zotero Scope', papers: 'papers', collectionsCount: 'collections', saved: 'Saved', addEdge: 'Create relationship', edgeLabel: 'Relationship label', edgeNote: 'Detailed note (optional)', edgeColor: 'Line color', directed: 'Directed edge', save: 'Save', cancel: 'Cancel', close: 'Close', language: 'Language', demo: 'Anonymised demo data', allItems: 'Items', online: 'Local API ready', baseline: 'Refresh baseline created', newItems: 'New', updatedItems: 'Updated', removedItems: 'Removed', select: 'Select', exportJson: 'Export JSON', exportSvg: 'Export SVG', aiOff: 'AI suggestions (disabled in v0.1)', noSelection: 'No Collection selected', libraryScope: 'All library scope', addText: 'Add text card', deleteEdge: 'Delete relationship', dotted: 'Dotted', dashed: 'Dashed', solid: 'Solid', directHint: 'Only direct items in this Collection', recursiveHint: 'Include items in nested Collections',
  },
};

function useText(language: Language) { return (key: string) => labels[language][key] ?? key; }

function getName(collection: ZoteroCollection | undefined, fallback: string) { return collection?.name || fallback; }

function scopeKeys(snapshot: ZoteroSnapshot, root: string, recursive: boolean): Set<string> {
  if (!root) return new Set(snapshot.collections.filter(c => recursive || !c.parentKey).map(c => c.key));
  const keys = new Set([root]);
  if (!recursive) return keys;
  let changed = true;
  while (changed) {
    changed = false;
    for (const collection of snapshot.collections) {
      if (collection.parentKey && keys.has(collection.parentKey) && !keys.has(collection.key)) { keys.add(collection.key); changed = true; }
    }
  }
  return keys;
}

function visibleItems(snapshot: ZoteroSnapshot, root: string, recursive: boolean): ZoteroItem[] {
  const keys = scopeKeys(snapshot, root, recursive);
  if (!root) return snapshot.items.filter(item => recursive || item.collectionKeys.some(k => keys.has(k)));
  return snapshot.items.filter(item => item.collectionKeys.some(k => keys.has(k)));
}

function createInitialDocument(snapshot: ZoteroSnapshot, root: string, recursive: boolean): CanvasDocument {
  const visible = scopeKeys(snapshot, root, recursive);
  const collections = snapshot.collections.filter(c => visible.has(c.key));
  const children = new Map<string, ZoteroCollection[]>();
  for (const collection of collections) {
    const parent = collection.parentKey && visible.has(collection.parentKey) ? collection.parentKey : '__root__';
    const list = children.get(parent) ?? []; list.push(collection); children.set(parent, list);
  }
  const groupLayout = new Map<string, { x: number; y: number; width: number; height: number; depth: number }>();
  const roots = children.get('__root__') ?? [];
  const layoutGroup = (collection: ZoteroCollection, x: number, y: number, depth: number) => {
    const childList = children.get(collection.key) ?? [];
    const directCount = snapshot.items.filter(item => item.collectionKeys.includes(collection.key)).length;
    const height = Math.max(280, Math.ceil(Math.max(1, directCount) / 2) * 186 + 94 + childList.length * 18);
    const entry = { x, y, width: 474, height: Math.max(height, 330 + childList.length * 320), depth }; groupLayout.set(collection.key, entry);
    const childY = y + 64 + Math.ceil(Math.max(1, directCount) / 2) * 186 + 20;
    childList.forEach((child, index) => layoutGroup(child, x + 22 + (depth % 2) * 12, childY + index * 320, depth + 1));
  };
  roots.forEach((collection, index) => layoutGroup(collection, (index % 2) * 530, Math.floor(index / 2) * 520, 0));
  const items = visibleItems(snapshot, root, recursive);
  const nodes: CanvasNodeRecord[] = [];
  for (const [key, position] of groupLayout) {
    const collection = snapshot.collections.find(c => c.key === key);
    nodes.push({ id: `group:${key}`, kind: 'group', collectionKey: key, title: collection?.name ?? key, x: position.x, y: position.y, width: position.width, height: position.height });
  }
  if (!root && items.some(item => item.collectionKeys.length === 0)) {
    groupLayout.set('__library__', { x: 0, y: Math.max(520, Math.ceil(roots.length / 2) * 520), width: 474, height: 320, depth: 0 });
    nodes.push({ id: 'group:__library__', kind: 'group', collectionKey: '__library__', title: 'Unfiled in Zotero', x: 0, y: Math.max(520, Math.ceil(roots.length / 2) * 520), width: 474, height: 320 });
  }
  const counters = new Map<string, number>();
  for (const item of items) {
    const targets = item.collectionKeys.filter(key => visible.has(key));
    if (!targets.length && !root) targets.push('__library__');
    for (const collectionKey of targets) {
      const group = groupLayout.get(collectionKey); if (!group) continue;
      const count = counters.get(collectionKey) ?? 0; counters.set(collectionKey, count + 1);
      nodes.push({ id: `paper:${item.key}:${collectionKey}`, kind: 'paper', itemKey: item.key, collectionKey, title: item.title, x: group.x + 22 + (count % 2) * 230, y: group.y + 64 + Math.floor(count / 2) * 184, width: 214, height: 156, isAlias: targets.length > 1 });
    }
  }
  return { id: `personal:${root || 'library'}`, rootCollectionKey: root, recursive, layoutVersion: 1, nodes, edges: [], textNodes: [], viewportX: 0, viewportY: 0, viewportZoom: 1, updatedAt: new Date().toISOString() };
}

function recordToFlowNode(record: CanvasNodeRecord, snapshot: ZoteroSnapshot | null, onContextMenu: (event: ReactMouseEvent, node: Node<PaperNodeData>) => void): LitFlowNode {
  if (record.kind === 'group') return { id: record.id, type: 'collectionGroup', position: { x: record.x, y: record.y }, draggable: false, selectable: false, connectable: false, zIndex: -1, style: { width: record.width, height: record.height }, data: { label: record.title ?? record.collectionKey ?? 'Collection', collectionKey: record.collectionKey ?? '', depth: 0 } } as LitFlowNode;
  if (record.kind === 'text') return { id: record.id, type: 'textCard', position: { x: record.x, y: record.y }, style: { width: record.width, height: record.height }, data: { text: record.text ?? '' } } as LitFlowNode;
  const item = snapshot?.items.find(i => i.key === record.itemKey) ?? null;
  return { id: record.id, type: 'paperCard', position: { x: record.x, y: record.y }, style: { width: record.width, height: record.height }, data: { item, itemKey: record.itemKey ?? '', collectionKey: record.collectionKey, isAlias: Boolean(record.isAlias), isGhost: Boolean(record.isGhost) || !item, isOutOfScope: Boolean(record.isOutOfScope), onContextMenu } } as LitFlowNode;
}

function documentToFlow(document: CanvasDocument, snapshot: ZoteroSnapshot | null, onContextMenu: (event: ReactMouseEvent, node: Node<PaperNodeData>) => void) {
  const records = [...document.nodes, ...document.textNodes.map(t => ({ id: t.id, kind: 'text' as const, text: t.text, x: t.x, y: t.y, width: t.width, height: t.height }))];
  return {
    nodes: records.map(record => recordToFlowNode(record, snapshot, onContextMenu)),
    edges: document.edges.map(edge => ({ id: edge.id, source: edge.source, target: edge.target, label: edge.label ?? '', type: 'smoothstep', markerEnd: edge.isDirected ? { type: MarkerType.ArrowClosed, color: edge.color } : undefined, style: { stroke: edge.color, strokeWidth: 2, strokeDasharray: edge.lineStyle === 'dashed' ? '8 5' : edge.lineStyle === 'dotted' ? '2 6' : undefined }, labelStyle: { fill: edge.color, fontWeight: 700 }, data: { note: edge.note, color: edge.color, isDirected: edge.isDirected } })) as LitFlowEdge[],
  };
}

function flowToDocument(nodes: LitFlowNode[], edges: LitFlowEdge[], root: string, recursive: boolean, viewport: { x: number; y: number; zoom: number }): CanvasDocument {
  const records: CanvasNodeRecord[] = [];
  const textNodes: CanvasDocument['textNodes'] = [];
  for (const node of nodes) {
    const width = typeof node.style?.width === 'number' ? node.style.width : 220;
    const height = typeof node.style?.height === 'number' ? node.style.height : 150;
    if (node.type === 'textCard') { const data = node.data as { text: string }; textNodes.push({ id: node.id, text: data.text, x: node.position.x, y: node.position.y, width, height }); continue; }
    const data = node.data as { label?: string; collectionKey?: string; itemKey?: string; item?: ZoteroItem | null; isAlias?: boolean; isGhost?: boolean; isOutOfScope?: boolean };
    records.push({ id: node.id, kind: node.type === 'collectionGroup' ? 'group' : 'paper', itemKey: data.itemKey, collectionKey: data.collectionKey, title: node.type === 'collectionGroup' ? data.label : data.item?.title, x: node.position.x, y: node.position.y, width, height, isAlias: data.isAlias, isGhost: data.isGhost, isOutOfScope: data.isOutOfScope });
  }
  const edgeRecords: CanvasEdgeRecord[] = edges.map(edge => ({ id: edge.id, source: edge.source, target: edge.target, label: typeof edge.label === 'string' ? edge.label : '', note: edge.data?.note, isDirected: edge.data?.isDirected ?? Boolean(edge.markerEnd), color: edge.data?.color ?? '#7C9CFF', lineStyle: edge.style?.strokeDasharray === '8 5' ? 'dashed' : edge.style?.strokeDasharray === '2 6' ? 'dotted' : 'solid' }));
  return { id: `personal:${root || 'library'}`, rootCollectionKey: root, recursive, layoutVersion: 1, nodes: records, edges: edgeRecords, textNodes, viewportX: viewport.x, viewportY: viewport.y, viewportZoom: viewport.zoom, updatedAt: new Date().toISOString() };
}

function GroupFrame({ data }: NodeProps<Node<{ label: string; collectionKey: string; depth: number }>>) {
  return <div className="group-frame"><div className="group-frame-title"><FolderOpen size={15} /><span>{data.label}</span></div><span className="group-key">{data.collectionKey === '__library__' ? 'Zotero' : data.collectionKey}</span></div>;
}

function PaperCard({ id, data, selected }: NodeProps<Node<PaperNodeData>>) {
  const item = data.item;
  const title = (item?.title ?? data.itemKey) || 'Deleted Zotero item';
  return <div className={`paper-card ${selected ? 'selected' : ''} ${data.isGhost ? 'ghost' : ''} ${data.isOutOfScope ? 'out-scope' : ''}`} onContextMenu={(event) => data.onContextMenu?.(event, { id, data } as Node<PaperNodeData>)}>
    <Handle type="target" position={Position.Top} className="flow-handle" />
    <div className="paper-card-top"><BookOpen size={15} /><div className="paper-badges">{data.isAlias && <span className="badge alias">Alias</span>}{data.isGhost && <span className="badge ghost-badge">Ghost</span>}{data.isOutOfScope && <span className="badge scope-badge">Out of scope</span>}</div></div>
    <div className="paper-title" title={title}>{title}</div>
    {item ? <>
      <div className="paper-author">{item.firstAuthor || item.creators?.[0]?.displayName || item.creators?.[0]?.name || '—'}{item.correspondingAuthor ? <span className="corresponding"> · {item.correspondingAuthor}</span> : null}</div>
      <div className="paper-meta"><span>{item.year || '—'}</span><span>{item.publicationTitle || 'No journal'}</span><span className={item.hasPdf ? 'pdf-ok' : 'pdf-none'}>{item.hasPdf ? 'PDF' : 'No PDF'}</span></div>
    </> : <div className="paper-author">This item is no longer in the Zotero snapshot.</div>}
    <Handle type="source" position={Position.Bottom} className="flow-handle" />
  </div>;
}

function TextCard({ data }: NodeProps<Node<{ text: string }>>) { return <div className="text-card"><StickyNote size={15} /><div>{data.text || 'Text note'}</div></div>; }

const nodeTypes: NodeTypes = { collectionGroup: GroupFrame, paperCard: PaperCard, textCard: TextCard };
const edgeTypes: EdgeTypes = {};

function AppInner() {
  const [language, setLanguage] = useState<Language>(() => navigator.language.toLowerCase().startsWith('zh') ? 'zh-CN' : 'en-US');
  const t = useText(language);
  const [snapshot, setSnapshot] = useState<ZoteroSnapshot | null>(null);
  const [status, setStatus] = useState<ZoteroStatus | null>(null);
  const [selectedCollection, setSelectedCollection] = useState('');
  const [recursive, setRecursive] = useState(true);
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const [query, setQuery] = useState('');
  const [flowNodes, setFlowNodes] = useState<LitFlowNode[]>([]);
  const [flowEdges, setFlowEdges] = useState<LitFlowEdge[]>([]);
  const [canvas, setCanvas] = useState<CanvasDocument | null>(null);
  const [reviewKeys, setReviewKeys] = useState<string[]>([]);
  const [reviewOpen, setReviewOpen] = useState(true);
  const [reviewSelected, setReviewSelected] = useState<Set<string>>(new Set());
  const [lastDiff, setLastDiff] = useState<RefreshDiff | null>(null);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [lastRefresh, setLastRefresh] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [menu, setMenu] = useState<MenuState>(null);
  const [dialog, setDialog] = useState<DialogState>(null);
  const [relationSource, setRelationSource] = useState<string | null>(null);
  const [selectedNode, setSelectedNode] = useState<string | null>(null);
  const canvasRef = useRef<CanvasDocument | null>(null);
  const reviewRef = useRef(new Set<string>());
  const saveTimer = useRef<number | undefined>(undefined);
  const past = useRef<Array<{ nodes: LitFlowNode[]; edges: LitFlowEdge[] }>>([]);
  const future = useRef<Array<{ nodes: LitFlowNode[]; edges: LitFlowEdge[] }>>([]);
  const [, redrawHistory] = useState(0);
  const { fitView } = useReactFlow();
  useEffect(() => { canvasRef.current = canvas; }, [canvas]);
  useEffect(() => { reviewRef.current = new Set(reviewKeys); }, [reviewKeys]);

  const onPaperContextMenu = useCallback((event: ReactMouseEvent, node: Node<PaperNodeData>) => { event.preventDefault(); setMenu({ x: event.clientX, y: event.clientY, nodeId: node.id, itemKey: node.data.itemKey }); }, []);
  const syncCanvas = useCallback((nodes: LitFlowNode[], edges: LitFlowEdge[], viewport = { x: canvasRef.current?.viewportX ?? 0, y: canvasRef.current?.viewportY ?? 0, zoom: canvasRef.current?.viewportZoom ?? 1 }) => {
    if (!canvasRef.current) return;
    const next = flowToDocument(nodes, edges, canvasRef.current.rootCollectionKey, canvasRef.current.recursive, viewport);
    canvasRef.current = next; setCanvas(next);
    if (saveTimer.current) window.clearTimeout(saveTimer.current);
    saveTimer.current = window.setTimeout(() => { void saveCanvas(next).catch((e: Error) => setError(e.message)); }, 380);
  }, []);
  const recordHistory = useCallback((nextNodes: LitFlowNode[], nextEdges: LitFlowEdge[]) => {
    past.current.push({ nodes: flowNodes, edges: flowEdges });
    if (past.current.length > 80) past.current.shift();
    future.current = [];
    redrawHistory(value => value + 1);
    setFlowNodes(nextNodes); setFlowEdges(nextEdges); syncCanvas(nextNodes, nextEdges);
  }, [flowEdges, flowNodes, syncCanvas]);
  const undo = () => {
    const previous = past.current.pop(); if (!previous) return;
    future.current.push({ nodes: flowNodes, edges: flowEdges }); setFlowNodes(previous.nodes); setFlowEdges(previous.edges); syncCanvas(previous.nodes, previous.edges); redrawHistory(value => value + 1);
  };
  const redo = () => {
    const next = future.current.pop(); if (!next) return;
    past.current.push({ nodes: flowNodes, edges: flowEdges }); setFlowNodes(next.nodes); setFlowEdges(next.edges); syncCanvas(next.nodes, next.edges); redrawHistory(value => value + 1);
  };
  const loadCurrentCanvas = useCallback(async (root: string, recursiveMode: boolean, currentSnapshot: ZoteroSnapshot | null) => {
    if (!currentSnapshot) { setCanvas(null); setFlowNodes([]); setFlowEdges([]); return; }
    try {
      const loaded = await loadCanvas(root);
      let document = loaded.document;
      if (!document) {
        document = createInitialDocument(currentSnapshot, root, recursiveMode);
        await saveCanvas(document);
      } else {
        document.recursive = recursiveMode;
        const visible = scopeKeys(currentSnapshot, root, recursiveMode);
        document.nodes = document.nodes.filter(node => node.kind === 'text' || (node.kind === 'group' ? Boolean(node.collectionKey && visible.has(node.collectionKey)) : Boolean(node.collectionKey && visible.has(node.collectionKey))));
        for (const node of document.nodes) {
          if (node.kind === 'paper') { const item = currentSnapshot.items.find(i => i.key === node.itemKey); node.isGhost = !item; node.isOutOfScope = Boolean(item && !item.collectionKeys.some(key => visible.has(key))); }
        }
      }
      const flows = documentToFlow(document, currentSnapshot, onPaperContextMenu);
      canvasRef.current = document; setCanvas(document); setFlowNodes(flows.nodes); setFlowEdges(flows.edges); setError(null);
      window.setTimeout(() => fitView({ padding: 0.18, duration: 280 }), 40);
    } catch (e) { setError((e as Error).message); }
  }, [fitView, onPaperContextMenu]);

  useEffect(() => { void getAppState().then((state: AppState) => { setSnapshot(state.snapshot); setStatus(state.status); setLastRefresh(state.lastRefresh ?? null); }).catch((e: Error) => setError(e.message)); }, []);
  useEffect(() => { if (snapshot) void loadCurrentCanvas(selectedCollection, recursive, snapshot); }, [selectedCollection]); // Collection clicks are explicit; recursive uses its own handler.

  const collections = snapshot?.collections ?? [];
  const collectionMap = useMemo(() => new Map(collections.map(collection => [collection.key, collection])), [collections]);
  const childMap = useMemo(() => { const map = new Map<string, ZoteroCollection[]>(); for (const collection of collections) { const parent = collection.parentKey || '__root__'; const list = map.get(parent) ?? []; list.push(collection); map.set(parent, list); } for (const list of map.values()) list.sort((a, b) => a.name.localeCompare(b.name)); return map; }, [collections]);
  const currentScope = useMemo(() => scopeKeys(snapshot ?? { collections: [], items: [], libraryKey: 'personal', refreshedAt: '', contentHash: '' }, selectedCollection, recursive), [snapshot, selectedCollection, recursive]);
  const filteredItems = useMemo(() => { const items = snapshot?.items.filter(item => !selectedCollection || item.collectionKeys.some(key => currentScope.has(key))) ?? []; const normalized = query.trim().toLowerCase(); return normalized ? items.filter(item => [item.title, item.firstAuthor, item.correspondingAuthor, item.doi, item.publicationTitle].some(value => value?.toLowerCase().includes(normalized))) : items; }, [snapshot, selectedCollection, currentScope, query]);
  const reviewItems = useMemo(() => (snapshot?.items ?? []).filter(item => reviewKeys.includes(item.key)), [snapshot, reviewKeys]);

  const setCollection = (key: string) => { setSelectedCollection(key); setRelationSource(null); };
  const toggleExpanded = (key: string) => setExpanded(previous => { const next = new Set(previous); if (next.has(key)) next.delete(key); else next.add(key); return next; });
  const renderCollection = (collection: ZoteroCollection, depth: number) => { const children = childMap.get(collection.key) ?? []; const isOpen = expanded.has(collection.key) || depth === 0; return <div key={collection.key}><button className={`collection-row ${selectedCollection === collection.key ? 'active' : ''}`} style={{ paddingLeft: 12 + depth * 16 }} onClick={() => setCollection(collection.key)}><span className="tree-toggle" onClick={(event) => { event.stopPropagation(); toggleExpanded(collection.key); }}>{children.length ? (isOpen ? <ChevronDown size={14} /> : <ChevronRight size={14} />) : <span className="tree-spacer" />}</span><FolderOpen size={15} /><span className="collection-name" title={collection.name}>{collection.name}</span><span className="collection-count">{collection.itemCount ?? ''}</span></button>{isOpen && children.map(child => renderCollection(child, depth + 1))}</div>; };

  const reconcileAfterRefresh = (nextSnapshot: ZoteroSnapshot, diff: RefreshDiff) => {
    setSnapshot(nextSnapshot); setStatus({ isRunning: true, apiEnabled: true, message: t('online'), checkedAt: new Date().toISOString() }); setLastRefresh(nextSnapshot.refreshedAt); setLastDiff(diff); setReviewKeys(previous => diff.isBaseline ? [] : [...new Set([...previous, ...diff.newItemKeys])]);
    if (!canvasRef.current) { void loadCurrentCanvas(selectedCollection, recursive, nextSnapshot); return; }
    const nextNodes = flowNodes.map(node => { if (node.type !== 'paperCard') return node; const data = node.data as PaperNodeData; const item = nextSnapshot.items.find(i => i.key === data.itemKey) ?? null; return { ...node, data: { ...data, item, isGhost: !item, isOutOfScope: Boolean(item && selectedCollection && !item.collectionKeys.some(key => currentScope.has(key))) } }; });
    setFlowNodes(nextNodes); syncCanvas(nextNodes, flowEdges);
  };
  const handleRefresh = async () => { setIsRefreshing(true); setError(null); try { const result = await refreshZotero(); reconcileAfterRefresh(result.snapshot, result.diff); } catch (e) { setError((e as Error).message); setStatus({ isRunning: false, apiEnabled: false, message: t('offline'), checkedAt: new Date().toISOString() }); } finally { setIsRefreshing(false); } };

  const addItems = (itemsToAdd: ZoteroItem[]) => {
    if (!snapshot || !canvasRef.current || !itemsToAdd.length) return;
    let nextNodes = [...flowNodes]; let paperCount = nextNodes.filter(node => node.type === 'paperCard').length; const addedKeys: string[] = []; let focusId: string | undefined;
    for (const item of itemsToAdd) {
      const existing = nextNodes.find(node => node.type === 'paperCard' && (node.data as PaperNodeData).itemKey === item.key);
      if (existing) { focusId = existing.id; addedKeys.push(item.key); continue; }
      const target = item.collectionKeys.find(key => currentScope.has(key)) ?? (selectedCollection || item.collectionKeys[0]);
      const group = nextNodes.find(node => node.type === 'collectionGroup' && (node.data as { collectionKey?: string }).collectionKey === target);
      const record: CanvasNodeRecord = { id: `paper:${item.key}:${target || 'library'}`, kind: 'paper', itemKey: item.key, collectionKey: target || '__library__', title: item.title, x: group ? group.position.x + 22 + (paperCount % 2) * 230 : paperCount * 28, y: group ? group.position.y + 64 + Math.floor(paperCount / 2) * 184 : paperCount * 28, width: 214, height: 156, isAlias: item.collectionKeys.length > 1 };
      nextNodes = [...nextNodes, recordToFlowNode(record, snapshot, onPaperContextMenu)]; paperCount += 1; addedKeys.push(item.key);
    }
    if (focusId) nextNodes = nextNodes.map(node => ({ ...node, selected: node.id === focusId }));
    if (nextNodes.length !== flowNodes.length || focusId) recordHistory(nextNodes, flowEdges);
    setReviewKeys(keys => keys.filter(key => !addedKeys.includes(key))); setReviewSelected(keys => { const next = new Set(keys); addedKeys.forEach(key => next.delete(key)); return next; });
    if (focusId) { setSelectedNode(focusId); window.setTimeout(() => fitView({ nodes: [{ id: focusId! }], padding: 0.45, duration: 300 }), 30); }
  };
  const addItem = (itemKey: string) => { const item = snapshot?.items.find(i => i.key === itemKey); if (item) addItems([item]); };
  const addAll = () => addItems(reviewItems);
  const addSelected = () => addItems(reviewItems.filter(item => reviewSelected.has(item.key)));

  const beginEdge = (source: string, target: string) => {
    if (source === target) { setError('The same Alias cannot connect to itself.'); return; }
    const sourceItem = flowNodes.find(node => node.id === source)?.data as PaperNodeData | undefined; const targetItem = flowNodes.find(node => node.id === target)?.data as PaperNodeData | undefined;
    if (!sourceItem || !targetItem || sourceItem.itemKey === targetItem.itemKey) { setError('The same Zotero item cannot connect to one of its Alias instances.'); return; }
    setDialog({ kind: 'edge', source, target, label: 'Reference', note: '', directed: true, color: '#7C9CFF', lineStyle: 'solid' }); setRelationSource(null);
  };
  const onConnect = (connection: Connection) => { if (connection.source && connection.target) beginEdge(connection.source, connection.target); };
  const saveDialog = () => {
    if (!dialog) return;
    if (dialog.kind === 'text') { const id = `text:${crypto.randomUUID()}`; const node: LitFlowNode = { id, type: 'textCard', position: { x: 180 + flowNodes.length * 18, y: 180 + flowNodes.length * 18 }, data: { text: dialog.text || 'Text note' }, style: { width: 220, height: 100 } }; const nextNodes = [...flowNodes, node]; recordHistory(nextNodes, flowEdges); setDialog(null); return; }
    const edge: LitFlowEdge = { id: dialog.edgeId ?? `edge:${crypto.randomUUID()}`, source: dialog.source, target: dialog.target, label: dialog.label || '', type: 'smoothstep', markerEnd: dialog.directed ? { type: MarkerType.ArrowClosed, color: dialog.color } : undefined, style: { stroke: dialog.color, strokeWidth: 2, strokeDasharray: dialog.lineStyle === 'dashed' ? '8 5' : dialog.lineStyle === 'dotted' ? '2 6' : undefined }, labelStyle: { fill: dialog.color, fontWeight: 700 }, data: { note: dialog.note, color: dialog.color, isDirected: dialog.directed } };
    const nextEdges = dialog.edgeId ? flowEdges.map(existing => existing.id === dialog.edgeId ? edge : existing) : addEdge(edge, flowEdges); recordHistory(flowNodes, nextEdges); setDialog(null);
  };
  const deleteEdge = (edgeId: string) => { const nextEdges = flowEdges.filter(edge => edge.id !== edgeId); recordHistory(flowNodes, nextEdges); setDialog(null); };
  const removeNode = (nodeId: string) => { const nextNodes = flowNodes.filter(node => node.id !== nodeId); const nextEdges = flowEdges.filter(edge => edge.source !== nodeId && edge.target !== nodeId); recordHistory(nextNodes, nextEdges); setMenu(null); };
  const updateEdges = (changes: EdgeChange<LitFlowEdge>[]) => { const next = applyEdgeChanges(changes, flowEdges); recordHistory(flowNodes, next); };
  const updateNodes = (changes: NodeChange<LitFlowNode>[]) => { const next = applyNodeChanges(changes, flowNodes); recordHistory(next, flowEdges); };
  const onNodeClick = (_event: ReactMouseEvent, node: LitFlowNode) => { setSelectedNode(node.id); setMenu(null); if (relationSource && node.type === 'paperCard') beginEdge(relationSource, node.id); };
  const openItem = async (itemKey: string) => { await invoke('OpenZoteroItem', { itemKey }).catch((e: Error) => setError(e.message)); setMenu(null); };
  const openPdf = async (attachmentKey: string) => { await invoke('OpenZoteroPdf', { attachmentKey }).catch((e: Error) => setError(e.message)); setMenu(null); };
  const exportCanvas = async (format: 'json' | 'svg' | 'png') => { if (!canvasRef.current) return; if (format === 'png') { await invoke('CaptureCanvasPng', {}).catch((e: Error) => setError(e.message)); return; } const document = flowToDocument(flowNodes, flowEdges, canvasRef.current.rootCollectionKey, canvasRef.current.recursive, { x: canvasRef.current.viewportX, y: canvasRef.current.viewportY, zoom: canvasRef.current.viewportZoom }); const content = format === 'json' ? JSON.stringify(document, null, 2) : `<svg xmlns="http://www.w3.org/2000/svg" width="1600" height="1000" viewBox="0 0 1600 1000"><rect width="100%" height="100%" fill="#0c101a"/>${flowNodes.filter(n => n.type !== 'collectionGroup').map(n => `<rect x="${n.position.x}" y="${n.position.y}" width="${n.style?.width ?? 220}" height="${n.style?.height ?? 150}" rx="12" fill="#182238" stroke="#7c9cff"/><text x="${n.position.x + 12}" y="${n.position.y + 28}" fill="#f5f7ff" font-family="Segoe UI, sans-serif" font-size="14">${escapeXml((n.data as PaperNodeData).item?.title ?? (n.data as { text?: string }).text ?? '')}</text>`).join('')}</svg>`; await invoke('SaveCanvasExport', { format, content, defaultName: `litweave-${selectedCollection || 'library'}` }).catch((e: Error) => setError(e.message)); };
  const onViewportChange = (_event: unknown, viewport: { x: number; y: number; zoom: number }) => { if (canvasRef.current) { canvasRef.current.viewportX = viewport.x; canvasRef.current.viewportY = viewport.y; canvasRef.current.viewportZoom = viewport.zoom; syncCanvas(flowNodes, flowEdges, viewport); } };
  const openRecursive = (next: boolean) => { setRecursive(next); if (snapshot) void loadCurrentCanvas(selectedCollection, next, snapshot); };

  return <div className="app-shell" onClick={() => { if (menu) setMenu(null); }}>
    <aside className="sidebar">
      <div className="brand"><div className="brand-mark"><GitBranch size={19} /></div><div><div className="brand-name">LitWeave</div><div className="brand-subtitle">Visual Literature Mapping</div></div><span className="version">v0.1</span></div>
      <div className="search-box"><Search size={16} /><input value={query} onChange={event => setQuery(event.target.value)} placeholder={t('search')} /><kbd>⌘ K</kbd></div>
      <div className="sidebar-scroll">
        <div className="section-heading"><span>{t('collections')}</span><span className="muted-count">{collections.length}</span></div>
        <button className={`library-row ${!selectedCollection ? 'active' : ''}`} onClick={() => setCollection('')}><BookOpen size={16} /><span>{t('library')}</span><span className="collection-count">{snapshot?.items.length ?? ''}</span></button>
        <div className="collection-tree">{(childMap.get('__root__') ?? []).map(collection => renderCollection(collection, 0))}</div>
        {!snapshot && <div className="empty-sidebar"><Zap size={18} /><div>{t('offline')}</div><small>{t('emptyBody')}</small></div>}
      </div>
      <div className="sidebar-footer">
        <button className="refresh-button" onClick={handleRefresh} disabled={isRefreshing}><RefreshCw size={16} className={isRefreshing ? 'spin' : ''} /><span>{isRefreshing ? t('refreshing') : t('refresh')}</span></button>
        <div className={`zotero-status ${status?.apiEnabled ? 'online' : 'offline'}`}><span className="status-dot" /><span>{status?.apiEnabled ? t('ready') : t('offline')}</span><span className="status-detail" title={status?.message}>{status?.apiEnabled ? 'API v3' : '—'}</span></div>
        <div className="last-refresh">{t('lastRefresh')}: {lastRefresh ? new Date(lastRefresh).toLocaleString(language) : '—'}</div>
        <div className="sidebar-actions"><button onClick={() => setLanguage(language === 'zh-CN' ? 'en-US' : 'zh-CN')} title={t('language')}><Languages size={15} />{language}</button><button onClick={() => setError(t('aiOff'))} title={t('aiOff')}><Settings2 size={15} /></button></div>
      </div>
    </aside>
    <main className="workspace">
      <header className="workspace-header"><div className="scope-title"><span className="eyebrow">{selectedCollection ? t('collections') : t('libraryScope')}</span><h1>{selectedCollection ? getName(collectionMap.get(selectedCollection), selectedCollection) : t('library')}</h1></div><div className="header-actions"><label className="toggle"><input type="checkbox" checked={recursive} onChange={event => openRecursive(event.target.checked)} /><span className="toggle-track" /><span>{recursive ? t('recursive') : t('direct')}</span></label><button className="ghost-button" onClick={() => setDialog({ kind: 'text', text: '' })}><StickyNote size={16} />{t('note')}</button><div className="export-menu"><button className="ghost-button" onClick={() => void exportCanvas('svg')}><Upload size={16} />{t('export')}</button><button className="icon-button" onClick={() => void exportCanvas('json')} title={t('exportJson')}><MoreHorizontal size={17} /></button><button className="icon-button" onClick={() => void exportCanvas('png')} title="PNG">PNG</button></div></div></header>
      <div className="canvas-toolbar"><div className="canvas-stats"><span>{flowNodes.filter(n => n.type === 'paperCard').length} {t('allItems')}</span><span>{flowNodes.filter(n => n.type === 'collectionGroup').length} {t('collectionsCount')}</span><span className="canvas-saved"><span className="status-dot" />{t('saved')}</span></div><div className="canvas-hint"><GitBranch size={14} />{relationSource ? t('relationHint') : t('connect')}</div><button className="icon-button" onClick={undo} disabled={!past.current.length} title="Undo"><Undo2 size={16} /></button><button className="icon-button" onClick={redo} disabled={!future.current.length} title="Redo"><Redo2 size={16} /></button><button className="icon-button" onClick={() => fitView({ padding: 0.18, duration: 300 })} title="Fit view"><Maximize2 size={16} /></button></div>
      <div className="canvas-area" onDoubleClick={event => { const target = event.target as HTMLElement; if (target.closest('.react-flow__pane')) setDialog({ kind: 'text', text: '' }); }} onDragOver={event => event.preventDefault()} onDrop={event => { event.preventDefault(); const itemKey = event.dataTransfer.getData('text/litweave-item'); if (itemKey) addItem(itemKey); }}><ReactFlow nodes={flowNodes} edges={flowEdges} nodeTypes={nodeTypes} edgeTypes={edgeTypes} onNodesChange={updateNodes} onEdgesChange={updateEdges} onConnect={onConnect} onNodeClick={onNodeClick} onNodeContextMenu={(event, node) => { if (node.type === 'paperCard') onPaperContextMenu(event, node as Node<PaperNodeData>); }} onEdgeDoubleClick={(_event, edge) => { const record = canvasRef.current?.edges.find(item => item.id === edge.id); setDialog({ kind: 'edge', edgeId: edge.id, source: edge.source, target: edge.target, label: record?.label ?? String(edge.label ?? ''), note: record?.note ?? '', directed: record?.isDirected ?? true, color: record?.color ?? '#7C9CFF', lineStyle: record?.lineStyle ?? 'solid' }); }} onPaneClick={() => { if (relationSource) setRelationSource(null); setMenu(null); }} onMoveEnd={onViewportChange} fitView={false} minZoom={0.08} maxZoom={2.5} proOptions={{ hideAttribution: true }}><Background color="#22314d" gap={28} size={1} /><Controls position="bottom-right" showInteractive={false} /><MiniMap nodeStrokeColor="#7c9cff" nodeColor="#182238" maskColor="rgba(12,16,26,.72)" /></ReactFlow>{!canvas && <div className="canvas-empty"><div className="empty-orb"><GitBranch size={28} /></div><h2>{t('emptyTitle')}</h2><p>{t('emptyBody')}</p><button className="refresh-button" onClick={handleRefresh}><RefreshCw size={16} />{t('refresh')}</button></div>}</div>
      <div className={`review-tray ${reviewOpen ? 'open' : 'closed'}`}><button className="review-header" onClick={() => setReviewOpen(open => !open)}><div><RefreshCw size={15} /><span>{t('review')}</span><span className="review-count">{reviewItems.length}</span></div>{reviewOpen ? <ChevronDown size={16} /> : <ChevronRight size={16} />}</button>{reviewOpen && <div className="review-body">{reviewItems.length ? <><div className="review-summary"><span>{reviewItems.length} {t('newItems')} · {lastDiff?.updatedItemKeys.length ?? 0} {t('updatedItems')} · {lastDiff?.removedItemKeys.length ?? 0} {t('removedItems')}</span><div className="review-summary-actions"><button onClick={addSelected} disabled={!reviewSelected.size}>{reviewSelected.size ? `${t('add')} ${reviewSelected.size}` : t('select')}</button><button onClick={addAll}>{t('addAll')}</button></div></div><div className="review-list">{reviewItems.slice(0, 30).map(item => <div className="review-item" key={item.key} draggable onDragStart={event => event.dataTransfer.setData('text/litweave-item', item.key)}><input type="checkbox" checked={reviewSelected.has(item.key)} onChange={event => setReviewSelected(previous => { const next = new Set(previous); if (event.target.checked) next.add(item.key); else next.delete(item.key); return next; })} /><div><strong title={item.title}>{item.title}</strong><small>{item.firstAuthor || '—'} · {item.year || '—'}</small></div><button onClick={() => addItem(item.key)}>{t('add')}</button></div>)}</div></> : <div className="review-empty"><span>{t('noNew')}</span>{snapshot && <small>{t('baseline')}</small>}</div>}</div>}</div>
    </main>
    {filteredItems.length > 0 && query.trim() && <div className="search-results"><div className="search-results-heading"><Search size={14} />{filteredItems.length} {t('allItems')}</div>{filteredItems.slice(0, 18).map(item => <button key={item.key} onClick={() => addItem(item.key)}><div><strong>{item.title}</strong><small>{item.firstAuthor || '—'} · {item.year || '—'}</small></div><Plus size={15} /></button>)}</div>}
    {menu && <div className="context-menu" style={{ left: Math.min(menu.x, window.innerWidth - 268), top: Math.min(menu.y, window.innerHeight - 260) }} onClick={event => event.stopPropagation()}>{menu.itemKey && <><button onClick={() => void openItem(menu.itemKey!)}><FolderOpen size={15} />{t('openItem')}</button>{(snapshot?.items.find(item => item.key === menu.itemKey)?.attachments ?? []).filter(a => a.isPdf || a.contentType === 'application/pdf').map(attachment => <button key={attachment.key} onClick={() => void openPdf(attachment.key)}><BookOpen size={15} />{t('openPdf')}{(snapshot?.items.find(item => item.key === menu.itemKey)?.attachments ?? []).filter(a => a.isPdf || a.contentType === 'application/pdf').length > 1 ? ` · ${attachment.title || attachment.key}` : ''}</button>)}{!(snapshot?.items.find(item => item.key === menu.itemKey)?.attachments ?? []).some(a => a.isPdf || a.contentType === 'application/pdf') && <button className="disabled-menu" disabled><BookOpen size={15} />{t('noPdf')}</button>}<div className="menu-divider" /><button onClick={() => { setRelationSource(menu.nodeId); setSelectedNode(menu.nodeId); setMenu(null); setError(t('relationHint')); }}><GitBranch size={15} />{t('addRelation')}</button><button onClick={() => { setDialog({ kind: 'text', text: '' }); setMenu(null); }}><StickyNote size={15} />{t('addText')}</button><button className="danger-menu" onClick={() => removeNode(menu.nodeId)}><Trash2 size={15} />{t('remove')}</button></>}</div>}
    {error && <div className="toast-error"><span>{error}</span><button onClick={() => setError(null)}><X size={15} /></button></div>}
    {dialog && <div className="modal-backdrop" onClick={() => setDialog(null)}><div className="modal" onClick={event => event.stopPropagation()}><div className="modal-header"><h3>{dialog.kind === 'edge' ? t('addEdge') : t('addText')}</h3><button className="icon-button" onClick={() => setDialog(null)}><X size={16} /></button></div>{dialog.kind === 'edge' ? <><label>{t('edgeLabel')}<input autoFocus value={dialog.label} onChange={event => setDialog({ ...dialog, label: event.target.value })} /></label><label>{t('edgeNote')}<textarea value={dialog.note} onChange={event => setDialog({ ...dialog, note: event.target.value })} rows={3} /></label><div className="modal-grid"><label className="toggle"><input type="checkbox" checked={dialog.directed} onChange={event => setDialog({ ...dialog, directed: event.target.checked })} /><span className="toggle-track" /><span>{t('directed')}</span></label><label>{t('solid')}<select value={dialog.lineStyle} onChange={event => setDialog({ ...dialog, lineStyle: event.target.value as 'solid' | 'dashed' | 'dotted' })}><option value="solid">{t('solid')}</option><option value="dashed">{t('dashed')}</option><option value="dotted">{t('dotted')}</option></select></label><label>{t('edgeColor')}<input className="color-input" type="color" value={dialog.color} onChange={event => setDialog({ ...dialog, color: event.target.value })} /></label></div></> : <label>{t('addText')}<textarea autoFocus value={dialog.text} onChange={event => setDialog({ ...dialog, text: event.target.value })} rows={5} /></label>}<div className="modal-actions">{dialog.kind === 'edge' && dialog.edgeId && <button className="danger-button" onClick={() => deleteEdge(dialog.edgeId!)}>{t('deleteEdge')}</button>}<button className="ghost-button" onClick={() => setDialog(null)}>{t('cancel')}</button><button className="refresh-button" onClick={saveDialog}>{t('save')}</button></div></div></div>}
  </div>;
}

function escapeXml(value: string) { return value.replace(/[<>&'\"]/g, character => ({ '<': '&lt;', '>': '&gt;', '&': '&amp;', "'": '&apos;', '"': '&quot;' }[character] ?? character)); }

export default function App() { return <ReactFlowProvider><AppInner /></ReactFlowProvider>; }
