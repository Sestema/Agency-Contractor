import { unzipSync, zipSync } from 'fflate';
import type { Editor } from '@docx-editor.dev/core';

export type TableJustification = 'left' | 'center' | 'right';

const PART = /^word\/(document|header\d+|footer\d+|footnotes|endnotes)\.xml$/;
const FALLBACK_WIDTH_TWIPS = 9360;

interface AlignRequest {
  paraId?: string | null;
  marker?: string | null;
  justification: TableJustification;
  equalize: boolean;
}

export async function alignSelectedTable(editor: Editor, justification: TableJustification): Promise<boolean> {
  if (!editor.getSelectedTable()) return false;

  const selection = editor.query({ type: 'selection' });
  const paraId = anchorParaId(selection?.from) ?? anchorParaId(selection?.to);
  const equalize = justification === 'center';
  const saved = new Uint8Array(await editor.save());
  const aligned = alignTableInPackage(saved, { paraId, justification, equalize });
  if (aligned) {
    editor.load(aligned);
    if (paraId) editor.exec({ type: 'setSelection', anchor: { paraId } });
    return true;
  }

  if (editor.query({ type: 'selectedText' })) return false;
  const marker = `WNALN${Math.random().toString(36).slice(2, 10)}`;
  const inserted = editor.exec({ type: 'insertText', text: marker });
  if (!inserted.ok) return false;

  try {
    const marked = new Uint8Array(await editor.save());
    const next = alignTableInPackage(marked, { marker, justification, equalize });
    if (!next) {
      removeMarker(editor, marker);
      return false;
    }
    editor.load(next);
    return true;
  } catch {
    removeMarker(editor, marker);
    return false;
  }
}

export function alignTableInPackage(bytes: Uint8Array, request: AlignRequest): Uint8Array | null {
  const files = unzipSync(bytes);
  const decoder = new TextDecoder('utf-8');
  const encoder = new TextEncoder();
  for (const path of Object.keys(files)) {
    if (!PART.test(path)) continue;
    const xml = decoder.decode(files[path]);
    const next = alignTableXml(xml, request);
    if (next === xml) continue;
    files[path] = encoder.encode(next);
    return zipSync(files);
  }
  return null;
}

export function alignTableXml(xml: string, request: AlignRequest): string {
  const index = locate(xml, request);
  if (index < 0) return xml;
  const bounds = findTableBounds(xml, index);
  if (!bounds) return xml;

  let table = xml.slice(bounds.start, bounds.end);
  if (request.marker) table = table.split(request.marker).join('');
  table = transformTable(table, request.justification, request.equalize, contentWidthTwips(xml));
  let next = xml.slice(0, bounds.start) + table + xml.slice(bounds.end);
  if (request.marker) next = next.split(request.marker).join('');
  return next;
}

function anchorParaId(point: { paraId?: string } | { container: unknown } | null | undefined): string | null {
  if (!point || !('paraId' in point) || !point.paraId) return null;
  return point.paraId;
}

function removeMarker(editor: Editor, marker: string) {
  const hit = editor.findMatches(marker, { matchCase: true }).flat()[0];
  if (!hit || !editor.selectMatch(hit).ok) return;
  editor.exec({ type: 'replaceText', text: '' });
}

function locate(xml: string, request: AlignRequest): number {
  if (request.paraId) {
    const pattern = new RegExp(`\\bparaId\\s*=\\s*["']${escapeRegExp(request.paraId)}["']`, 'i');
    return pattern.exec(xml)?.index ?? -1;
  }
  if (request.marker) return xml.indexOf(request.marker);
  return -1;
}

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function transformTable(
  tableXml: string,
  justification: TableJustification,
  equalize: boolean,
  contentWidth: number | null,
): string {
  const { xml, masks } = maskNestedTables(tableXml);
  let current = mapTblPr(xml, (properties) => upsertJc(properties, justification));
  if (justification === 'center') current = mapTblPr(current, stripIndent);

  if (equalize) {
    const grid = readGridWidths(current);
    const fromCells = grid ? null : readFirstRowWidths(current);
    const count = grid?.length || fromCells?.length || countColumns(current);
    const known = totalOf(grid) || totalOf(fromCells);
    if (count > 1) {
      const total = known > 0 ? known : contentWidth ?? FALLBACK_WIDTH_TWIPS;
      const widths = equalShares(total, count);
      current = writeGrid(current, widths);
      current = writeCells(current, widths);
      current = mapTblPr(current, (properties) => upsertTblW(upsertLayoutFixed(properties), total));
    }
  }

  return unmask(current, masks);
}

function totalOf(widths: number[] | null | undefined): number {
  if (!widths) return 0;
  return widths.reduce((sum, width) => sum + (Number.isFinite(width) ? width : 0), 0);
}

