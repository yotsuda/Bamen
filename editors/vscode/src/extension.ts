import { ChildProcess, spawn } from 'child_process';
import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { ClipMusic, createStems } from './stems';

// The extension is thin on purpose, and has no language server yet (M0). At every edit it runs
// `bamen preview FILE --stdin` with the text as the editor has it, which answers with one JSON: the
// dance's clip as `shosa clip` wrote it, every shot with its time, its line and its solved camera, and
// the diagnostics. The diagnostics go to the editor; the rest to the preview, which is Shosa's preview
// (src/preview, copied: DESIGN.md 2) filming through the shots' cameras, cut by cut.
//
// Messages between editor and preview, beyond Shosa's (src/preview/main.js has them all):
//   editor  -> preview : { type: 'clip', clips: [clip], shots, ... }  the dance and the shots
//   preview -> editor  : { type: 'active', spans }   the line of the shot being shown, to light it up
//   preview -> editor  : { type: 'song', key, music } the song's notes: answered with `lysc notes` (Lily#), as
//                        Shosa's language server answers; the parts Lily# 0.17+ hands out (stems.ts) come first
// Every position is an offset into the document in UTF-16 code units: what VS Code's offsetAt /
// positionAt count, and what bamen's spans are (a C# string index).

interface Shot { shot: number; start: number; end: number; src: number; len: number; label: string }
interface Preview {
  title?: string;
  /** The Shosa file the dance was read from, in full. */
  dance?: string;
  clip: { music?: ClipMusic | null; figures?: { model?: string }[] } | null;
  shots: Shot[];
  diagnostics: { start: number; length: number; severity: 'error' | 'warning' | 'info'; code: string; message: string }[];
}

let panel: vscode.WebviewPanel | undefined;
let panelUri: string | undefined;

