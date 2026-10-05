import { loadFonts, type FontResolver } from '@docx-editor.dev/core';
import type { HostFontFace } from './bridge';

const ALWAYS_LOADED = ['Calibri', 'Times New Roman', 'Arial'];

/**
 * Resolves the families a document names to the Windows fonts the host exposes
 * over its virtual font host, so pagination is measured with real metrics.
 */
export function createSystemFontResolver(faces: readonly HostFontFace[]): FontResolver | undefined {
  if (faces.length === 0) return undefined;

  const byFamily = new Map<string, HostFontFace[]>();
  for (const face of faces) {
    const key = face.family.toLowerCase();
    const list = byFamily.get(key);
    if (list) list.push(face);
    else byFamily.set(key, [face]);
  }

  const supportedFamilies = [...new Set(faces.map((f) => f.family))].sort((a, b) => a.localeCompare(b));

  return async (request) => {
    const wanted = new Set<string>([...request.families, request.defaultFamily, ...ALWAYS_LOADED]);
    const sources = [...wanted].flatMap((family) => byFamily.get(family.toLowerCase()) ?? []);
    if (sources.length === 0) return { supportedFamilies };

    const result = await loadFonts({
      sources: sources.map((f) => ({ url: f.url, family: f.family, weight: f.weight, style: f.style })),
      ...(request.signal ? { signal: request.signal } : {}),
    });
    return { supportedFamilies, sources: result.sources };
  };
}
