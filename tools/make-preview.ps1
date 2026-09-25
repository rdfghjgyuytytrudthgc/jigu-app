# FILE: jigu-app/tools/make-preview.ps1
# 生成一份离线预览页：把 web/ 前端与真实语料内联进单个 HTML，
# 直接用浏览器打开就能看界面（含「决策分析」栏），不需要跑 exe、不需要 WebView2。
# 用法: powershell -File tools\make-preview.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "tests\preview-ui.html"

function Read-Utf8([string]$p) { return [System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8) }

$html = Read-Utf8 (Join-Path $root "web\index.html")
$css1 = Read-Utf8 (Join-Path $root "web\styles.css")
$css2 = Read-Utf8 (Join-Path $root "web\ink.css")
$app = Read-Utf8 (Join-Path $root "web\app.js")

# 语料：合并全部书卷。pros/cons 必须带上，否则分析栏在预览里是空的。
$items = New-Object System.Collections.ArrayList
Get-ChildItem (Join-Path $root "resources\data") -Filter *.json | Sort-Object Name | ForEach-Object {
  if ($_.Name -eq "version.json") { return }
  $o = Read-Utf8 $_.FullName | ConvertFrom-Json
  foreach ($it in $o.items) {
    [void]$items.Add([ordered]@{
      book = $o.book; chapter = $it.chapter; title = $it.title
      original = $it.original; translation = $it.translation
      figures = $it.figures; decision = $it.decision; outcome = $it.outcome
      themes = $it.themes; pros = $it.pros; cons = $it.cons
    })
  }
}
$corpusJs = ($items | ConvertTo-Json -Depth 6 -Compress)
Write-Host ("corpus items = " + $items.Count)

# app.js 在没有宿主桥（hostObjects 不存在）时走离线兜底，那条路只做一件事：
# fetch("corpus.json")。预览页把语料直接喂给这个 fetch，离线引擎就跑起来了，
# 渲染路径与真机完全一致——预览看到的排版就是用户看到的排版。
# （离线引擎精度低于内置 C# 引擎，这里只看版式。）
$prelude = @"
window.__PREVIEW__ = true;
window.__CORPUS__ = { book: "corpus", items: $corpusJs };
window.fetch = function (url) {
  if (String(url).indexOf("corpus.json") >= 0) {
    return Promise.resolve({ json: function () { return Promise.resolve(window.__CORPUS__); } });
  }
  return Promise.reject(new Error("preview: no network"));
};
"@

# 页面加载后自动填一个示例问题并点「稽古一问」，省得每次手输。
$boot = @"
(function () {
  var tries = 0;
  function go() {
    var ta = document.getElementById("situation");
    var btn = document.getElementById("go");
    if ((!ta || !btn || btn.disabled) && tries++ < 40) return setTimeout(go, 250);
    if (!ta || !btn) return;
    ta.value = "我和合伙人互相猜忌，团队里有人不断传话挑拨，骨干已经想走了";
    btn.click();
  }
  window.addEventListener("load", function () { setTimeout(go, 250); });
})();
"@

$html = $html.Replace('<link rel="stylesheet" href="styles.css" />', "<style>`n$css1`n</style>")
$html = $html.Replace('<link rel="stylesheet" href="ink.css" />', "<style>`n$css2`n</style>")
$html = $html.Replace('<script src="app.js"></script>', "<script>`n$prelude`n$app`n</script>`n<script>`n$boot`n</script>")
$html = $html.Replace('<title>稽古</title>', '<title>稽古 · 界面预览</title>')

[System.IO.File]::WriteAllText($out, $html, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("preview: " + $out + "  " + [math]::Round((Get-Item $out).Length/1KB,1) + " KB")