function equalShares(total: number, count: number): number[] {
  const base = Math.floor(total / count);
  const widths = Array.from({ length: count }, () => base);
  widths[count - 1] = total - base * (count - 1);
  return widths;
}

function contentWidthTwips(xml: string): number | null {
  const section = xml.match(/<w:sectPr\b[\s\S]*?<\/w:sectPr>/);
  if (!section) return null;
  const page = /<w:pgSz\b[^>]*\bw:w="(\d+)"/.exec(section[0]);
  const margins = /<w:pgMar\b([^>]*)\/?>/.exec(section[0]);
  if (!page || !margins) return null;
  const left = Number(/\bw:left="(\d+)"/.exec(margins[1])?.[1] ?? 0);
  const right = Number(/\bw:right="(\d+)"/.exec(margins[1])?.[1] ?? 0);
  const width = Number(page[1]) - left - right;
  return width > 1000 ? width : null;
}

function isTableStart(xml: string, index: number): boolean {
  const next = xml[index + 6];
  return next === '>' || next === '/' || (next !== undefined && /\s/.test(next));
}

function findTableEnd(xml: string, start: number): number {
  let depth = 0;
  let index = start;
  while (index < xml.length) {
    if (xml.startsWith('<w:tbl', index) && isTableStart(xml, index)) {
      depth += 1;
      index += 6;
      continue;
    }
    if (xml.startsWith('</w:tbl>', index)) {
      depth -= 1;
      index += '</w:tbl>'.length;
      if (depth === 0) return index;
      continue;
    }
    index += 1;
  }
  return xml.length;
}

function findTableBounds(xml: string, needle: number): { start: number; end: number } | null {
  const before = xml.slice(0, needle);
  let from = before.length;
  while (from > 0) {
    const start = before.lastIndexOf('<w:tbl', from - 1);
    if (start < 0) return null;
    if (isTableStart(xml, start)) {
      const end = findTableEnd(xml, start);
      if (end > needle) return { start, end };
    }
    from = start;
  }
  return null;
}

function maskNestedTables(tableXml: string): { xml: string; masks: string[] } {
  const masks: string[] = [];
  let xml = '';
  let depth = 0;
  let index = 0;
  while (index < tableXml.length) {
    if (tableXml.startsWith('<w:tbl', index) && isTableStart(tableXml, index) && depth > 0) {
      const end = findTableEnd(tableXml, index);
      xml += `\uE000${masks.length}\uE001`;
      masks.push(tableXml.slice(index, end));
      index = end;
      continue;
    }
    if (tableXml.startsWith('<w:tbl', index) && isTableStart(tableXml, index)) depth += 1;
    else if (tableXml.startsWith('</w:tbl>', index)) depth = Math.max(0, depth - 1);
    xml += tableXml[index];
    index += 1;
  }
  return { xml, masks };
}

function unmask(xml: string, masks: readonly string[]): string {
  return xml.replace(/\uE000(\d+)\uE001/g, (_match, index: string) => masks[Number(index)] ?? '');
}

function mapTblPr(tableXml: string, update: (properties: string) => string): string {
  const open = tableXml.search(/<w:tblPr[\s>]/);
  if (open < 0) {
    const tagEnd = tableXml.indexOf('>');
    if (tagEnd < 0) return tableXml;
    const created = update('<w:tblPr></w:tblPr>');
    return tableXml.slice(0, tagEnd + 1) + created + tableXml.slice(tagEnd + 1);
  }
  const close = tableXml.indexOf('</w:tblPr>', open);
  if (close < 0) return tableXml;
  const end = close + '</w:tblPr>'.length;
  return tableXml.slice(0, open) + update(tableXml.slice(open, end)) + tableXml.slice(end);
}

function upsertJc(properties: string, justification: TableJustification): string {
  const tag = `<w:jc w:val="${justification}"/>`;
  if (/<w:jc\b/.test(properties)) {
    return properties
      .replace(/<w:jc\b[^>]*\/>/g, tag)
      .replace(/<w:jc\b[^>]*>[\s\S]*?<\/w:jc>/g, tag);
  }
  return properties.replace(/<w:tblPr\b([^>]*)>/, `<w:tblPr$1>${tag}`);
}

function stripIndent(properties: string): string {
  return properties
    .replace(/<w:tblInd\b[^>]*\/>/g, '')
    .replace(/<w:tblInd\b[^>]*>[\s\S]*?<\/w:tblInd>/g, '');
}

function upsertLayoutFixed(properties: string): string {
  const tag = '<w:tblLayout w:type="fixed"/>';
  if (/<w:tblLayout\b/.test(properties)) return properties.replace(/<w:tblLayout\b[^>]*\/?>/g, tag);
  return properties.replace(/<w:tblPr\b([^>]*)>/, `<w:tblPr$1>${tag}`);
}

