param(
    [string]$Configuration = 'Debug',   # Debug or Release
    [switch]$NoTest,                    # build only: skip the tests and the check of the sample
    [switch]$Publish,                   # also publish bamen.exe self-contained for win-x64 into -Out
    [string]$Out = "$PSScriptRoot\..\out\cli"
)
# Builds the solution (Bamen.slnx), runs the tests, then `bamen check` on samples\dance.bamen: the sample
# is read through `shosa clip` (BAMEN_SHOSA, the PATH, then the Shosa extension for VS Code), so this also
# says whether a shosa is found. With -Publish, bamen.exe goes to out\cli. Publishes nothing anywhere else.
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$Out = [IO.Path]::GetFullPath($Out)

"building ($Configuration)"
cmd /d /s /c "dotnet build `"$root\Bamen.slnx`" -c $Configuration -v q -nologo < NUL"
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

if (-not $NoTest) {
    "testing"
    cmd /d /s /c "dotnet test `"$root\Bamen.slnx`" -c $Configuration --no-build -v q -nologo < NUL"
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed" }

    # The whole way through, with the real shosa: the sample has no problems.
    $bamen = "$root\Bamen.Cli\bin\$Configuration\net10.0\bamen.exe"
    "checking samples\dance.bamen"
    & $bamen check "$root\samples\dance.bamen"
    if ($LASTEXITCODE -ne 0) { throw "bamen check samples\dance.bamen failed (exit $LASTEXITCODE)" }
}

if ($Publish) {
    if (Test-Path $Out) { Remove-Item -Recurse -Force $Out }
    "publishing bamen.exe into $Out"
    cmd /d /s /c "dotnet publish `"$root\Bamen.Cli`" -c Release -r win-x64 --self-contained -o `"$Out`" -v q -nologo < NUL"
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
    Get-ChildItem $Out -Filter *.pdb | Remove-Item
    $size = (Get-ChildItem $Out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
    "  {0:F1} MB" -f $size
}
"done"
