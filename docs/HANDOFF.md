# Bamen 引き継ぎ (HANDOFF)

撮影 (カメラとカット) をテキストで書き、動く絵コンテにする言語。Lily# (音)、Shosa (人の動き) の上に重なる 3 つ目。
リポジトリは `C:\MyProj\Bamen`。下のパスはリポジトリの根から。

## 現在地 (2026-11-23 の始まり)

- この日にリポジトリを作った。中身は文書だけで、コードはまだない。
- 製品の大きな絵、リポジトリを Shosa と分けた理由、名前 (Konte を退けて Bamen) は `docs/DESIGN.md` 1〜2 (ユーザーと決めた)。
- M0 の文法の 7 点 (`docs/DESIGN.md` 3.2) は、ユーザーが 7 点とも第 1 案に決めた。実装で決めた細部は 3.2.1。
- M0 の骨組みは入った：`Bamen.Core` (Syntax：字句と構文、Semantics：束縛と時間、Dances：`shosa clip` を走らせて読む、Cameras：固定カメラの解決、Output：check の報告・カメラの JSON・絵コンテのシート・プレビューの JSON、Canvas は Shosa の写し)、`Bamen.Cli` (`bamen check`、`cameras`、`sheet`、`preview`)、`Bamen.Tests` (30 件)。LSP はまだ。
- 固定カメラの解き方は `docs/DESIGN.md` 3.2.2 (レンズは大きさで 35/50/85 mm など、実装で決めた細部)。見本のシートは `tools\build.ps1` が `out\sample\dance.png` に出す。
- VS Code 拡張 `editors/vscode` (未公開)：Shosa のプレビューを写し、解いたカメラでカットごとに切り替えて再生、MP4 (1920×1080、曲つき) を書く。言語サーバーなしで `bamen preview --stdin` を走らせる。細部は DESIGN.md 3.2.3。`tools\test-extension-host.ps1 -Lily` で本物の VS Code で確かめ済み (診断、MP4 の映像と音、各カットのコマは `bamen sheet` と同じ構図)。
- 自動で確かめていないこと：Shots の箱の切り替え、再生中の shot の行の強調、カーソルで shot へ飛ぶ (F5 で目で見る)。
- ユーザーに見てもらうこと：プレビュー (F5) と `out\hosttest\dance.mp4`。DESIGN.md 3.2.2 の最後 (動き回る対象への close は寄れない。診断で言うか)。
- 次は ROADMAP 次の一手 5 (ユーザーの確認)、その後 M1。

## よく使うコマンド

```
tools\build.ps1                     # ビルド、テスト、見本の check・cameras・sheet (本物の shosa で、out\sample へ)。-NoTest、-Configuration Release
tools\build.ps1 -Publish            # さらに bamen.exe を自己完結で out\cli へ
dotnet test Bamen.slnx
dotnet build Bamen.Cli
Bamen.Cli\bin\Debug\net10.0\bamen.exe check samples\dance.bamen   # shosa は BAMEN_SHOSA → PATH → VS Code の Shosa 拡張の順に探す
Bamen.Cli\bin\Debug\net10.0\bamen.exe cameras samples\dance.bamen [-o x.json]   # 解いたカメラの JSON
Bamen.Cli\bin\Debug\net10.0\bamen.exe sheet samples\dance.bamen [-o x.png]      # 各カットの最初・真ん中・最後のコマのシート
Bamen.Cli\bin\Debug\net10.0\bamen.exe preview samples\dance.bamen [--stdin] [-o x.json]   # 拡張が読む JSON
cd editors\vscode; npm install; npm run build; npx tsc --noEmit   # 拡張 (F5 は .vscode\launch.json)
tools\test-extension-host.ps1 [-Lily]   # 本物の VS Code で拡張を試す。MP4 と各カットのコマを out\hosttest へ。-Lily で曲つき
```

## 最初に読むもの

- `docs/ROADMAP.md` — 段の計画と次の一手。
- `docs/DESIGN.md` — 設計の決定とその理由。
- 隣のリポジトリ：`C:\MyProj\Shosa` (動きの供給元。`shosa clip` を読む。拡張の基盤もここから写す)、`C:\MyProj\LilySharp` (曲)。

## 決まりごと

- `git push` はしない (ユーザーが行う)。
- セッションの終わりに、「このまま続行か、コンテキストをクリアするか」を理由つきで報告する (Shosa と同じ)。
