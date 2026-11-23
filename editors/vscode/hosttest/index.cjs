// Run inside a real VS Code (tools\test-extension-host.ps1): the things only an editor can show.
//
//   - an edit is read as the editor has it (`bamen preview --stdin`), and its diagnostics come back
//   - the preview opens, and films the shots as an MP4 (bamen.exportVideo with a path: no dialog)
//
// The result is written as JSON to the file named by BAMEN_HOSTTEST_OUT; run() rejects if a check fails.
// The MP4 is left where BAMEN_HOSTTEST_MP4 says, for the script to look at.

const vscode = require('vscode');
const fs = require('fs');
const path = require('path');

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

async function until(what, test, seconds = 30) {
  for (let i = 0; i < seconds * 4; i++) {
    const value = await test();
    if (value) {
      return value;
    }
    await sleep(250);
  }
  throw new Error(`timed out waiting for ${what}`);
}

/** The four-letter boxes an MP4 has, among those asked for (enough to tell a sound track from none). */
function boxes(bytes, names) {
  const text = bytes.toString('latin1');
  return names.filter(name => text.includes(name));
}

exports.run = async function run() {
  const out = process.env.BAMEN_HOSTTEST_OUT;
  const mp4 = process.env.BAMEN_HOSTTEST_MP4;
  const report = { vscode: vscode.version, checks: [], failures: [] };
  const check = (name, ok, detail) => {
    report.checks.push({ name, ok: !!ok, detail });
    if (!ok) {
      report.failures.push(name);
    }
  };

  try {
    const folder = vscode.workspace.workspaceFolders[0].uri.fsPath;
    const document = await vscode.workspace.openTextDocument(path.join(folder, 'dance.bamen'));
    const editor = await vscode.window.showTextDocument(document);
    report.lily = !!vscode.extensions.getExtension('yotsuda.lilysharp');

    // An unsaved edit: a shot that looks at no one. Its diagnostic comes from the text as the editor has it.
    const end = document.positionAt(document.getText().length);
    await editor.edit(edit => edit.insert(end, '\nshot 4 wide 1s look nobody\n'));
    const found = await until('the diagnostic of the edit', () =>
      vscode.languages.getDiagnostics(document.uri).find(d => /nobody/.test(d.message)), 60).catch(() => undefined);
    check('an unsaved edit is read, and its diagnostic shown', found,
      vscode.languages.getDiagnostics(document.uri).map(d => `${d.code} ${d.message}`));
    await vscode.commands.executeCommand('undo');
    await until('the edit undone', () => !document.getText().includes('nobody'), 10);
    await until('its diagnostic gone', () => !vscode.languages.getDiagnostics(document.uri).some(d => /nobody/.test(d.message)), 30)
      .then(() => check('undone, the diagnostic goes', true), () => check('undone, the diagnostic goes', false,
        vscode.languages.getDiagnostics(document.uri).map(d => d.message)));

    // The preview, and the MP4 of the shots.
    await vscode.commands.executeCommand('bamen.openPreview');
    if (report.lily) {
      await sleep(8000);   // Lily# reads the song (its notes, or its parts: stems.ts) before the MP4 is filmed
    }
    const started = Date.now();
    const written = await Promise.race([
      vscode.commands.executeCommand('bamen.exportVideo', mp4),
      sleep(240000).then(() => 'timeout'),
    ]);
    report.filmSeconds = (Date.now() - started) / 1000;
    const exists = fs.existsSync(mp4);
    const bytes = exists ? fs.readFileSync(mp4) : Buffer.alloc(0);
    check('the shots are filmed as an MP4', Array.isArray(written) && written.length === 1 && exists && bytes.length > 100000,
      { written, size: bytes.length });
    report.mp4 = { size: bytes.length, boxes: boxes(bytes, ['ftyp', 'moov', 'avc1', 'mp4a']) };
  } catch (error) {
    report.failures.push('threw: ' + (error && error.stack || error));
  }

  if (out) {
    fs.writeFileSync(out, JSON.stringify(report, null, 2));
  }
  if (report.failures.length) {
    throw new Error(report.failures.join('; '));
  }
};