function upsertTblW(properties: string, total: number): string {
  const tag = `<w:tblW w:w="${total}" w:type="dxa"/>`;
  if (/<w:tblW\b/.test(properties)) return properties.replace(/<w:tblW\b[^>]*\/?>/g, tag);
  return properties.replace(/<w:tblPr\b([^>]*)>/, `<w:tblPr$1>${tag}`);
}

function readGridWidths(tableXml: string): number[] | null {
  const grid = tableXml.match(/<w:tblGrid\b[^>]*>[\s\S]*?<\/w:tblGrid>/);
  if (!grid) return null;
  const widths = [...grid[0].matchAll(/<w:gridCol\b[^>]*(?:\/>|>\s*<\/w:gridCol>)/g)].map((match) => {
    return Number(/\bw:w="(\d+)"/.exec(match[0])?.[1] ?? 0);
  });
  return widths.length > 0 ? widths : null;
}

function readFirstRowWidths(tableXml: string): number[] | null {
  const row = tableXml.match(/<w:tr\b[\s\S]*?<\/w:tr>/);
  if (!row) return null;
  const widths: number[] = [];
  for (const cell of row[0].matchAll(/<w:tc\b[\s\S]*?<\/w:tc>/g)) {
    const span = Number(/<w:gridSpan\b[^>]*\bw:val="(\d+)"/.exec(cell[0])?.[1] ?? 1);
    const type = /<w:tcW\b[^>]*\bw:type="([^"]+)"/.exec(cell[0])?.[1];
    if (type && type !== 'dxa') return null;
    const width = Number(/<w:tcW\b[^>]*\bw:w="(\d+)"/.exec(cell[0])?.[1] ?? 0);
    const share = span > 0 ? Math.round(width / span) : width;
    for (let index = 0; index < Math.max(1, span); index += 1) widths.push(share);
  }
  return widths.length > 0 ? widths : null;
}

function countColumns(tableXml: string): number {
  const row = tableXml.match(/<w:tr\b[\s\S]*?<\/w:tr>/);
  if (!row) return 0;
  let count = 0;
  for (const cell of row[0].matchAll(/<w:tc\b[\s\S]*?<\/w:tc>/g)) {
    count += Number(/<w:gridSpan\b[^>]*\bw:val="(\d+)"/.exec(cell[0])?.[1] ?? 1);
  }
  return count;
}

function writeGrid(tableXml: string, widths: readonly number[]): string {
  const grid = `<w:tblGrid>${widths.map((width) => `<w:gridCol w:w="${width}"/>`).join('')}</w:tblGrid>`;
  if (/<w:tblGrid\b/.test(tableXml)) {
    return tableXml.replace(/<w:tblGrid\b[^>]*>[\s\S]*?<\/w:tblGrid>/, grid);
  }
  const propertiesEnd = tableXml.indexOf('</w:tblPr>');
  if (propertiesEnd >= 0) {
    const at = propertiesEnd + '</w:tblPr>'.length;
    return tableXml.slice(0, at) + grid + tableXml.slice(at);
  }
  const tagEnd = tableXml.indexOf('>');
  return tableXml.slice(0, tagEnd + 1) + grid + tableXml.slice(tagEnd + 1);
}

function writeCells(tableXml: string, widths: readonly number[]): string {
  return tableXml.replace(/<w:tr\b[\s\S]*?<\/w:tr>/g, (row) => {
    let column = 0;
    return row.replace(/<w:tc\b[\s\S]*?<\/w:tc>/g, (cell) => {
      const span = Number(/<w:gridSpan\b[^>]*\bw:val="(\d+)"/.exec(cell)?.[1] ?? 1);
      let sum = 0;
      for (let index = 0; index < span && column + index < widths.length; index += 1) {
        sum += widths[column + index];
      }
      column += span;
      return sum > 0 ? setCellWidth(cell, sum) : cell;
    });
  });
}

function setCellWidth(cell: string, twips: number): string {
  const tag = `<w:tcW w:w="${twips}" w:type="dxa"/>`;
  if (/<w:tcW\b/.test(cell)) {
    return cell
      .replace(/<w:tcW\b[^>]*\/>/g, tag)
      .replace(/<w:tcW\b[^>]*>[\s\S]*?<\/w:tcW>/g, tag);
  }
  if (/<w:tcPr\b/.test(cell)) return cell.replace(/<w:tcPr\b([^>]*)>/, `<w:tcPr$1>${tag}`);
  return cell.replace(/<w:tc\b([^>]*)>/, `<w:tc$1><w:tcPr>${tag}</w:tcPr>`);
}
