# FILE: jigu-app/build.ps1
# Jigu build script -- compiles a standalone Windows .exe using the in-box csc.exe.
# No Node, no Rust, no SDK required. This file is intentionally pure ASCII so that
# Windows PowerShell 5.1 (which reads .ps1 using the ANSI code page) parses it safely.
#
# Usage:  powershell -ExecutionPolicy Bypass -File build.ps1

$ErrorActionPreference = "Stop"
# Every path is derived from this script's own location, so the checkout can live anywhere.
# $PSScriptRoot is a reserved automatic variable and cannot be shadowed by host-injected
# variables, which is what the previous hardcoded block was guarding against.
if ([string]::IsNullOrEmpty($PSScriptRoot)) {
  throw "PSScriptRoot is empty -- run this with -File, not piped through stdin"
}
$root = $PSScriptRoot
$src  = Join-Path $root "src"
$web  = Join-Path $root "web"
$res  = Join-Path $root "resources"
$obj  = Join-Path $root "obj"
$dist = Join-Path $root "dist"

# --- product naming (built from code points to keep this file ASCII-only) ---
$APP_CH  = [string][char]0x7A3D + [string][char]0x53E4          # "Ji Gu"
$appDir  = Join-Path $dist ([string][char]0x7A3D + [string][char]0x53E4)
$exeName = $APP_CH + ".exe"
$zipName = $APP_CH + "-portable.zip"
$readmeName = [string][char]0x4F7F + [string][char]0x7528 + [string][char]0x8BF4 + [string][char]0x660E + ".txt"

function Write-Step($t) { Write-Host ""; Write-Host "=== $t ===" -ForegroundColor Cyan }
function Write-Ok($t)   { Write-Host "  [OK] $t" -ForegroundColor Green }
function Write-Warn2($t){ Write-Host "  [!] $t" -ForegroundColor Yellow }

# ---------------------------------------------------------------- 0. toolchain
Write-Step "0/6 locate in-box compiler and WebView2 runtime"
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { throw "csc.exe not found: $csc" }
Write-Ok "csc: $csc"

$wvCandidates = @(
  # Repo-local first: on a machine that has none of the office installs below,
  # dropping the two DLLs (plus WebView2Loader.dll) into <repo>\lib makes the build work.
  (Join-Path $PSScriptRoot "lib"),
  "C:\Program Files\Microsoft OfficePLUS\4.1.0.5753\addin",
  "C:\Program Files\Microsoft OfficePLUS\4.1.0.1926\addin",
  "C:\Program Files\Microsoft Office\root\Office16",
  "C:\Program Files\Microsoft Office\root\Office16\ADDINS\Microsoft Power Query for Excel Integrated\bin"
)
$wvDir = $null
foreach ($cand in $wvCandidates) {
  if ((Test-Path (Join-Path $cand "Microsoft.Web.WebView2.Core.dll")) -and
      (Test-Path (Join-Path $cand "Microsoft.Web.WebView2.WinForms.dll"))) { $wvDir = $cand; break }
}
if (-not $wvDir) { throw "Microsoft.Web.WebView2.Core.dll not found on this machine" }
Write-Ok "WebView2 SDK: $wvDir"

$loaderCandidates = @(
  (Join-Path $PSScriptRoot "lib\WebView2Loader.dll"),
  "C:\Program Files\Microsoft OneDrive\26.163.0823.0004\WebView2Loader.dll",
  (Join-Path $wvDir "runtimes\win-x64\native\WebView2Loader.dll"),
  (Join-Path $wvDir "WebView2Loader.dll"),
  "C:\Program Files\Microsoft Office\root\Office16\WebView2Loader.dll",
  "C:\Program Files\Bambu Studio\WebView2Loader.dll"
)
$loader = $null
$bestVer = ""
foreach ($cand in $loaderCandidates) {
  if (-not (Test-Path $cand)) { continue }
  $ver = (Get-Item $cand).VersionInfo.FileVersion
  if ([string]::IsNullOrEmpty($bestVer)) { $loader = $cand; $bestVer = $ver; continue }
  $a = @(); foreach ($p in $ver.Split('.')) { $a += [int]$p }
  $b = @(); foreach ($p in $bestVer.Split('.')) { $b += [int]$p }
  while ($a.Count -lt 4) { $a += 0 }
  while ($b.Count -lt 4) { $b += 0 }
  for ($i = 0; $i -lt 4; $i++) {
    if ($a[$i] -gt $b[$i]) { $loader = $cand; $bestVer = $ver; break }
    if ($a[$i] -lt $b[$i]) { break }
  }
}
if (-not $loader) { throw "WebView2Loader.dll (x64) not found" }
Write-Ok ("WebView2 loader: " + $loader + "  v" + $bestVer)

