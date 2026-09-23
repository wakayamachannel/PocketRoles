# Re-extract the official Among Us translation tables (read-only on the game folder). See HOW.md.
# Usage:  powershell -ExecutionPolicy Bypass -File tools\terms\run.ps1 [-GameDir "C:\...\Among Us"] [-OutDir <folder>]
# It COPIES referencedatagroup_assets_all_*.bundle to <OutDir>\refdata.bundle, then runs TermsExtract (AssetsTools.NET),
# which writes official-*.tsv, glossary.tsv and out\ into <OutDir> (default: tools\terms\local, git-ignored: the game's
# own text is Innersloth's and is never committed). check-terms.ps1 reads <OutDir>\glossary.tsv when it is there.
param([string]$GameDir = "C:\Users\riotgames\Desktop\Among Us", [string]$OutDir = "")
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $OutDir) { $OutDir = Join-Path $here "local" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$aa = Join-Path $GameDir "Among Us_Data\StreamingAssets\aa\Steam\StandaloneWindows"
$found = @(Get-ChildItem -Path $aa -Filter "referencedatagroup_assets_all_*.bundle")
if ($found.Count -eq 0) { throw "referencedatagroup bundle not found under $aa" }
# after a game update an old bundle may stay next to the new one: never guess which one the game reads
if ($found.Count -gt 1) { throw ("more than one referencedatagroup bundle under {0}: {1}. Check which one the game uses (catalog.json) and copy that one by hand." -f $aa, (($found | ForEach-Object { $_.Name + " " + $_.LastWriteTime.ToString("yyyy-MM-dd HH:mm") }) -join ", ")) }
$src = $found[0]
$copy = Join-Path $OutDir "refdata.bundle"
Copy-Item -LiteralPath $src.FullName -Destination $copy -Force
"source: " + $src.FullName
"sha256: " + (Get-FileHash $copy).Hash
$dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
& $dotnet build (Join-Path $here "TermsExtract\TermsExtract.csproj") -c Release -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "build failed" }
$out = Join-Path $OutDir "out"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
& $dotnet (Join-Path $here "TermsExtract\bin\Release\net8.0\TermsExtract.dll") $copy $out $OutDir (Join-Path $here "glossary-keys.tsv")
if ($LASTEXITCODE -ne 0) { throw "extract failed" }
