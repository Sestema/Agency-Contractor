import type { Editor } from '@docx-editor.dev/core';
import type { TagReplacement } from './bridge';

const TAG_HIGHLIGHT_SET = 'templateTags';

export function readPlainText(editor: Editor): string {
  return editor
    .query({ type: 'paragraphs' })
    .map((paragraph) => paragraph.text)
    .join('\n');
}

export function refreshTagHighlights(editor: Editor, tags: readonly string[]): void {
  try {
    if (tags.length === 0) {
      editor.clearHighlights(TAG_HIGHLIGHT_SET);
      return;
    }
    const results = editor.findMatches(tags.map((tag) => `\${${tag}}`), { matchCase: true });
    const ranges = results.flat().map((match) => ({
      blockId: match.blockId,
      start: match.start,
      length: match.length,
      ...(match.scope ? { scope: match.scope } : {}),
    }));
    editor.setHighlights(TAG_HIGHLIGHT_SET, ranges, { color: 'rgba(124, 58, 237, 0.16)', priority: -10 });
  } catch {
    // Highlighting is cosmetic; a refused set must never break editing.
  }
}

const normalizeDashes = (text: string) => text.replace(/[\u2012\u2013\u2015]/g, '\u2014');

const squash = (text: string) => normalizeDashes(text).replace(/\s+/g, ' ').trim().toLowerCase();

/**
 * Replaces AI-detected placeholders with template tags. Each placeholder is located by the
 * label text that precedes it in the same paragraph; placeholders without a matching label
 * are skipped rather than guessed.
 */
export function replaceTagPlaceholders(editor: Editor, items: readonly TagReplacement[]): number {
  let replaced = 0;

  for (const item of items) {
    if (!item.replaceWhat || !item.tag) continue;

    const context = squash(item.contextBefore ?? '');
    const queries = [...new Set([item.replaceWhat, normalizeDashes(item.replaceWhat)])];

    let target = null;
    for (const query of queries) {
      const matches = editor.findMatches(query, { matchCase: true });
      target =
        matches.find((match) => {
          const before = squash(match.contextBefore ?? '');
          if (/\$\{[^}]*$/.test(before)) return false;
          return context.length === 0 || before.endsWith(context) || before.includes(context);
        }) ?? null;
      if (target) break;
    }
    if (!target) continue;

    if (!editor.selectMatch(target).ok) continue;
    const result = editor.exec({ type: 'replaceText', text: item.tag });
    if (result.ok && result.changed) replaced++;
  }

  return replaced;
}