# ---------------------------------------------------------------- 1. staging
Write-Step "1/6 clean old build output and icon cache, then stage assets"

# Remove obj / bin / previous outputs / previous icon files.
# Windows caches icons by file path, so stale builds or stale icon names can win.
foreach ($junk in @($obj,
                    "$root\bin",
                    "$root\Jigu.Assets.resources",
                    "$root\src\Jigu.resx",
                    (Join-Path $dist ($APP_CH + "-portable")))) {
  if (Test-Path -LiteralPath $junk) {
    Remove-Item -LiteralPath $junk -Recurse -Force -ErrorAction SilentlyContinue
  }
}
# Drop old icon files that lack a version suffix
try {
  Get-ChildItem -LiteralPath "$root\dist" -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_ -ne $null -and ($_.Name -eq "Jigu.ico" -or $_.Name -eq "app.ico") } |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }
} catch { }
Remove-Item -LiteralPath "$root\resources\app.ico" -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath "$root\resources\app.png" -Force -ErrorAction SilentlyContinue

# Regenerate the icon so every build carries the newest artwork
if (Test-Path "$root\tools\make-icon.ps1") {
  & powershell -NoProfile -ExecutionPolicy Bypass -File "$root\tools\make-icon.ps1" | Out-Null
  Write-Ok "icon regenerated (resources\app.ico)"
}
if (-not (Test-Path "$root\resources\app.ico")) { throw "icon generation failed" }

# Refresh the Windows icon cache (the user never has to do it by hand)
$iconCache = Join-Path $env:LOCALAPPDATA "IconCache.db"
if (Test-Path -LiteralPath $iconCache) {
  Remove-Item -LiteralPath $iconCache -Force -ErrorAction SilentlyContinue
}
Get-ChildItem (Join-Path $env:LOCALAPPDATA "Microsoft\Windows\Explorer") -Filter "iconcache*" -ErrorAction SilentlyContinue |
  ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }
Write-Ok "icon cache refreshed (no manual step needed by the user)"

New-Item -ItemType Directory -Path $obj | Out-Null
if (Test-Path $appDir) { Remove-Item -Recurse -Force $appDir }
New-Item -ItemType Directory -Path $appDir | Out-Null
$dataDir = Join-Path $res "data"
New-Item -ItemType Directory -Path $dataDir -Force | Out-Null

$webFiles = @("index.html", "styles.css", "ink.css", "app.js")
foreach ($f in $webFiles) {
  $from = Join-Path $web $f
  if (-not (Test-Path $from)) { throw "missing web asset: $from" }
}
Write-Ok "web assets found"

$utf8 = New-Object System.Text.UTF8Encoding($false)
$sha = [System.Security.Cryptography.SHA256]::Create()


# ---------------------------------------------------------------- 2. resources
Write-Step "2/6 embed assets as generated C# source (deterministic, no resource lookup)"
# Previous builds used ResourceWriter + ResourceManager, but ResourceManager could
# not retrieve the entries at runtime (GetObject returned null), leaving the memory
# index empty and making WebView2 report ERR_FILE_NOT_FOUND. Assets are now compiled
# in as base64 string constants: no resource lookup, no disk access, nothing to fail.
$assetList = New-Object System.Collections.ArrayList
[void]$assetList.Add(@("index.html",     (Join-Path $web "index.html")))
[void]$assetList.Add(@("styles.css",     (Join-Path $web "styles.css")))
[void]$assetList.Add(@("ink.css",        (Join-Path $web "ink.css")))
[void]$assetList.Add(@("app.js",         (Join-Path $web "app.js")))
[void]$assetList.Add(@("app.png",        (Join-Path $res "app.png")))
# Only the seed corpus is embedded, purely as an offline fallback when corpus.json is absent.
[void]$assetList.Add(@("seed.json",      (Join-Path $res "seed.json")))
# The label table is the bridge between the user language and the corpus annotation.
# Embedded as a fallback; an external labels.json next to the exe takes precedence.
[void]$assetList.Add(@("labels.json",     (Join-Path $res "labels.json")))
# Query-side stopwords: modern function words rare in a classical corpus, so IDF
# over-rewards them. Same external-first fallback as the label table.
[void]$assetList.Add(@("stopwords.json",  (Join-Path $res "stopwords.json")))
# Self-check expectations are embedded too: --selfcheck must be able to assert recall
# quality on a RELEASE build, where the tests\ folder is not shipped. A guardrail that
# silently skips itself is worse than no guardrail.
[void]$assetList.Add(@("selfcheck-cases.json", (Join-Path $root "tests\cases.json")))
$dataEntries = 1

