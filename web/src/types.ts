import type { Edge, Node } from '@xyflow/react';

export interface ZoteroCollection { key: string; name: string; parentKey?: string | null; itemCount?: number; }
export interface ZoteroCreator { creatorType: string; firstName?: string | null; lastName?: string | null; name?: string | null; displayName?: string; }
export interface ZoteroAttachment { key: string; parentItemKey: string; title?: string | null; contentType?: string | null; isPdf?: boolean; }
export interface ZoteroItem {
  key: string; itemType: string; title: string; date?: string | null; year?: string | null;
  publicationTitle?: string | null; doi?: string | null; url?: string | null; abstractNote?: string | null;
  correspondingAuthor?: string | null; firstAffiliation?: string | null; firstAuthor?: string | null;
  creators: ZoteroCreator[]; collectionKeys: string[]; tags: string[]; attachments: ZoteroAttachment[];
  hasPdf?: boolean; pdfAttachments?: ZoteroAttachment[]; contentHash?: string;
}
export interface ZoteroSnapshot { libraryKey: string; refreshedAt: string; contentHash: string; collections: ZoteroCollection[]; items: ZoteroItem[]; }
export interface ZoteroStatus { isRunning: boolean; apiEnabled: boolean; zoteroVersion?: string | null; apiVersion?: string | null; message: string; checkedAt: string; }
export interface RefreshDiff { newItemKeys: string[]; updatedItemKeys: string[]; removedItemKeys: string[]; collectionChanges: string[]; isBaseline: boolean; }
export interface RefreshResult { snapshot: ZoteroSnapshot; diff: RefreshDiff; status: ZoteroStatus; }

export type MorandiAppearance = { borderColor: string; borderWidth: number; fillColor: string; };
export interface CanvasNodeRecord {
  id: string; kind: 'group' | 'paper' | 'image' | 'text'; itemKey?: string | null; collectionKey?: string | null;
  title?: string | null; displayName?: string; text?: string | null; imageId?: string | null;
  x: number; y: number; width: number; height: number; zIndex?: number; isAlias?: boolean; isGhost?: boolean; isOutOfScope?: boolean;
  appearance?: MorandiAppearance;
  titleFontSize?: number;
}
export interface CanvasEdgeRecord {
  id: string; source: string; target: string; sourceHandle?: string | null; targetHandle?: string | null;
  label?: string | null; note?: string | null; isDirected: boolean; color: string;
  lineStyle: 'solid' | 'dashed' | 'dotted'; width?: number;
}
export interface CanvasTextRecord { id: string; text: string; x: number; y: number; width: number; height: number; }
export interface CanvasDocument {
  id: string; name: string; isLegacy?: boolean; createdAt?: string; rootCollectionKey?: string; recursive?: boolean;
  layoutVersion: number; nodes: CanvasNodeRecord[]; edges: CanvasEdgeRecord[]; textNodes: CanvasTextRecord[];
  itemMetadata?: ZoteroItem[];
  viewportX: number; viewportY: number; viewportZoom: number; updatedAt: string;
  nextNodeNumber?: number;
  showAllDetails?: boolean;
}
export interface BoardSummary { id: string; name: string; isLegacy?: boolean; updatedAt: string; }
export type NodeData = {
  record: CanvasNodeRecord; item?: ZoteroItem | null; relationSource?: boolean; suppressHover?: boolean;
  showAllDetails?: boolean; connecting?: boolean;
  onBeginRelation?: (id: string) => void; onPinDetails?: (id: string) => void; onPreview?: (id: string) => void; onTextChange?: (id: string, text: string) => void;
};
export type LitFlowNode = Node<NodeData>;
export type LitFlowEdge = Edge<{ note?: string | null; color: string; isDirected: boolean; width: number }>;
