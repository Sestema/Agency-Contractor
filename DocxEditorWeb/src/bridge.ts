export interface HostFontFace {
  family: string;
  weight: number;
  style: 'normal' | 'italic';
  url: string;
}

export interface InitMessage {
  type: 'init';
  locale: string;
  tags: string[];
  fonts: HostFontFace[];
}

export interface LoadStartMessage {
  type: 'loadStart';
  count: number;
}

export interface LoadChunkMessage {
  type: 'loadChunk';
  index: number;
  data: string;
}

export interface InsertTextMessage {
  type: 'insertText';
  text: string;
}

export interface TagReplacement {
  contextBefore: string;
  replaceWhat: string;
  tag: string;
}

export interface RequestMessage {
  type: 'request';
  id: number;
  op: 'save' | 'plainText' | 'replaceTags';
  items?: TagReplacement[];
}

export type HostMessage = InitMessage | LoadStartMessage | LoadChunkMessage | InsertTextMessage | RequestMessage;

export type EditorMessage =
  | { type: 'ready' }
  | { type: 'loaded' }
  | { type: 'loadFailed'; message: string }
  | { type: 'changed' }
  | { type: 'saveRequested' }
  | { type: 'response'; id: number; ok: boolean; result?: unknown; error?: string }
  | { type: 'saveChunk'; id: number; index: number; count: number; data: string }
  | { type: 'trace'; message: string };

interface WebViewBridge {
  postMessage(message: unknown): void;
  addEventListener(type: 'message', listener: (event: { data: unknown }) => void): void;
}

function webview(): WebViewBridge | undefined {
  return (window as unknown as { chrome?: { webview?: WebViewBridge } }).chrome?.webview;
}

export const isHosted = (): boolean => webview() !== undefined;

export function postToHost(message: EditorMessage): void {
  webview()?.postMessage(message);
}

export function onHostMessage(listener: (message: HostMessage) => void): void {
  webview()?.addEventListener('message', (event) => {
    const data = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
    if (data && typeof data === 'object' && 'type' in data) listener(data as HostMessage);
  });
}

export function base64ToBytes(base64: string): Uint8Array {
  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes;
}

export function bytesToBase64(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer);
  let binary = '';
  const chunk = 0x8000;
  for (let i = 0; i < bytes.length; i += chunk) {
    binary += String.fromCharCode(...bytes.subarray(i, i + chunk));
  }
  return btoa(binary);
}
