# Bamen 引き継ぎ (HANDOFF)

撮影 (カメラとカット) をテキストで書き、動く絵コンテにする言語。Lily# (音)、Shosa (人の動き) の上に重なる 3 つ目。
リポジトリは `C:\MyProj\Bamen`。下のパスはリポジトリの根から。

## 現在地 (2026-11-23 の始まり)

- この日にリポジトリを作った。中身は文書だけで、コードはまだない。
- 製品の大きな絵、リポジトリを Shosa と分けた理由、名前 (Konte を退けて Bamen) は `docs/DESIGN.md` 1〜2 (ユーザーと決めた)。
- M0 の文法の 7 点 (`docs/DESIGN.md` 3.2) は、ユーザーが 7 点とも第 1 案に決めた。実装で決めた細部は 3.2.1。
- M0 の骨組みは入った：`Bamen.Core` (Syntax：字句と構文、Semantics：束縛と時間、Dances：`shosa clip` を走らせて読む、Output：check の報告)、`Bamen.Cli` (`bamen check` だけ)、`Bamen.Tests` (17 件)。LSP と拡張はまだ。
- 次は ROADMAP 次の一手 3：固定カメラを解き (`Bamen.Core` に Camera を足す)、`bamen cameras` と `bamen sheet`。

## よく使うコマンド

```
tools\build.ps1                     # ビルド、テスト、見本の check (本物の shosa で)。-NoTest、-Configuration Release
tools\build.ps1 -Publish            # さらに bamen.exe を自己完結で out\cli へ
dotnet test Bamen.slnx
dotnet build Bamen.Cli
Bamen.Cli\bin\Debug\net10.0\bamen.exe check samples\dance.bamen   # shosa は BAMEN_SHOSA → PATH → VS Code の Shosa 拡張の順に探す
```

## 最初に読むもの

- `docs/ROADMAP.md` — 段の計画と次の一手。
- `docs/DESIGN.md` — 設計の決定とその理由。
- 隣のリポジトリ：`C:\MyProj\Shosa` (動きの供給元。`shosa clip` を読む。拡張の基盤もここから写す)、`C:\MyProj\LilySharp` (曲)。

## 決まりごと

- `git push` はしない (ユーザーが行う)。
- セッションの終わりに、「このまま続行か、コンテキストをクリアするか」を理由つきで報告する (Shosa と同じ)。
