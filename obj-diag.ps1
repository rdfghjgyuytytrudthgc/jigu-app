$root = "D:\DSH\jigu-app"
$obj = Join-Path $root "obj"
if (Test-Path $obj) { Remove-Item -Recurse -Force $obj }
New-Item -ItemType Directory -Path $obj | Out-Null
"obj = [$obj]"
"exists = $(Test-Path -LiteralPath $obj)"
$resourcesPath = Join-Path $obj "Jigu.Assets.resources"
"resourcesPath = [$resourcesPath]"
"len = $($resourcesPath.Length)"
try { $rw = [System.Resources.ResourceWriter]::new($resourcesPath); "ctor OK"; $rw.Close() } catch { "ctor ERR: $($_.Exception.Message)"; "  inner: $($_.Exception.InnerException)" }
