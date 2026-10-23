import { createFontSource, type FontResolver } from '@docx-editor.dev/core';
import { base64ToBytes, type HostFontFace } from './bridge';

const ALWAYS_LOADED = ['Calibri', 'Times New Roman', 'Arial'];
const FONT_WAIT_MS = 8000;

interface IncomingFont {
  chunks: number;
  parts: Array<string | undefined>;
  waiters: Array<() => void>;
}

const incomingFonts = new Map<string, IncomingFont>();

/**
 * The host reads Windows font files itself and streams the bytes here.
 * Nothing in this path fetches a URL, so opening a template does not wait on the network.
 */
export function noteFontManifest(faces: readonly HostFontFace[]): void {
  for (const face of faces) {
    const existing = incomingFonts.get(face.id);
    if (existing?.chunks === face.chunks) continue;
    incomingFonts.set(face.id, {
      chunks: face.chunks,
      parts: new Array(face.chunks),
      waiters: existing?.waiters ?? [],
    });
  }
}

export function acceptFontChunk(id: string, index: number, data: string): void {
  let face = incomingFonts.get(id);
  if (!face) {
    face = { chunks: index + 1, parts: [], waiters: [] };
    incomingFonts.set(id, face);
  }
  if (face.parts.length <= index) face.parts.length = index + 1;
  face.parts[index] = data;
  if (face.parts.length < face.chunks || face.parts.some((part) => part === undefined)) return;
  const waiters = face.waiters.splice(0);
  waiters.forEach((resolve) => resolve());
}

function fontBytes(id: string): Promise<Uint8Array | null> {
  const face = incomingFonts.get(id);
  if (!face || face.chunks <= 0) return Promise.resolve(null);

  const ready = () => face.parts.length >= face.chunks && face.parts.every((part) => part !== undefined);
  const build = () => base64ToBytes(face.parts.join(''));
  if (ready()) return Promise.resolve(build());

  return new Promise((resolve) => {
    const timer = window.setTimeout(() => resolve(null), FONT_WAIT_MS);
    face.waiters.push(() => {
      window.clearTimeout(timer);
      resolve(ready() ? build() : null);
    });
  });
}

export function createSystemFontResolver(faces: readonly HostFontFace[]): FontResolver | undefined {
  if (faces.length === 0) return undefined;

  const byFamily = new Map<string, HostFontFace[]>();
  for (const face of faces) {
    const key = face.family.toLowerCase();
    const list = byFamily.get(key);
    if (list) list.push(face);
    else byFamily.set(key, [face]);
  }

  const supportedFamilies = [...new Set(faces.map((face) => face.family))].sort((a, b) => a.localeCompare(b));

  return async (request) => {
    const wanted = new Set<string>([...request.families, request.defaultFamily, ...ALWAYS_LOADED]);
    const selected = [...wanted].flatMap((family) => byFamily.get(family.toLowerCase()) ?? []);
    const sources = [];
    for (const face of selected) {
      const bytes = await fontBytes(face.id);
      if (!bytes) continue;
      const created = createFontSource(bytes, {
        family: face.family,
        weight: face.weight,
        style: face.style,
      });
      if ('source' in created) sources.push(created.source);
    }
    return { supportedFamilies, sources };
  };
}