export function activate(context: vscode.ExtensionContext): void {
  const performed = vscode.window.createTextEditorDecorationType({
    backgroundColor: new vscode.ThemeColor('editor.findMatchHighlightBackground'),
    borderRadius: '3px',
  });
  const diagnostics = vscode.languages.createDiagnosticCollection('bamen');
  context.subscriptions.push(performed, diagnostics);

  /** What bamen last answered for each document. */
  const previews = new Map<string, Preview>();
  const editorFor = (uri: string | undefined) =>
    vscode.window.visibleTextEditors.find(e => e.document.uri.toString() === uri);

  // ---- Reading a file: `bamen preview`, one run at a time per document; a newer edit stops an older run.
  const running = new Map<string, ChildProcess>();
  const timers = new Map<string, NodeJS.Timeout>();
  let warned = false;

  const compile = (document: vscode.TextDocument) => {
    const uri = document.uri.toString();
    const program = findBamen(context);
    if (!program) {
      if (!warned) {
        warned = true;
        void vscode.window.showErrorMessage('Bamen: the bamen program was not found. Build it with "dotnet build", or set bamen.cli.path.');
      }
      return;
    }
    running.get(uri)?.kill();
    // An untitled file has no folder: its `dance` is looked for in the first folder of the workspace.
    const file = document.uri.scheme === 'file' ? document.uri.fsPath
      : path.join(vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ?? process.cwd(), 'untitled.bamen');
    const child = spawn(program.command, [...program.args, 'preview', file, '--stdin'], { windowsHide: true });
    running.set(uri, child);
    const out: Buffer[] = [], err: Buffer[] = [];
    child.stdout.on('data', (chunk: Buffer) => out.push(chunk));
    child.stderr.on('data', (chunk: Buffer) => err.push(chunk));
    child.on('error', error => {
      if (running.get(uri) === child) {
        running.delete(uri);
        void vscode.window.showErrorMessage(`Bamen: ${program.command} could not be started: ${error.message}`);
      }
    });
    child.on('close', () => {
      if (running.get(uri) !== child) {
        return;   // stopped for a newer edit
      }
      running.delete(uri);
      let preview: Preview;
      try {
        preview = JSON.parse(Buffer.concat(out).toString('utf8')) as Preview;
      } catch {
        const said = Buffer.concat(err).toString('utf8').trim();
        diagnostics.set(document.uri, [new vscode.Diagnostic(new vscode.Range(0, 0, 0, 1),
          `bamen did not answer${said ? ': ' + said.split('\n')[0] : ''}`, vscode.DiagnosticSeverity.Error)]);
        return;
      }
      previews.set(uri, preview);
      diagnostics.set(document.uri, preview.diagnostics.map(d => {
        // A zero-width span (something missing) would not show; give it one character.
        const start = d.length > 0 ? d.start : Math.max(0, d.start - 1), length = Math.max(1, d.length);
        const range = new vscode.Range(document.positionAt(start), document.positionAt(start + length));
        const severity = d.severity === 'error' ? vscode.DiagnosticSeverity.Error
          : d.severity === 'warning' ? vscode.DiagnosticSeverity.Warning : vscode.DiagnosticSeverity.Information;
        const diagnostic = new vscode.Diagnostic(range, d.message, severity);
        diagnostic.code = d.code;
        diagnostic.source = 'bamen';
        return diagnostic;
      }));
      if (panel && panelUri === uri) {
        post(preview, uri, false);
      }
    });
    child.stdin.end(document.getText(), 'utf8');
  };

  const later = (document: vscode.TextDocument, wait: number) => {
    if (document.languageId !== 'bamen') {
      return;
    }
    const uri = document.uri.toString();
    clearTimeout(timers.get(uri));
    timers.set(uri, setTimeout(() => {
      timers.delete(uri);
      compile(document);
    }, wait));
  };

  context.subscriptions.push(
    vscode.workspace.onDidOpenTextDocument(document => later(document, 0)),
    vscode.workspace.onDidChangeTextDocument(event => later(event.document, 300)),
    // A saved .shosa or .lys may be the dance or the song of an open .bamen file.
    vscode.workspace.onDidSaveTextDocument(saved => {
      if (/\.(shosa|lys)$/i.test(saved.fileName)) {
        vscode.workspace.textDocuments.filter(d => d.languageId === 'bamen').forEach(d => later(d, 0));
      }
    }),
    vscode.workspace.onDidCloseTextDocument(document => {
      diagnostics.delete(document.uri);
      previews.delete(document.uri.toString());
    }));
  vscode.workspace.textDocuments.forEach(document => later(document, 0));

  // ---- The preview.
  const post = (preview: Preview, uri: string, reset: boolean) => {
    const editor = editorFor(uri);
    const clip = preview.clip;
    // The dance's own files (its characters, its song) are beside the Shosa file, not the .bamen file.
    const folder = preview.dance ? path.dirname(preview.dance) : path.dirname(vscode.Uri.parse(uri).fsPath);
    const models: Record<string, string> = {};
    for (const figure of clip?.figures ?? []) {
      if (figure.model && !(figure.model in models)) {
        models[figure.model] = path.resolve(folder, figure.model);
      }
    }
    void panel?.webview.postMessage({
      type: 'clip',
      clips: clip ? [clip] : [],
      shots: preview.shots,
      title: preview.title ?? '',
      models,
      offset: editor ? editor.document.offsetAt(editor.selection.active) : -1,
      reset,
    });
    for (const file of new Set(Object.values(models))) {
      void sendFile('model', file);
    }
    if (clip?.music?.file && preview.dance) {
      stems.want(vscode.Uri.file(preview.dance), [clip.music]);
    }
  };

  // A character file or a song's part is read here and its bytes handed to the page, which parses it.
  const sent = new Map<string, number>();   // type and file -> the time it was last written, as sent
  const sendFile = async (type: 'model' | 'stem', file: string) => {
    let written = -1;
    try {
      written = (await fs.promises.stat(file)).mtimeMs;
    } catch {
      // not there: the page says so
    }
    if (sent.get(type + '|' + file) === written) {
      return;
    }
    sent.set(type + '|' + file, written);
    if (written < 0) {
      void panel?.webview.postMessage({ type, file, error: `${file} was not found.` });
      return;
    }
    try {
      const data = await fs.promises.readFile(file);
      void panel?.webview.postMessage({ type, file, data: new Uint8Array(data.buffer, data.byteOffset, data.byteLength) });
    } catch (error) {
      void panel?.webview.postMessage({ type, file, error: `${file} could not be read: ${String(error)}` });
    }
  };

  // The song, one part at a time, from the Lily# extension (stems.ts, as Shosa's preview has it).
  const stems = createStems(context, {
    post: message => void panel?.webview.postMessage(message),
    sendFile: file => void sendFile('stem', file),
  });

  const show = (uri: string) => {
    panelUri = uri;
    if (!panel) {
      return;
    }
    panel.title = 'Preview ' + path.basename(vscode.Uri.parse(uri).fsPath);
    const known = previews.get(uri);
    if (known) {
      post(known, uri, true);
    } else {
      void panel.webview.postMessage({ type: 'waiting', text: `Reading ${path.basename(vscode.Uri.parse(uri).fsPath)}…` });
      const document = vscode.workspace.textDocuments.find(d => d.uri.toString() === uri);
      if (document) {
        compile(document);
      }
    }
  };

  // The song is played through a sound font when this machine has one, until (or unless) Lily# hands out its parts.
  const dist = vscode.Uri.joinPath(context.extensionUri, 'dist');
  const resourceRoots = () => {
    const font = soundFontFile();
    return font ? [dist, vscode.Uri.file(path.dirname(font))] : [dist];
  };
  const tellSynth = () => {
    if (!panel) {
      return;
    }
    const font = soundFontFile();
    void panel.webview.postMessage({
      type: 'synth',
      processor: panel.webview.asWebviewUri(vscode.Uri.joinPath(dist, 'spessasynth_processor.min.js')).toString(),
      soundFont: font ? panel.webview.asWebviewUri(vscode.Uri.file(font)).toString() : '',
      name: font ? path.basename(font) : '',
    });
  };

  // ---- Export: the preview films the shots (src/preview/video.js); the extension asks where, and writes.
  let next = 1;
  let pageReady = false;
  /** A click made by a command: where to write (no dialog then), and who waits for the file. */
  const asked = new Map<string, { path?: string; done: (files: string[]) => void }>();
  const filming = new Map<string, { target: vscode.Uri; done: (files: string[]) => void }>();

  const suggested = (): vscode.Uri => {
    const source = panelUri ? vscode.Uri.parse(panelUri) : undefined;
    const folder = source?.scheme === 'file' ? path.dirname(source.fsPath) : vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ?? '';
    const stem = source?.scheme === 'file' ? path.basename(source.fsPath, '.bamen') : 'untitled';
    return vscode.Uri.file(path.join(folder, stem + '.mp4'));
  };

  const film = async (given: string | undefined, done: (files: string[]) => void) => {
    const target = given ? vscode.Uri.file(given)
      : await vscode.window.showSaveDialog({ title: 'Export MP4 Video', defaultUri: suggested(), filters: { 'MP4 video (*.mp4)': ['mp4'] } });
    if (!target || !panel) {
      done([]);
      return;
    }
    const id = 'film' + next++;
    filming.set(id, { target, done });
    void panel.webview.postMessage({ type: 'record', id });
  };

  context.subscriptions.push(vscode.commands.registerCommand('bamen.exportVideo', async (given?: string): Promise<string[]> => {
    if (!panel) {
      await vscode.commands.executeCommand('bamen.openPreview');
    }
    for (let i = 0; i < 300 && panel && !(pageReady && panelUri && previews.has(panelUri)); i++) {
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    if (!panel || !pageReady) {
      void vscode.window.showErrorMessage('Bamen: open a .bamen file to film its shots.');
      return [];
    }
    const id = 'ask' + next++;
    return new Promise<string[]>(resolve => {
      asked.set(id, { path: typeof given === 'string' ? given : undefined, done: resolve });
      void panel?.webview.postMessage({ type: 'export', what: 'video', id });
    });
  }));

  interface PageMessage {
    type: string; what?: string; id?: string; data?: ArrayBuffer | Uint8Array; error?: string; silent?: string; stopped?: boolean;
    spans?: [number, number][]; start?: number; length?: number; key?: string; music?: ClipMusic;
  }

  const exported = (message: PageMessage): boolean => {
    if (message.type === 'export') {
      const from = message.id ? asked.get(message.id) : undefined;
      if (message.id) {
        asked.delete(message.id);
      }
      const done = from?.done ?? (() => undefined);
      if (message.what !== 'video') {
        done([]);
        return true;
      }
      film(from?.path, done).catch(error => {
        void vscode.window.showErrorMessage(`Bamen: the export failed. ${error instanceof Error ? error.message : String(error)}`);
        done([]);
      });
      return true;
    }
    if (message.type === 'recorded') {
      const filmed = message.id ? filming.get(message.id) : undefined;
      const from = message.id ? asked.get(message.id) : undefined;
      if (message.id) {
        filming.delete(message.id);
        asked.delete(message.id);
      }
      const done = filmed?.done ?? from?.done ?? (() => undefined);
      if (message.error || !filmed || !message.data) {
        if (message.error) {
          void vscode.window.showErrorMessage(`Bamen: no video was written: ${message.error}`);
        }
        done([]);
        return true;
      }
      const bytes = message.data instanceof ArrayBuffer ? Buffer.from(message.data)
        : Buffer.from(message.data.buffer, message.data.byteOffset, message.data.byteLength);
      fs.promises.writeFile(filmed.target.fsPath, bytes).then(() => {
        const name = path.basename(filmed.target.fsPath);
        void vscode.window.showInformationMessage(`Bamen: wrote ${name}${message.silent ? ` (no sound: ${message.silent})` : ''}`, 'Open').then(choice => {
          if (choice === 'Open') {
            void vscode.env.openExternal(filmed.target);
          }
        });
        done([filmed.target.fsPath]);
      }, error => {
        void vscode.window.showErrorMessage(`Bamen: ${path.basename(filmed.target.fsPath)} could not be written. ${String(error)}`);
        done([]);
      });
      return true;
    }
    return false;
  };

  context.subscriptions.push(vscode.commands.registerCommand('bamen.openPreview', () => {
    const editor = vscode.window.activeTextEditor;
    if (!editor || editor.document.languageId !== 'bamen') {
      return;
    }
    const uri = editor.document.uri.toString();
    if (panel) {
      panel.reveal(vscode.ViewColumn.Beside, true);
      show(uri);
      return;
    }

    panel = vscode.window.createWebviewPanel('bamen.preview', 'Preview', { viewColumn: vscode.ViewColumn.Beside, preserveFocus: true },
      { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: resourceRoots() });
    panel.webview.html = previewHtml(panel.webview, vscode.Uri.joinPath(dist, 'preview.js'));
    panelUri = uri;

    panel.onDidDispose(() => {
      editorFor(panelUri)?.setDecorations(performed, []);
      panel = undefined;
      panelUri = undefined;
      pageReady = false;
      sent.clear();
      stems.forget();
    });

    panel.webview.onDidReceiveMessage((message: PageMessage) => {
      const editor = editorFor(panelUri);
      if (exported(message)) {
        return;
      }
      switch (message.type) {
        case 'song': {
          // The notes of the song, for when Lily# hands out no parts: `lysc notes`, as Shosa's language server reads them.
          const key = message.key, dance = panelUri ? previews.get(panelUri)?.dance : undefined;
          void songNotes(message.music, dance).then(song => void panel?.webview.postMessage({ type: 'song', key, song }));
          break;
        }

        case 'ready':
          sent.clear();   // a new page knows no file yet
          tellSynth();
          if (panelUri) {
            show(panelUri);
          }
          pageReady = true;
          break;

        case 'active':
          // The line of the shot being shown.
          editor?.setDecorations(performed, (message.spans ?? []).map(([start, length]) =>
            new vscode.Range(editor.document.positionAt(start), editor.document.positionAt(start + length))));
          break;

        case 'reveal':
          if (editor && message.start !== undefined) {
            const range = new vscode.Range(editor.document.positionAt(message.start), editor.document.positionAt(message.start + (message.length ?? 0)));
            editor.selection = new vscode.Selection(range.start, range.start);
            editor.revealRange(range, vscode.TextEditorRevealType.InCenterIfOutsideViewport);
          }
          break;
      }
    });
  }));

  // The cursor leads the preview: put it on a shot line and the preview goes to that shot.
  context.subscriptions.push(vscode.window.onDidChangeTextEditorSelection(event => {
    if (!panel || event.textEditor.document.uri.toString() !== panelUri) {
      return;
    }
    if (event.kind === vscode.TextEditorSelectionChangeKind.Command || event.kind === undefined) {
      return;
    }
    void panel.webview.postMessage({ type: 'cursor', offset: event.textEditor.document.offsetAt(event.selections[0].active) });
  }));

  // The preview follows the Bamen file being edited.
  context.subscriptions.push(vscode.window.onDidChangeActiveTextEditor(editor => {
    if (panel && editor && editor.document.languageId === 'bamen' && editor.document.uri.toString() !== panelUri) {
      editorFor(panelUri)?.setDecorations(performed, []);
      show(editor.document.uri.toString());
    }
  }));
}

export function deactivate(): void {
  // the runs of bamen end with their input
}

/**
 * The bamen program: the setting, then the one the package carries (`server`), then, running from the
 * repository, the one `dotnet build` leaves. A .dll is run with `dotnet`.
 */
function findBamen(context: vscode.ExtensionContext): { command: string; args: string[] } | undefined {
  const exe = process.platform === 'win32' ? 'bamen.exe' : 'bamen';
  const configured = vscode.workspace.getConfiguration('bamen').get<string>('cli.path') ?? '';
  const repository = path.join(context.extensionPath, '..', '..', 'Bamen.Cli', 'bin');
  const candidates = [
    configured,
    path.join(context.extensionPath, 'server', exe),
    path.join(repository, 'Debug', 'net10.0', exe),
    path.join(repository, 'Release', 'net10.0', exe),
  ];
  const found = candidates.find(candidate => candidate.length > 0 && fs.existsSync(candidate));
  if (!found) {
    return undefined;
  }
  return found.toLowerCase().endsWith('.dll') ? { command: 'dotnet', args: [found] } : { command: found, args: [] };
}

type SongNotes = { error: string | null; notes: number[][] };
const notesKept = new Map<string, { written: number; song: SongNotes }>();

/**
 * The notes of a dance's song (its `music`: the file beside the Shosa file, the score, the order), as
 * `lysc notes` gives them: what Shosa's language server answers its preview with. Kept until the .lys changes.
 */
async function songNotes(music: ClipMusic | undefined, dance: string | undefined): Promise<SongNotes> {
  if (!music?.file || !dance) {
    return { error: 'The dance names no song.', notes: [] };
  }
  const file = path.resolve(path.dirname(dance), music.file);
  let written: number;
  try {
    written = (await fs.promises.stat(file)).mtimeMs;
  } catch {
    return { error: `There is no file ${file}.`, notes: [] };
  }
  const lysc = await lyscOfLilySharp();
  if (!lysc) {
    return { error: 'The song is read by Lily#: install the Lily# extension.', notes: [] };
  }
  const args = ['notes', file];
  if (music.score) {
    args.push('--score', music.score);
  }
  if (music.order?.length) {
    args.push('--order', music.order.join(' '));
  }
  const key = JSON.stringify(args);
  const kept = notesKept.get(key);
  if (kept && kept.written === written) {
    return kept.song;
  }
  const song = await new Promise<SongNotes>(resolve => {
    const child = spawn(lysc, args, { windowsHide: true });
    const out: Buffer[] = [], err: Buffer[] = [];
    child.stdout.on('data', (chunk: Buffer) => out.push(chunk));
    child.stderr.on('data', (chunk: Buffer) => err.push(chunk));
    child.on('error', error => resolve({ error: `lysc could not be started: ${error.message}`, notes: [] }));
    child.on('close', code => {
      try {
        if (code === 0) {
          resolve(JSON.parse(Buffer.concat(out).toString('utf8')) as SongNotes);
          return;
        }
      } catch {
        // said below
      }
      const said = Buffer.concat(err).toString('utf8').trim();
      resolve({ error: `${music.file} could not be read: ${said || 'lysc gave no answer'}`, notes: [] });
    });
    child.stdin.end();
  });
  notesKept.set(key, { written, song });
  return song;
}

/** The lysc of the installed Lily# extension: what its exports name (`lyscPath`), else the one in its `server` folder. */
async function lyscOfLilySharp(): Promise<string | undefined> {
  const lily = vscode.extensions.getExtension<{ lyscPath?: string }>('yotsuda.lilysharp');
  if (!lily) {
    return undefined;
  }
  let named: string | undefined;
  try {
    const exports = await Promise.race([
      lily.isActive ? Promise.resolve(lily.exports) : lily.activate(),
      new Promise<undefined>(resolve => setTimeout(() => resolve(undefined), 5000)),
    ]);
    named = exports?.lyscPath;
  } catch {
    // Lily# failed to start: its folder below
  }
  const bundled = path.join(lily.extensionPath, 'server', process.platform === 'win32' ? 'lysc.exe' : 'lysc');
  return [named, bundled].find(candidate => !!candidate && fs.existsSync(candidate));
}

/** The sound font the song is played with when Lily# hands out no parts: Bamen's setting, Shosa's, Lily#'s, then the system's. */
function soundFontFile(): string | undefined {
  for (const section of ['bamen', 'shosa', 'lilysharp']) {
    const named = (vscode.workspace.getConfiguration(section).get<string>('playback.soundFont') ?? '').trim();
    if (named && fs.existsSync(named)) {
      return named;
    }
  }
  const packaged = vscode.extensions.getExtension('yotsuda.lilysharp-soundfont');
  if (packaged) {
    const folder = path.join(packaged.extensionPath, 'soundfont');
    try {
      const fonts = fs.readdirSync(folder).filter(name => /\.(sf2|sf3|dls)$/i.test(name)).sort();
      if (fonts.length > 0) {
        return path.join(folder, fonts[0]);
      }
    } catch {
      // installed without its font
    }
  }
  const system = process.platform === 'win32' ? path.join(process.env.SystemRoot ?? 'C:\\Windows', 'System32', 'drivers', 'gm.dls')
    : process.platform === 'darwin' ? '/System/Library/Components/CoreAudio.component/Contents/Resources/gs_instruments.dls'
    : '';
  return system && fs.existsSync(system) ? system : undefined;
}

function previewHtml(webview: vscode.Webview, script: vscode.Uri): string {
  const nonce = Array.from({ length: 32 }, () => 'abcdefghijklmnopqrstuvwxyz0123456789'[Math.floor(Math.random() * 36)]).join('');
  const csp = `default-src 'none'; script-src 'nonce-${nonce}' ${webview.cspSource} 'wasm-unsafe-eval' blob:; style-src 'unsafe-inline'; `
    + `img-src ${webview.cspSource} data: blob:; connect-src ${webview.cspSource} blob:;`;
  return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="${csp}">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Bamen preview</title>
</head>
<body>
<script nonce="${nonce}" src="${webview.asWebviewUri(script)}"></script>
</body>
</html>`;
}