$gen = New-Object System.Text.StringBuilder
[void]$gen.AppendLine("// <auto-generated> generated by build.ps1: embedded assets (base64)")
[void]$gen.AppendLine("// do not edit by hand; sources live in web\ and resources\.")
[void]$gen.AppendLine("// </auto-generated>")
[void]$gen.AppendLine("using System;")
[void]$gen.AppendLine("using System.Collections.Generic;")
[void]$gen.AppendLine("")
[void]$gen.AppendLine("namespace Jigu")
[void]$gen.AppendLine("{")
[void]$gen.AppendLine("    /// <summary>compiled-in asset table: relative path -> base64</summary>")
[void]$gen.AppendLine("    internal static class EmbeddedAssets")
[void]$gen.AppendLine("    {")
[void]$gen.AppendLine("        public static Dictionary<string, string> Table()")
[void]$gen.AppendLine("        {")
[void]$gen.AppendLine("            Dictionary<string, string> t = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);")
$totalBytes = 0
foreach ($pair in $assetList) {
  if (-not (Test-Path -LiteralPath $pair[1])) { throw ("missing asset: " + $pair[1]) }
  $bytes = [System.IO.File]::ReadAllBytes($pair[1])
  $totalBytes += $bytes.Length
  $b64 = [Convert]::ToBase64String($bytes)
  [void]$gen.AppendLine("            t[""" + $pair[0] + """] = """ + $b64 + """;")
}
[void]$gen.AppendLine("            return t;")
[void]$gen.AppendLine("        }")
[void]$gen.AppendLine("    }")
[void]$gen.AppendLine("}")
# ---- Merge all book files into ONE external corpus.json (data lives OUTSIDE the exe) ----
$corpusPath = Join-Path $appDir "corpus.json"
$corpusSb = New-Object System.Text.StringBuilder
[void]$corpusSb.Append('{"book":"corpus","items":[')
$firstDoc = $true
$totalDocs = 0
$dataFiles = Get-ChildItem -Path $dataDir -Filter *.json | Sort-Object Name

# Pass 1: collect every record together with its original text.
$all = New-Object System.Collections.ArrayList
$rawDocs = 0
foreach ($file in $dataFiles) {
  if ($file.Name -eq "version.json") { continue }
  $obj = [System.IO.File]::ReadAllText($file.FullName, $utf8) | ConvertFrom-Json
  $bookName = $obj.book
  $count = 0
  foreach ($it in $obj.items) {
    # pros/cons carry the authored pros-and-cons analysis shown in the UI panel.
    $rec = [ordered]@{
      book = $bookName; chapter = $it.chapter; title = $it.title
      original = $it.original; translation = $it.translation
      figures = $it.figures; decision = $it.decision; outcome = $it.outcome; themes = $it.themes
      pros = $it.pros; cons = $it.cons
    }
    [void]$all.Add(@([string]$it.original, $rec))
    $count++; $rawDocs++
  }
  Write-Host ("     {0,-12} {1,4} docs" -f $bookName, $count)
}

# Pass 2: drop records whose original text repeats a later one.
# The seed volume (00_...) sorts first and overlaps the period volumes, so "keep the last
# occurrence" keeps the period-volume copy, whose book attribution is the accurate one.
# Left alone, duplicates eat two of the three result slots for the same event.
# The seed FILE is untouched: it doubles as the embedded offline fallback.
$keep = New-Object "bool[]" $all.Count
$seenOrig = @{}
for ($i = $all.Count - 1; $i -ge 0; $i--) {
  $key = [string]$all[$i][0]
  if ($key.Length -eq 0) { $keep[$i] = $true; continue }
  if ($seenOrig.ContainsKey($key)) { continue }
  $seenOrig[$key] = 1
  $keep[$i] = $true
}
$skipped = 0
for ($i = 0; $i -lt $all.Count; $i++) {
  if (-not $keep[$i]) { $skipped++; continue }
  if (-not $firstDoc) { [void]$corpusSb.Append(',') }
  $firstDoc = $false
  [void]$corpusSb.Append(($all[$i][1] | ConvertTo-Json -Depth 6 -Compress))
  $totalDocs++
}
[void]$corpusSb.Append(']}')
[System.IO.File]::WriteAllText($corpusPath, $corpusSb.ToString(), $utf8)
$corpusHash = ($sha.ComputeHash([System.IO.File]::ReadAllBytes($corpusPath)) | ForEach-Object { $_.ToString('x2') }) -join ''
Write-Ok ("external corpus: " + $totalDocs + " docs, " + [math]::Round((Get-Item $corpusPath).Length/1KB,1) + " KB")
if ($skipped -gt 0) {
  Write-Warn2 ("deduped " + $skipped + " record(s) repeating an original text (" + $rawDocs + " raw -> " + $totalDocs + ")")
}

# ---- data_version.json next to the exe ----
# "files" are plain-file replacements: downloaded and written as-is, not merged by book.
# labels.json / stopwords.json live here so both tables can be extended without a new build.
$synPath = Join-Path $res "labels.json"
$synHash = (Get-FileHash -LiteralPath $synPath -Algorithm SHA256).Hash.ToLowerInvariant()
$stopPath = Join-Path $res "stopwords.json"
$stopHash = (Get-FileHash -LiteralPath $stopPath -Algorithm SHA256).Hash.ToLowerInvariant()
$dv = [ordered]@{
  version = "0.1.0"
  generated_at = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
  source_url = "https://github.com/alephpi/24histories-simplified-chinese"
  books = [ordered]@{ corpus = $corpusHash }
  files = [ordered]@{ "labels.json" = $synHash; "stopwords.json" = $stopHash }
  paths = [ordered]@{ corpus = "corpus.json"; "labels.json" = "labels.json"; "stopwords.json" = "stopwords.json" }
}
[System.IO.File]::WriteAllText((Join-Path $appDir "data_version.json"), ($dv | ConvertTo-Json -Depth 5), $utf8)
Write-Ok "data_version.json written"

# ---- embedded seed: first book only, as an offline fallback ----
$seedSrc = Join-Path $dataDir ("00_" + [string][char]0x79CD + [string][char]0x5B50 + ".json")
if (Test-Path $seedSrc) {
  Copy-Item $seedSrc (Join-Path $res "seed.json") -Force
  Write-Ok "embedded seed prepared (offline fallback only)"
}

# ---- app_version.json templates (for the developer to host) ----
$appPkg = [string][char]0x7A3D + [string][char]0x53E4 + "-setup.exe"
$avExample = [ordered]@{
  version = "0.1.0"; url = ("https://example.com/jigu/" + $appPkg); sha256 = ""
  notes = "External corpus with inverted index; dual-track hot update; OS self-adaptation."
  mandatory = $false
}
[System.IO.File]::WriteAllText((Join-Path $appDir "app_version.example.json"),
  ($avExample | ConvertTo-Json -Depth 4), $utf8)
Write-Ok "app_version example written"
$genPath = "$root\src\EmbeddedAssets.g.cs"
[System.IO.File]::WriteAllText($genPath, $gen.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Write-Ok ("assets embedded: web=8 data=" + $dataEntries + " total=" + [math]::Round($totalBytes/1KB,1) + " KB")
Write-Ok ("generated source: " + $genPath + " (" + [math]::Round((Get-Item $genPath).Length/1KB,1) + " KB)")

# ---------------------------------------------------------------- 3. compile
Write-Step "3/6 compile the executable"
$cscArgs = @(
  "/nologo", "/target:winexe", "/platform:anycpu", "/optimize+", "/utf8output",
  ("/out:" + (Join-Path $appDir $exeName)),
  "/reference:System.dll",
  "/reference:System.Core.dll",
  "/reference:System.Drawing.dll",
  "/reference:System.Windows.Forms.dll",
  "/reference:System.Net.dll",
  "/reference:System.Security.dll",
  ("/reference:" + (Join-Path $wvDir "Microsoft.Web.WebView2.Core.dll")),
  ("/reference:" + (Join-Path $wvDir "Microsoft.Web.WebView2.WinForms.dll"))
)
$iconPath = Join-Path $res "app.ico"
if (Test-Path $iconPath) { $cscArgs += ("/win32icon:" + $iconPath) }
# Without a supportedOS manifest, Environment.OSVersion reports 6.2.9200 (Windows 8)
# on Windows 10/11, and the self-check report shows that wrong string to users.
$manifestPath = Join-Path $res "app.manifest"
if (Test-Path $manifestPath) { $cscArgs += ("/win32manifest:" + $manifestPath) }
else { throw ("manifest missing: " + $manifestPath) }
# Compile every C# source in src\ (Host.cs, Corpus.cs, Json.cs, Update.cs) plus the
# generated asset tables, so new modules are picked up automatically.
Get-ChildItem -Path $src -Filter *.cs | Sort-Object Name | ForEach-Object {
  # Exclude the installer sources (they define their own Main) and the generated
  # *.g.cs tables, which are appended explicitly below.
  $skip = ($_.Name -eq "Installer.cs") -or ($_.Name -eq "SetupAssemblyInfo.cs") -or ($_.Name.EndsWith(".g.cs"))
  if (-not $skip) {
    $cscArgs += $_.FullName
  }
}
# ---- Stage the runtime files BEFORE compiling, so the embedded payload (below) is complete ----
Copy-Item (Join-Path $wvDir "Microsoft.Web.WebView2.Core.dll")     $appDir -Force
Copy-Item (Join-Path $wvDir "Microsoft.Web.WebView2.WinForms.dll") $appDir -Force
Copy-Item $loader (Join-Path $appDir "WebView2Loader.dll") -Force
$portableData = Join-Path $appDir "data"
New-Item -ItemType Directory -Path $portableData -Force | Out-Null
# Keep the delivered folder clean: drop runtime caches and stale artifacts that must never
# ship with the product (they are recreated on the user's machine as needed).
foreach ($junk in @("jigu-data", "pending", "Jigu-1.4.ico", "version.json", "selftest.txt", "update-sim-report.txt", "jigu-debug.log", "app_version.test.json", "app_version.example.json", $readmeName)) {
  $p = Join-Path $appDir $junk
  if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Recurse -Force -ErrorAction SilentlyContinue }
}
# Merge the per-book JSON into the external corpus later (corpus.json is the runtime source
# of truth); data\ is still shipped as an editable reference set for the user.
Copy-Item (Join-Path $dataDir "*") $portableData -Recurse -Force
# Readme for the end user (UTF-8 source copied verbatim; literal Chinese name).
Copy-Item "$root\resources\readme-installed.txt" (Join-Path $appDir $readmeName) -Force
Copy-Item "$root\resources\app.ico" (Join-Path $appDir "Jigu-1.6.ico") -Force
[System.IO.File]::WriteAllText((Join-Path $appDir "update_base.txt"),
  "https://raw.githubusercontent.com/alephpi/24histories-data/main",
  (New-Object System.Text.UTF8Encoding($false)))
Write-Ok ("staged runtime files: " + (Get-ChildItem $appDir -Recurse -File).Count + " files")
$cscArgs += $genPath
# The app also carries the installer payload: hot update needs the CURRENT exe to be able to
# unpack a newly downloaded release (--extract-to). Generated before this compile so it is
# always in sync with the files staged above.
$payloadFiles = Get-ChildItem -Path $appDir -Recurse -File | Sort-Object FullName
$pl = New-Object System.Text.StringBuilder
[void]$pl.AppendLine("// <auto-generated> generated by build.ps1: installer payload (base64)")
[void]$pl.AppendLine("// do not edit by hand.")
[void]$pl.AppendLine("using System;")
[void]$pl.AppendLine("using System.Collections.Generic;")
[void]$pl.AppendLine("")
[void]$pl.AppendLine("namespace Jigu")
[void]$pl.AppendLine("{")
[void]$pl.AppendLine("    internal static partial class InstallerPayload")
[void]$pl.AppendLine("    {")
[void]$pl.AppendLine("        public static Dictionary<string, string> Table()")
[void]$pl.AppendLine("        {")
[void]$pl.AppendLine("            Dictionary<string, string> t = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);")
$plBytes = 0
foreach ($file in $payloadFiles) {
  $rel = $file.FullName.Substring($appDir.Length + 1).Replace('\', '/')
  $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
  $plBytes += $bytes.Length
  $b64 = [Convert]::ToBase64String($bytes)
  [void]$pl.AppendLine("            t[""" + $rel + """] = """ + $b64 + """;")
}
[void]$pl.AppendLine("            return t;")
[void]$pl.AppendLine("        }")
[void]$pl.AppendLine("    }")
[void]$pl.AppendLine("}")
$plPath = "$root\src\InstallerPayload.g.cs"
[System.IO.File]::WriteAllText($plPath, $pl.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Write-Ok ("embedded payload: " + $payloadFiles.Count + " files, " + [math]::Round($plBytes/1KB,1) + " KB")
$cscArgs += $plPath
# NOTE: InstallerPayload.g.cs is intentionally NOT part of the app build. It is generated
# later (step 6) and only the setup wizard / hot-swap path needs it, so the app compiles
# without it.

$cscOut = & $csc $cscArgs 2>&1
$cscOut | ForEach-Object { Write-Host "     $_" }
if ($LASTEXITCODE -ne 0) { throw ("compile failed, csc exit " + $LASTEXITCODE) }
Write-Ok ("compiled: " + (Join-Path $appDir $exeName))
# ---- Regenerate the payload now that the app exe exists, so the setup wizard and the
# hot-swap path carry a COMPLETE release (including the executable itself) ----
$payloadFiles = Get-ChildItem -Path $appDir -Recurse -File | Sort-Object FullName
$pl = New-Object System.Text.StringBuilder
[void]$pl.AppendLine("// <auto-generated> generated by build.ps1: installer payload (base64)")
[void]$pl.AppendLine("// do not edit by hand.")
[void]$pl.AppendLine("using System;")
[void]$pl.AppendLine("using System.Collections.Generic;")
[void]$pl.AppendLine("")
[void]$pl.AppendLine("namespace Jigu")
[void]$pl.AppendLine("{")
[void]$pl.AppendLine("    internal static partial class InstallerPayload")
[void]$pl.AppendLine("    {")
[void]$pl.AppendLine("        public static Dictionary<string, string> Table()")
[void]$pl.AppendLine("        {")
[void]$pl.AppendLine("            Dictionary<string, string> t = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);")
$plBytes = 0
foreach ($file in $payloadFiles) {
  $rel = $file.FullName.Substring($appDir.Length + 1).Replace('\', '/')
  $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
  $plBytes += $bytes.Length
  $b64 = [Convert]::ToBase64String($bytes)
  [void]$pl.AppendLine("            t[""" + $rel + """] = """ + $b64 + """;")
}
[void]$pl.AppendLine("            return t;")
[void]$pl.AppendLine("        }")
[void]$pl.AppendLine("    }")
[void]$pl.AppendLine("}")
$plPath = "$root\src\InstallerPayload.g.cs"
[System.IO.File]::WriteAllText($plPath, $pl.ToString(), (New-Object System.Text.UTF8Encoding($false)))
$hasExe = ($payloadFiles | Where-Object { $_.Name -like "*.exe" }).Count
Write-Ok ("final payload: " + $payloadFiles.Count + " files (exe=" + $hasExe + "), " + [math]::Round($plBytes/1KB,1) + " KB")


# ---------------------------------------------------------------- 4. package
Write-Step "4/6 assemble the portable folder"
Copy-Item (Join-Path $wvDir "Microsoft.Web.WebView2.Core.dll")     $appDir -Force
Copy-Item (Join-Path $wvDir "Microsoft.Web.WebView2.WinForms.dll") $appDir -Force
Copy-Item $loader (Join-Path $appDir "WebView2Loader.dll") -Force
Write-Ok "WebView2 runtime files bundled"

$portableData = Join-Path $appDir "data"
New-Item -ItemType Directory -Path $portableData -Force | Out-Null
# Keep the delivered folder clean: drop runtime caches and stale artifacts that must never
# ship with the product (they are recreated on the user's machine as needed).
foreach ($junk in @("jigu-data", "pending", "Jigu-1.4.ico", "version.json", "selftest.txt", "update-sim-report.txt", "jigu-debug.log", "app_version.test.json", "app_version.example.json", $readmeName)) {
  $p = Join-Path $appDir $junk
  if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Recurse -Force -ErrorAction SilentlyContinue }
}
# The per-book JSON stays alongside the merged corpus.json: corpus.json is what the engine
# reads at runtime, while data\ is an editable reference set for the user.
Copy-Item (Join-Path $dataDir "*") $portableData -Recurse -Force
# Readme for the end user (UTF-8 source copied verbatim; literal Chinese name).
Copy-Item "$root\resources\readme-installed.txt" (Join-Path $appDir $readmeName) -Force
$countData = (Get-ChildItem $portableData -Recurse -Filter *.json | Measure-Object).Count
Write-Ok ("corpus files bundled: " + $countData)

[System.IO.File]::WriteAllText((Join-Path $appDir "data_base.txt"),
  "https://raw.githubusercontent.com/alephpi/24histories-data/main", $utf8)


# ---------------------------------------------------------------- 5. zip
Write-Step "5/6 create the portable zip"
$zip = Join-Path $dist $zipName
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($appDir, $zip)
Write-Ok ("zip: " + $zip)

# ---------------------------------------------------------------- 6. installer
Write-Step "6/6 build the Windows installer (setup wizard)"
# Inno Setup is not available on this machine and the sandbox has no network, so the
# installer is produced with the same in-box csc.exe. It provides an equivalent
# experience: Chinese wizard, per-user default path, desktop + start menu shortcuts,
# uninstall entry in Windows Settings, and a silent mode used for automated testing.
$iconInApp = Join-Path $appDir "Jigu-1.6.ico"
Copy-Item "$root\resources\app.ico" $iconInApp -Force
Write-Ok "app icon placed for shortcuts and uninstall entry"

# Installer payload: every file of the portable folder (so nothing is missed)
# Reuse the payload generated in step 3 (it already contains every staged file, including
# the freshly compiled exe, so the wizard and the hot-swap path stay in sync).
Write-Ok ("installer payload reused: " + $payloadFiles.Count + " files, " + [math]::Round($plBytes/1KB,1) + " KB")

$setupName = $APP_CH + "-" + [string][char]0x5B89 + [string][char]0x88C5 + [string][char]0x5305 + ".exe"
$setupPath = Join-Path $dist $setupName
if (Test-Path $setupPath) { Remove-Item $setupPath -Force }
$setupArgs = @(
  "/nologo", "/target:winexe", "/platform:anycpu", "/optimize+", "/utf8output",
  ("/out:" + $setupPath),
  ("/win32icon:" + $iconInApp),
  ("/win32manifest:" + (Join-Path $res "app.manifest")),
  "/reference:System.dll",
  "/reference:System.Core.dll",
  "/reference:System.Drawing.dll",
  "/reference:System.Windows.Forms.dll"
)
$setupArgs += "$root\src\Installer.cs"
$setupArgs += "$root\src\SetupAssemblyInfo.cs"
$setupArgs += "$root\src\Host.cs"
$setupArgs += "$root\src\Update.cs"
$setupArgs += "$root\src\Json.cs"
$setupArgs += "$root\src\MiniJson.cs"
$setupArgs += "$root\src\Corpus.cs"
$setupArgs += "$root\src\PayloadExt.cs"
$setupArgs += $plPath
# The wizard reuses Host.cs, which references WebView2 types, so the same references apply.
$setupArgs += ("/reference:" + (Join-Path $wvDir "Microsoft.Web.WebView2.Core.dll"))
$setupArgs += ("/reference:" + (Join-Path $wvDir "Microsoft.Web.WebView2.WinForms.dll"))
$setupArgs += $genPath
$setupOut = & $csc $setupArgs 2>&1
$setupOut | ForEach-Object { Write-Host "     $_" }
if ($LASTEXITCODE -ne 0) { throw ("installer compile failed, csc exit " + $LASTEXITCODE) }
Write-Ok ("installer: " + $setupPath + " (" + [math]::Round((Get-Item $setupPath).Length/1KB,1) + " KB)")

Write-Host ""
Write-Host "BUILD OK" -ForegroundColor Green
Write-Host ("  portable  : " + (Join-Path $appDir $exeName)) -ForegroundColor Green
Write-Host ("  zip       : " + $zip) -ForegroundColor Green
Write-Host ("  installer : " + $setupPath) -ForegroundColor Green
Get-ChildItem $appDir | Select-Object Name, @{n = "KB"; e = { [math]::Round($_.Length / 1KB, 1) } } | Format-Table -AutoSize



