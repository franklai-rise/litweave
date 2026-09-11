import type { Edge, Node } from '@xyflow/react';

export interface ZoteroCollection {
  key: string;
  name: string;
  parentKey?: string | null;
  itemCount?: number;
}

export interface ZoteroCreator {
  creatorType: string;
  firstName?: string | null;
  lastName?: string | null;
  name?: string | null;
  displayName?: string;
}

export interface ZoteroAttachment {
  key: string;
  parentItemKey: string;
  title?: string | null;
  contentType?: string | null;
  isPdf?: boolean;
}

export interface ZoteroItem {
  key: string;
  itemType: string;
  title: string;
  date?: string | null;
  year?: string | null;
  publicationTitle?: string | null;
  doi?: string | null;
  url?: string | null;
  abstractNote?: string | null;
  correspondingAuthor?: string | null;
  firstAuthor?: string | null;
  creators: ZoteroCreator[];
  collectionKeys: string[];
  tags: string[];
  attachments: ZoteroAttachment[];
  hasPdf?: boolean;
  pdfAttachments?: ZoteroAttachment[];
  contentHash?: string;
}

export interface ZoteroSnapshot {
  libraryKey: string;
  refreshedAt: string;
  contentHash: string;
  collections: ZoteroCollection[];
  items: ZoteroItem[];
}

export interface ZoteroStatus {
  isRunning: boolean;
  apiEnabled: boolean;
  zoteroVersion?: string | null;
  apiVersion?: string | null;
  message: string;
  checkedAt: string;
}

export interface RefreshDiff {
  newItemKeys: string[];
  updatedItemKeys: string[];
  removedItemKeys: string[];
  collectionChanges: string[];
  isBaseline: boolean;
}

export interface RefreshResult {
  snapshot: ZoteroSnapshot;
  diff: RefreshDiff;
  status: ZoteroStatus;
}

export interface CanvasNodeRecord {
  id: string;
  kind: 'group' | 'paper' | 'text';
  itemKey?: string | null;
  collectionKey?: string | null;
  title?: string | null;
  text?: string | null;
  x: number;
  y: number;
  width: number;
  height: number;
  isAlias?: boolean;
  isGhost?: boolean;
  isOutOfScope?: boolean;
}

export interface CanvasEdgeRecord {
  id: string;
  source: string;
  target: string;
  label?: string | null;
  note?: string | null;
  isDirected: boolean;
  color: string;
  lineStyle: 'solid' | 'dashed' | 'dotted';
}

export interface CanvasTextRecord {
  id: string;
  text: string;
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface CanvasDocument {
  id: string;
  rootCollectionKey: string;
  recursive: boolean;
  layoutVersion: number;
  nodes: CanvasNodeRecord[];
  edges: CanvasEdgeRecord[];
  textNodes: CanvasTextRecord[];
  viewportX: number;
  viewportY: number;
  viewportZoom: number;
  updatedAt: string;
}

export type GroupNodeData = { label: string; collectionKey: string; depth: number };
export type PaperNodeData = {
  item: ZoteroItem | null;
  itemKey: string;
  collectionKey?: string | null;
  isAlias: boolean;
  isGhost: boolean;
  isOutOfScope: boolean;
  onContextMenu?: (event: React.MouseEvent, node: Node<PaperNodeData>) => void;
};
export type TextNodeData = { text: string };

export type LitFlowNode = Node<GroupNodeData | PaperNodeData | TextNodeData>;
export type LitFlowEdge = Edge<{ note?: string; color: string; isDirected: boolean }>;
