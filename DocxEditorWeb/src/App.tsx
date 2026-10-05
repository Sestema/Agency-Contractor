import { Component, useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { DocxEditor, LocaleProvider, useChromeTranslate, useEditorCommand } from '@docx-editor.dev/react';
import type { Editor } from '@docx-editor.dev/core';
import {
  base64ToBytes,
  bytesToBase64,
  isHosted,
  onHostMessage,
  postToHost,
  type HostMessage,
  type InitMessage,
  type RequestMessage,
  type TagReplacement,
} from './bridge';
import { createSystemFontResolver } from './fonts';
import { catalogFor, regionalLocaleFor } from './i18n';
import { replaceTagPlaceholders, refreshTagHighlights, readPlainText } from './documentOps';

type DocumentSource = Uint8Array | 'blank';

interface LoadedDocument {
  key: number;
  source: DocumentSource;
}

const DEV_INIT: InitMessage = { type: 'init', locale: 'uk', tags: ['EMPLOYEE_FullName', 'COMPANY_Name'], fonts: [] };

export function App() {
  const [init, setInit] = useState<InitMessage | null>(isHosted() ? null : DEV_INIT);
  const [doc, setDoc] = useState<LoadedDocument | null>(isHosted() ? null : { key: 0, source: 'blank' });
  const editorRef = useRef<Editor | null>(null);
  const tagsRef = useRef<string[]>(init?.tags ?? []);
  const highlightTimer = useRef<number | undefined>(undefined);
  const incomingChunks = useRef<string[] | null>(null);

  const scheduleHighlights = useCallback(() => {
    window.clearTimeout(highlightTimer.current);
    highlightTimer.current = window.setTimeout(() => {
      if (editorRef.current) refreshTagHighlights(editorRef.current, tagsRef.current);
    }, 350);
  }, []);

  const handleRequest = useCallback(async (message: RequestMessage) => {
    const editor = editorRef.current;
    if (!editor) {
      postToHost({ type: 'response', id: message.id, ok: false, error: 'Editor is not ready' });
      return;
    }
    try {
      let result: unknown;
      if (message.op === 'save') {
        postBase64InChunks(message.id, bytesToBase64(await editor.save()));
        return;
      } else if (message.op === 'plainText') {
        result = readPlainText(editor);
      } else {
        result = replaceTagPlaceholders(editor, message.items ?? ([] as TagReplacement[]));
        scheduleHighlights();
      }
      postToHost({ type: 'response', id: message.id, ok: true, result });
    } catch (error) {
      postToHost({ type: 'response', id: message.id, ok: false, error: String(error instanceof Error ? error.message : error) });
    }
  }, [scheduleHighlights]);

  useEffect(() => {
    onHostMessage((message: HostMessage) => {
      switch (message.type) {
        case 'init':
          tagsRef.current = message.tags;
          setInit(message);
          break;
        case 'loadStart':
          editorRef.current = null;
          beginDocumentLoad(message.count);
          break;
        case 'loadChunk':
          acceptDocumentChunk(message.index, message.data);
          break;
        case 'insertText':
          insertAtCaret(editorRef.current, message.text);
          scheduleHighlights();
          break;
        case 'request':
          void handleRequest(message);
          break;
      }
    });
    postToHost({ type: 'ready' });
  }, [handleRequest, scheduleHighlights]);

  const beginDocumentLoad = (count: number) => {
    incomingChunks.current = count > 0 ? new Array<string>(count) : null;
    if (count === 0) postToHost({ type: 'loadFailed', message: 'The editor received an empty document.' });
  };

  const acceptDocumentChunk = (index: number, data: string) => {
    const parts = incomingChunks.current;
    if (!parts || index < 0 || index >= parts.length) return;
    parts[index] = data;
    if (parts.some((part) => part === undefined)) return;

    try {
      const source = base64ToBytes(parts.join(''));
      incomingChunks.current = null;
      postToHost({ type: 'trace', message: `Editor received ${source.byteLength} bytes inline` });
      if (source.byteLength === 0) throw new Error('The editor received an empty document.');
      setDoc((prev) => ({ key: (prev?.key ?? 0) + 1, source }));
    } catch (error) {
      postToHost({ type: 'loadFailed', message: String(error instanceof Error ? error.message : error) });
    }
  };

  useEffect(() => {
    const blockHostShortcuts = (event: KeyboardEvent) => {
      if (!(event.ctrlKey || event.metaKey) || event.altKey) return;
      const key = event.key.toLowerCase();
      if (key === 's' || key === 'o' || key === 'p') {
        event.preventDefault();
        event.stopPropagation();
        if (key === 's') postToHost({ type: 'saveRequested' });
      }
    };
    window.addEventListener('keydown', blockHostShortcuts, true);
    return () => window.removeEventListener('keydown', blockHostShortcuts, true);
  }, []);

  const fonts = useMemo(() => createSystemFontResolver(init?.fonts ?? []), [init]);
  const catalog = useMemo(() => catalogFor(init?.locale ?? 'uk'), [init]);

  if (!init || !doc) return <div className="host-waiting" />;

  return (
    <LoadErrorBoundary key={doc.key}>
      <LocaleProvider i18n={catalog}>
        <DocxEditor.Root
          key={doc.key}
          document={doc.source}
          mode="edit"
          locale={regionalLocaleFor(init.locale)}
          {...(fonts ? { fonts } : {})}
          onReady={(editor) => {
            editorRef.current = editor;
            postToHost({ type: 'loaded' });
            scheduleHighlights();
          }}
          onChange={(change) => {
            if (change.source) return;
            postToHost({ type: 'changed' });
            scheduleHighlights();
          }}
        >
          <EditorChrome />
        </DocxEditor.Root>
      </LocaleProvider>
    </LoadErrorBoundary>
  );
}

function EditorChrome() {
  const t = useChromeTranslate();
  return (
    <div className="docx-editor editor-shell">
      <DocxEditor.Menu t={t} reportIssue={false}>
        <DocxEditor.Menu.File hidden />
        <DocxEditor.Menu.Review hidden />
      </DocxEditor.Menu>
      <DocxEditor.Toolbar t={t}>
        <DocxEditor.Toolbar.Comments hidden />
        <DocxEditor.Toolbar.EditingMode hidden />
        <DocxEditor.Toolbar.Reviewers hidden />
        <DocxEditor.Toolbar.Save hidden />
        <TableAlignActions />
      </DocxEditor.Toolbar>
      <DocxEditor.HorizontalRuler unit="cm" />
      <div className="editor-workspace">
        <DocxEditor.Navigation />
        <DocxEditor.Viewport>
          <DocxEditor.VerticalRuler unit="cm" />
          <DocxEditor.HeaderFooterChrome />
          <DocxEditor.NotesChrome />
          <DocxEditor.Content />
          <DocxEditor.HyperLink />
          <DocxEditor.ContextMenu>
            <DocxEditor.ContextMenu.Slot slot="review.comments" hidden />
          </DocxEditor.ContextMenu>
        </DocxEditor.Viewport>
        <DocxEditor.Loading overlay />
        <DocxEditor.PageNumber />
      </div>
    </div>
  );
}

const ALIGN_TABLE_LEFT = { type: 'setTableProperties', justification: 'left' } as const;
const ALIGN_TABLE_CENTER = { type: 'setTableProperties', justification: 'center' } as const;
const ALIGN_TABLE_RIGHT = { type: 'setTableProperties', justification: 'right' } as const;

function TableAlignActions() {
  const t = useChromeTranslate();
  const left = useEditorCommand(ALIGN_TABLE_LEFT);
  const center = useEditorCommand(ALIGN_TABLE_CENTER);
  const right = useEditorCommand(ALIGN_TABLE_RIGHT);
  if (!left.isEnabled && !center.isEnabled && !right.isEnabled) return null;

  return (
    <>
      <DocxEditor.Toolbar.Action
        label={t('tableAdvanced.alignTableLeft')}
        icon={<AlignIcon mode="left" />}
        active={left.isActive}
        disabled={!left.isEnabled}
        disabledReason={left.disabledReason ?? undefined}
        onSelect={() => { left.execute(); }}
      />
      <DocxEditor.Toolbar.Action
        label={t('tableAdvanced.alignTableCenter')}
        icon={<AlignIcon mode="center" />}
        active={center.isActive}
        disabled={!center.isEnabled}
        disabledReason={center.disabledReason ?? undefined}
        onSelect={() => { center.execute(); }}
      />
      <DocxEditor.Toolbar.Action
        label={t('tableAdvanced.alignTableRight')}
        icon={<AlignIcon mode="right" />}
        active={right.isActive}
        disabled={!right.isEnabled}
        disabledReason={right.disabledReason ?? undefined}
        onSelect={() => { right.execute(); }}
      />
    </>
  );
}

function AlignIcon({ mode }: { mode: 'left' | 'center' | 'right' }) {
  const x = mode === 'left' ? 1 : mode === 'right' ? 7 : 4;
  return (
    <svg width="18" height="18" viewBox="0 0 18 18" aria-hidden="true">
      <rect x={x} y="3" width="10" height="3" rx="0.5" fill="currentColor" />
      <rect x={x} y="8" width="10" height="3" rx="0.5" fill="currentColor" />
      <rect x={x} y="13" width="10" height="2" rx="0.5" fill="currentColor" />
    </svg>
  );
}

const SAVE_CHUNK_CHARS = 240_000;

function postBase64InChunks(id: number, base64: string) {
  const count = Math.max(1, Math.ceil(base64.length / SAVE_CHUNK_CHARS));
  for (let index = 0; index < count; index++) {
    const start = index * SAVE_CHUNK_CHARS;
    postToHost({
      type: 'saveChunk',
      id,
      index,
      count,
      data: base64.slice(start, start + SAVE_CHUNK_CHARS),
    });
  }
}

function insertAtCaret(editor: Editor | null, text: string) {
  if (!editor || !text) return;
  editor.focus();
  const selected = editor.query({ type: 'selectedText' });
  editor.exec(selected ? { type: 'replaceText', text } : { type: 'insertText', text });
}

class LoadErrorBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  componentDidCatch(error: unknown) {
    postToHost({ type: 'loadFailed', message: String(error instanceof Error ? error.message : error) });
  }

  render() {
    return this.state.failed ? <div className="host-waiting" /> : this.props.children;
  }
}
