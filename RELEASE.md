# 稽古 · 发布到 GitHub Release（操作手册）

这份文档说明怎么把构建产物发布成 GitHub 上的 Release，让用户能直接下载安装包。
**配置已经写进仓库了**（`.github/workflows/release.yml`），你只需要打一个 tag。

---

## 一、最省事的做法：双击 `RELEASE.cmd`

仓库根目录的 `RELEASE.cmd` 会：

1. 从 `build.ps1` 读出当前版本号（当前为 `0.1.0`）；
2. 检查工作区是否干净（有未提交改动会先问你要不要继续）；
3. 建 tag `v<版本号>` 并推送；
4. 自动打开 GitHub 的 Actions 页面，让你看着它跑。

推送 tag 后，GitHub Actions 会自动完成：
**在 Windows 机器上拉取 WebView2 SDK → 构建 → 自检（`--selfcheck`）→ 创建 Release → 上传安装包 / 便携包 / SHA256 校验文件**。
全程约 3–6 分钟，不需要你做任何配置。

---

## 二、发布前必须做的两件事

### 1. 版本号要一致（三处）

| 文件 | 位置 |
| --- | --- |
| `build.ps1` | `$version = "0.1.0"`（**这一处是唯一真源**，CI 从这里读版本号） |
| `src/Update.cs` | `AppVer.Number = "0.1.0"`（程序内显示 + 更新比对） |
| `src/Installer.cs` | `SetupInfo.Version = "0.1.0"`（安装向导与卸载项） |

改完记得重新构建一次，确认 `dist\稽古\data_version.json` 里写的是新版本。

### 2. tag 名要和版本号对得上

tag 用 `v` + 版本号，例如版本 `0.1.0` → tag `v0.1.0`。
workflow 只在 `v*` 的 tag 上触发。

---

## 三、CI 里 WebView2 SDK 从哪来

构建只需要三个文件：

```
Microsoft.Web.WebView2.Core.dll
Microsoft.Web.WebView2.WinForms.dll
WebView2Loader.dll
```

托管 runner 上没有它们（本机是从 Office 安装目录里借的），所以 workflow 会：

1. 查询 NuGet 上 `Microsoft.Web.WebView2` 的最新版本；
2. 下载 `.nupkg` 并解压；
3. 把两个托管 DLL 从 `lib/net45/`、加载器从 `runtimes/win-x64/native/` 复制到仓库的 `lib/`；
4. 设置环境变量 `JIGU_WEBVIEW2_DIR=lib`，`build.ps1` 会优先从这里取。

**本地构建不受影响**：`build.ps1` 的查找顺序是
`JIGU_WEBVIEW2_DIR` → `<repo>\lib` → 本机 Office 安装目录。
所以你在自己机器上照旧 `build.ps1`，想让别的机器也能构建，只要把这三个 DLL 放进 `lib\`。

---

## 四、Release 里会有什么

| 资源 | 说明 |
| --- | --- |
| `jigu-installer-v0.1.0.exe` | 安装包（原文件名 `稽古-安装包.exe`，改成 ASCII 名是为了链接和下载方便） |
| `jigu-portable-v0.1.0.zip` | 便携包（解压即用） |
| `SHA256SUMS.txt` | 校验值，用户可 `certutil -hashfile` 比对 |

Release 正文是自动生成的固定内容（下载哪个、系统要求、校验方法、首次运行步骤），
写在 workflow 的 `Write release notes` 步骤里，要改直接改那段。

---

## 五、不想走 CI？手工发布也行

本机已经把产物构建好了：

```
dist\稽古-安装包.exe
dist\稽古-portable.zip
```

在 GitHub 网页上 **Releases → Draft a new release → 选择 tag → 上传这两个文件 → Publish** 即可。
CI 只是把这一步自动化了，不是必须的。

---

## 六、常见问题

| 现象 | 原因 / 处理 |
| --- | --- |
| Actions 里没有跑 | tag 不是 `v*` 形式，或者 tag 推送时仓库里还没有 `.github/workflows/release.yml`（先推一次 `main`，再推 tag） |
| `csc.exe not found` | runner 镜像换了。workflow 用的是 `windows-2022`，如改成别的镜像需确认自带 .NET Framework 4.x |
| `WebView2Loader.dll not found inside the SDK package` | NuGet 包结构调整了，按 Actions 日志里打印的包内文件清单改 `Fetch the WebView2 SDK` 那一步的路径 |
| `selfcheck` 失败 | 语料或界面资源没随包打进去。看 Actions 日志里 `selfcheck.txt` 的内容，里面有具体哪一项 FAIL |
| Release 创建失败 `Resource not accessible` | 仓库 `Settings → Actions → General → Workflow permissions` 要选 **Read and write permissions** |
| 想删掉某个 Release | Releases 页面右上角 Delete；tag 也要删的话：`git push --delete origin v0.1.0` |
