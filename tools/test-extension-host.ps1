# Runs editors\vscode\hosttest inside a real VS Code: an unsaved edit is read and its diagnostic shown,
# the preview opens and films the shots of samples\dance.bamen as an MP4. Build first: tools\build.ps1,
# then `npm install` and `npm run build` in editors\vscode.
#
#   tools\test-extension-host.ps1          only the extension in editors\vscode: the MP4 is silent (no Lily#)
#   tools\test-extension-host.ps1 -Lily    with the Lily# extensions installed on this PC: the MP4 has the song
#
# A window of VS Code opens for a minute or so, with its own empty profile (a temp folder), so the VS Code
# you have open is not touched. The MP4 is left in out\hosttest\dance.mp4, and with ffmpeg on the PATH a
# frame of each shot beside it (shot1.png …).
param([switch]$Lily)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$extension = Join-Path $root 'editors\vscode'
$code = (Get-Command code.cmd -ErrorAction Stop).Source
$exe = Join-Path (Split-Path (Split-Path $code -Parent) -Parent) 'Code.exe'
$temp = Join-Path ([IO.Path]::GetTempPath()) "bamen-hosttest-$PID"
$work = Join-Path $temp 'work'
New-Item -ItemType Directory -Force $work | Out-Null
Copy-Item (Join-Path $root 'samples\*') $work   # a copy: the test edits dance.bamen (and undoes it)
$keep = Join-Path $root 'out\hosttest'
New-Item -ItemType Directory -Force $keep | Out-Null
$mp4 = Join-Path $keep 'dance.mp4'
Remove-Item $mp4, (Join-Path $keep 'shot*.png') -ErrorAction SilentlyContinue

$arguments = @(
    "--extensionDevelopmentPath=$extension",
    "--extensionTestsPath=$(Join-Path $extension 'hosttest\index.cjs')",
    "--user-data-dir=$(Join-Path $temp 'profile')",
    '--disable-workspace-trust', '--skip-welcome', '--skip-release-notes')
if ($Lily) {
    # An extensions folder of its own, with the newest Lily# and its sound font copied in.
    $extensions = Join-Path $temp 'extensions'
    New-Item -ItemType Directory -Force $extensions | Out-Null
    foreach ($name in '^yotsuda\.lilysharp-\d', '^yotsuda\.lilysharp-soundfont-') {
        $found = Get-ChildItem (Join-Path $env:USERPROFILE '.vscode\extensions') -Directory | Where-Object Name -match $name |
            Sort-Object LastWriteTime | Select-Object -Last 1
        if ($found) { Copy-Item $found.FullName $extensions -Recurse } else { "no $name installed" }
    }
    $arguments += "--extensions-dir=$extensions"
} else {
    $arguments += '--disable-extensions'
}
$arguments += $work

$env:BAMEN_HOSTTEST_OUT = Join-Path $temp 'report.json'
$env:BAMEN_HOSTTEST_MP4 = $mp4
try {
    $process = Start-Process $exe -ArgumentList ($arguments | ForEach-Object { '"' + $_ + '"' }) -PassThru -Wait
    if (Test-Path $env:BAMEN_HOSTTEST_OUT) {
        $report = Get-Content $env:BAMEN_HOSTTEST_OUT -Raw | ConvertFrom-Json
        "VS Code $($report.vscode), Lily#: $($report.lily)"
        foreach ($check in $report.checks) {
            $sign = if ($check.ok) { 'ok  ' } else { 'FAIL' }
            "$sign $($check.name)"
            if (-not $check.ok) { "       " + ($check.detail | ConvertTo-Json -Depth 5 -Compress) }
        }
        foreach ($failure in $report.failures | Where-Object { $_ -like 'threw:*' }) { $failure }
        if ($report.mp4) { "MP4: $($report.mp4.size) bytes, boxes $($report.mp4.boxes -join ' '), filmed in $($report.filmSeconds) s" }
    } else {
        'FAIL no report was written (the test did not run)'
    }
    "exit code $($process.ExitCode)"

    # A frame from the middle of each shot, to look at.
    if ((Test-Path $mp4) -and (Get-Command ffmpeg -ErrorAction SilentlyContinue)) {
        $cameras = & (Join-Path $root 'Bamen.Cli\bin\Debug\net10.0\bamen.exe') cameras (Join-Path $root 'samples\dance.bamen') | ConvertFrom-Json
        foreach ($shot in $cameras.shots) {
            $at = ($shot.start + $shot.end) / 2
            $png = Join-Path $keep "shot$($shot.shot).png"
            cmd /d /s /c "ffmpeg -v error -y -ss $at -i `"$mp4`" -frames:v 1 -vf scale=640:-2 `"$png`" < NUL"
            if (Test-Path $png) { $png }
        }
        cmd /d /s /c "ffprobe -v error -show_entries stream=codec_type,codec_name,width,height,duration -of compact `"$mp4`" < NUL"
    }
} finally {
    Remove-Item Env:\BAMEN_HOSTTEST_OUT, Env:\BAMEN_HOSTTEST_MP4
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}
