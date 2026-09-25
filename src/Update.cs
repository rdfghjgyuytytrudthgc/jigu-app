// FILE: jigu-app/src/Update.cs
// 双路热更新：史料数据增量更新 + 程序本体热替换；以及系统自适应检测。
//
// 路径策略：一律以 AppDomain.CurrentDomain.BaseDirectory 为基准，
// 绝不出现 C:\Program Files 这类绝对路径，因此安装到任何目录都能工作。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Jigu
{
    /// <summary>远端 data_version.json / 本地 data_version.json</summary>
    internal sealed class RemoteManifest
    {
        public string Version = "";
        public string SourceUrl = "";
        public Dictionary<string, string> Books = new Dictionary<string, string>();
        /// <summary>
        /// 平文件（name -> sha256）：直接下载覆盖，不参与「按书切分再合并」。
        /// 目前用于 labels.json —— 情境标签表是全项目最需要频繁迭代的资产
        /// （一个词的缺口就让某类提问检索不到东西），它不该每次都靠重新编译 exe 来更新。
        /// 相对路径仍复用 Paths 映射。
        /// </summary>
        public Dictionary<string, string> Files = new Dictionary<string, string>();
        public Dictionary<string, string> Paths = new Dictionary<string, string>();
    }

    /// <summary>远端 app_version.json</summary>
    internal sealed class AppManifest
    {
        public string Version = "";
        public string Url = "";
        public string Sha256 = "";
        public string Notes = "";
        public bool Mandatory;
    }

    internal static class Net
    {
        public const string DefaultBase =
            "https://raw.githubusercontent.com/alephpi/24histories-data/main";

        public static string BaseUrl()
        {
            string v = Environment.GetEnvironmentVariable("JIGU_UPDATE_BASE");
            if (!string.IsNullOrEmpty(v)) return v.TrimEnd('/');
            try
            {
                string cfg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update_base.txt");
                if (File.Exists(cfg))
                {
                    string t = File.ReadAllText(cfg, Encoding.UTF8).Trim();
                    if (t.Length > 0) return t.TrimEnd('/');
                }
            }
            catch { }
            return DefaultBase;
        }

        public static WebClient Client()
        {
            WebClient c = new WebClient();
            c.Headers.Add("User-Agent", "jigu/" + AppVer.Number);
            c.Encoding = Encoding.UTF8;
            try { c.Proxy = WebRequest.DefaultWebProxy; } catch { }
            return c;
        }

        /// <summary>下载字节；失败/超时返回 null（调用方一律静默降级）</summary>
        public static byte[] Get(WebClient client, string url, int timeoutMs)
        {
            // file:// support: the "update source" may be a local folder. Handy for offline
            // end-to-end testing and for distributing updates from a network share.
            if (url != null && url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string path = new Uri(url).LocalPath;
                    return File.Exists(path) ? File.ReadAllBytes(path) : null;
                }
                catch (Exception ex) { Log.Error("file get " + url, ex); return null; }
            }

            ManualResetEvent done = new ManualResetEvent(false);
            byte[] result = null;
            Exception error = null;
            client.DownloadDataCompleted += delegate(object s, DownloadDataCompletedEventArgs e)
            {
                if (e.Error != null) error = e.Error; else result = e.Result;
                done.Set();
            };
            try { client.DownloadDataAsync(new Uri(url)); }
            catch (Exception ex) { error = ex; }
            if (error != null) return null;
            if (!done.WaitOne(timeoutMs)) { try { client.CancelAsync(); } catch { } return null; }
            return result;
        }

        public static string Sha256Hex(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(bytes);
                StringBuilder sb = new StringBuilder(64);
                for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        public static string Sha256File(string path)
        {
            try
            {
                if (!File.Exists(path)) return "";
                return Sha256Hex(File.ReadAllBytes(path));
            }
            catch { return ""; }
        }
    }

    internal static class AppVer
    {
        public const string Number = "0.1.0";
    }

    /// <summary>系统自适应：判断 Windows 版本、架构与可用的界面渲染方式</summary>
    internal static class Sys
    {
        public static string Describe()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                System.Version v = Environment.OSVersion.Version;
                sb.Append("Windows ").Append(v.Major).Append('.').Append(v.Minor).Append('.')
                  .Append(v.Build);
                if (v.Major == 10 && v.Build >= 22000) sb.Append("(Win11)");
                else if (v.Major == 10) sb.Append("(Win10)");
                else if (v.Major == 6 && v.Minor == 3) sb.Append("(8.1)");
                else if (v.Major == 6 && v.Minor == 2) sb.Append("(8)");
                else if (v.Major == 6 && v.Minor == 1) sb.Append("(7)");
            }
            catch { sb.Append("Windows(unknown)"); }
            sb.Append(" / ").Append(Environment.Is64BitOperatingSystem ? "x64" : "x86");
            sb.Append(" / .NET ").Append(Environment.Version);
            return sb.ToString();
        }

        /// <summary>是否满足最低要求（Win7 SP1 及以上）</summary>
        public static bool IsSupported()
        {
            try
            {
                System.Version v = Environment.OSVersion.Version;
                if (v.Major > 6) return true;
                if (v.Major == 6 && v.Minor >= 1) return true;
                return false;
            }
            catch { return true; }
        }

        /// <summary>系统里是否有可用的 Edge / Chrome（WebView2 失效时的降级出口）</summary>
        public static string FindFallbackBrowser()
        {
            string[] cands =
            {
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
            };
            foreach (string c in cands)
            {
                try { if (File.Exists(c)) return c; }
                catch { }
            }
            return null;
        }
    }

    /// <summary>数据更新：比对远端 data_version.json，只下载变化的史料文件并合并进 corpus.json</summary>
    internal static class DataUpdater
    {
        public sealed class Report
        {
            public bool Checked;
            public string LocalVersion = "";
            public string RemoteVersion = "";
            public List<string> Changed = new List<string>();
            /// <summary>需要更新的平文件（区别于按书合并的 Changed）</summary>
            public List<string> FilesChanged = new List<string>();
            public List<string> Failed = new List<string>();
            public List<string> Merged = new List<string>();
            public List<string> FilesUpdated = new List<string>();
            public string Message = "";
        }

        /// <summary>读取本地 data_version.json（exe 同目录）</summary>
        private static RemoteManifest ReadLocal(string baseDir)
        {
            RemoteManifest m = new RemoteManifest();
            string p = Path.Combine(baseDir, Corpus.DataVersionFile);
            if (!File.Exists(p)) return m;
            try
            {
                Dictionary<string, object> root = MiniJson.Parse(File.ReadAllText(p, Encoding.UTF8))
                    as Dictionary<string, object>;
                if (root == null) return m;
                m.Version = MiniJson.Str(root, "version");
                m.SourceUrl = MiniJson.Str(root, "source_url");
                object books, paths, files;
                if (root.TryGetValue("books", out books) && books is Dictionary<string, object>)
                    foreach (KeyValuePair<string, object> kv in (Dictionary<string, object>)books)
                        m.Books[kv.Key] = Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
                if (root.TryGetValue("files", out files) && files is Dictionary<string, object>)
                    foreach (KeyValuePair<string, object> kv in (Dictionary<string, object>)files)
                        m.Files[kv.Key] = Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
                if (root.TryGetValue("paths", out paths) && paths is Dictionary<string, object>)
                    foreach (KeyValuePair<string, object> kv in (Dictionary<string, object>)paths)
                        m.Paths[kv.Key] = Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) { Log.Error("read local data_version", ex); }
            return m;
        }

        public static Report Check(string baseDir)
        {
            Report r = new Report();
            RemoteManifest local = ReadLocal(baseDir);
            r.LocalVersion = local.Version;
            byte[] bytes = Net.Get(Net.Client(), Net.BaseUrl() + "/data_version.json", 7000);
            if (bytes == null) { r.Message = "未连上数据源（离线使用本地史料）"; return r; }
            RemoteManifest remote;
            try { remote = ParseManifest(Encoding.UTF8.GetString(bytes)); }
            catch (Exception ex) { Log.Error("parse remote data_version", ex); r.Message = "远端版本文件无法解析"; return r; }

            r.Checked = true;
            r.RemoteVersion = remote.Version;
            string corpusPath = Path.Combine(baseDir, Corpus.DataFileName);
            foreach (KeyValuePair<string, string> kv in remote.Books)
            {
                string have = local.Books.ContainsKey(kv.Key) ? local.Books[kv.Key] : "";
                if (kv.Key == "corpus") have = Net.Sha256File(corpusPath);
                if (!string.Equals(have, kv.Value, StringComparison.OrdinalIgnoreCase)) r.Changed.Add(kv.Key);
            }
            // 平文件：拿远端清单里的 hash 比对本机实际文件的 hash
            foreach (KeyValuePair<string, string> kv in remote.Files)
            {
                string name = Path.GetFileName(kv.Key);      // 防目录穿越
                if (string.IsNullOrEmpty(name)) continue;
                string have = Net.Sha256File(Path.Combine(baseDir, name));
                if (!string.Equals(have, kv.Value, StringComparison.OrdinalIgnoreCase))
                    r.FilesChanged.Add(name);
            }

            int totalChanged = r.Changed.Count + r.FilesChanged.Count;
            r.Message = totalChanged == 0
                ? "史料库已是最新（v" + remote.Version + "）"
                : ("发现 " + totalChanged + " 项更新");
            Log.Write("data check: local=" + local.Version + " remote=" + remote.Version
                + " changed=" + r.Changed.Count + " files=" + r.FilesChanged.Count);
            return r;
        }

        private static RemoteManifest ParseManifest(string text)
        {
            RemoteManifest m = new RemoteManifest();
            Dictionary<string, object> root = MiniJson.Parse(text) as Dictionary<string, object>;
            if (root == null) return m;
            m.Version = MiniJson.Str(root, "version");
            m.SourceUrl = MiniJson.Str(root, "source_url");
            object books, paths, files;
            if (root.TryGetValue("books", out books) && books is Dictionary<string, object>)
                foreach (KeyValuePair<string, object> kv in (Dictionary<string, object>)books)
                    m.Books[kv.Key] = Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
            if (root.TryGetValue("files", out files) && files is Dictionary<string, object>)
                foreach (KeyValuePair<string, object> kv in (Dictionary<string, object>)files)
                    m.Files[kv.Key] = Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
            if (root.TryGetValue("paths", out paths) && paths is Dictionary<string, object>)
                foreach (KeyValuePair<string, object> kv in (Dictionary<string, object>)paths)
                    m.Paths[kv.Key] = Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
            return m;
        }

        /// <summary>
        /// 执行增量更新：下载变化的史料文件 -> 校验 -> 合并进 corpus.json -> 更新本地清单。
        /// 任何一步失败都只记录、不打断用户。
        /// </summary>
        public static Report Apply(string baseDir, Report plan)
        {
            Report r = plan ?? new Report();
            if (!r.Checked) { return r; }
            string corpusPath = Path.Combine(baseDir, Corpus.DataFileName);
            byte[] current = File.Exists(corpusPath) ? File.ReadAllBytes(corpusPath) : null;
            Dictionary<string, List<string>> bookDocs =
                current == null ? new Dictionary<string, List<string>>()
                                : SplitByBook(current);

            WebClient client = Net.Client();
            // 用远端清单里的 paths 映射取相对路径；没有映射时才回退到 <book>/<book>.json
            RemoteManifest remoteManifest = null;
            try
            {
                byte[] mv = Net.Get(client, Net.BaseUrl() + "/data_version.json", 7000);
                if (mv != null) remoteManifest = ParseManifest(Encoding.UTF8.GetString(mv));
            }
            catch (Exception ex) { Log.Error("reload remote manifest", ex); }

            foreach (string book in r.Changed)
            {
                string rel = null;
                if (remoteManifest != null) remoteManifest.Paths.TryGetValue(book, out rel);
                if (string.IsNullOrEmpty(rel))
                    rel = book.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                        ? book : (book + "/" + book + ".json");
                byte[] data = Net.Get(client, Net.BaseUrl() + "/" + rel, 20000);
                if (data == null) { r.Failed.Add(book); Log.Write("data fetch failed: " + rel); continue; }
                if (data.Length < 32) { r.Failed.Add(book); continue; }
                // 必须是合法语料：以 [ 或 { 开头，且含 items 或 book
                string head = Encoding.UTF8.GetString(data, 0, Math.Min(256, data.Length));
                if (head.TrimStart().Length == 0) { r.Failed.Add(book); continue; }
                char first = head.TrimStart()[0];
                if (first != '[' && first != '{') { r.Failed.Add(book); continue; }

                string name = Path.GetFileNameWithoutExtension(rel);
                bookDocs[name] = ExtractBookDocs(data, name);
                r.Merged.Add(book);
                Log.Write("data updated: " + book + " -> " + bookDocs[name].Count + " docs");
            }

            // 平文件直接覆盖（不参与按书合并）。放在语料重写之前，两者互不阻塞。
            foreach (string fileName in r.FilesChanged)
            {
                try
                {
                    string rel = null;
                    if (remoteManifest != null) remoteManifest.Paths.TryGetValue(fileName, out rel);
                    if (string.IsNullOrEmpty(rel)) rel = fileName;
                    byte[] data = Net.Get(client, Net.BaseUrl() + "/" + rel, 20000);
                    if (data == null || data.Length < 16) { r.Failed.Add(fileName); continue; }
                    // 必须是 JSON 对象/数组开头，避免把网页或二进制写进来
                    string head = Encoding.UTF8.GetString(data, 0, Math.Min(64, data.Length)).TrimStart();
                    if (head.Length == 0 || (head[0] != '{' && head[0] != '[')) { r.Failed.Add(fileName); continue; }

                    string dest = Path.Combine(baseDir, Path.GetFileName(fileName));
                    string tmpFile = dest + ".tmp";
                    File.WriteAllBytes(tmpFile, data);
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(tmpFile, dest);
                    r.FilesUpdated.Add(fileName);
                    Log.Write("file updated: " + fileName + " (" + data.Length + " B)");
                }
                catch (Exception fex)
                {
                    r.Failed.Add(fileName);
                    Log.Error("update file " + fileName, fex);
                }
            }

            // 合并写回 corpus.json（原子替换）。
            // 只有真的合并了史料才重写：不然「仅同义词表有更新」这类情况也会把
            // 内容完全没变的 corpus.json 重写一遍（mtime 变、hash 变、白挨一次 IO）。
            if (r.Merged.Count > 0)
            {
                StringBuilder sb = new StringBuilder(1 << 20);
                sb.Append("{\"book\":\"全量史料\",\"items\":[");
                bool firstDoc = true;
                foreach (KeyValuePair<string, List<string>> kv in bookDocs)
                {
                    foreach (string obj in kv.Value)
                    {
                        if (!firstDoc) sb.Append(',');
                        firstDoc = false;
                        sb.Append(obj);
                    }
                }
                sb.Append("]}");
                try
                {
                    string tmp = corpusPath + ".tmp";
                    File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                    if (File.Exists(corpusPath)) File.Delete(corpusPath);
                    File.Move(tmp, corpusPath);
                }
                catch (Exception ex)
                {
                    Log.Error("merge corpus", ex);
                    r.Message = "史料合并失败，继续使用原数据";
                    return r;
                }
            }

            // 清单必须在「合并了史料」和「只换了文件」两种情况下都更新，
            // 否则下次启动会认为同一个文件又变了、反复重下。
            try { WriteLocalVersion(baseDir, r.RemoteVersion); }
            catch (Exception ex) { Log.Error("write local version", ex); }

            StringBuilder msg = new StringBuilder();
            msg.Append("已更新到 v").Append(r.RemoteVersion).Append("（");
            if (r.Merged.Count > 0) msg.Append("合并 ").Append(r.Merged.Count).Append(" 项史料");
            if (r.FilesUpdated.Count > 0)
            {
                if (r.Merged.Count > 0) msg.Append("，");
                msg.Append("替换 ").Append(r.FilesUpdated.Count).Append(" 个数据文件");
            }
            if (r.Merged.Count == 0 && r.FilesUpdated.Count == 0) msg.Append("无变化");
            msg.Append("）");
            if (r.Failed.Count > 0) msg.Append("；").Append(r.Failed.Count).Append(" 项失败");
            r.Message = msg.ToString();
            return r;
        }

        /// <summary>把语料文件按 book 切开（用于合并时替换同一部史书）</summary>
        private static Dictionary<string, List<string>> SplitByBook(byte[] bytes)
        {
            Dictionary<string, List<string>> map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            JsonScan.ForEachDocument(bytes, delegate(CorpusDoc doc, long start, long end)
            {
                string key = string.IsNullOrEmpty(doc.Book) ? "史料" : doc.Book;
                List<string> list;
                if (!map.TryGetValue(key, out list)) { list = new List<string>(64); map[key] = list; }
                list.Add(ObjOf(bytes, start, end));
            });
            return map;
        }

        private static List<string> ExtractBookDocs(byte[] bytes, string fallbackBook)
        {
            List<string> list = new List<string>(64);
            JsonScan.ForEachDocument(bytes, delegate(CorpusDoc doc, long start, long end)
            {
                string obj = ObjOf(bytes, start, end);
                if (string.IsNullOrEmpty(doc.Book) && !string.IsNullOrEmpty(fallbackBook))
                {
                    // 补上 book 字段，避免合并后丢失归属
                    obj = obj.TrimEnd();
                    if (obj.EndsWith("}", StringComparison.Ordinal))
                        obj = obj.Substring(0, obj.Length - 1)
                            + ",\"book\":\"" + Json.Escape(fallbackBook) + "\"}";
                }
                list.Add(obj);
            });
            return list;
        }

        private static string ObjOf(byte[] bytes, long start, long end)
        {
            int s = (int)start, e = (int)end;
            if (s < 0) s = 0;
            if (e > bytes.Length) e = bytes.Length;
            return Encoding.UTF8.GetString(bytes, s, e - s);
        }

        /// <summary>写本地 data_version.json（exe 同目录）</summary>
        public static void WriteLocalVersion(string baseDir, string version)
        {
            try
            {
                string corpusPath = Path.Combine(baseDir, Corpus.DataFileName);
                string hash = Net.Sha256File(corpusPath);
                // 平文件也必须记下 hash：否则下次启动又会把它当「已变化」重下一遍
                string synPath = Path.Combine(baseDir, LabelTable.FileName);
                bool hasSyn = File.Exists(synPath);

                StringBuilder sb = new StringBuilder(512);
                sb.Append("{\n  \"version\": \"").Append(Json.Escape(version)).Append("\",\n");
                sb.Append("  \"generated_at\": \"").Append(DateTime.UtcNow.ToString("s")).Append("\",\n");
                sb.Append("  \"source_url\": \"https://github.com/alephpi/24histories-simplified-chinese\",\n");
                sb.Append("  \"books\": { \"corpus\": \"").Append(hash).Append("\" },\n");
                if (hasSyn)
                    sb.Append("  \"files\": { \"").Append(LabelTable.FileName).Append("\": \"")
                      .Append(Net.Sha256File(synPath)).Append("\" },\n");
                sb.Append("  \"paths\": { \"corpus\": \"").Append(Corpus.DataFileName).Append("\"");
                if (hasSyn)
                    sb.Append(", \"").Append(LabelTable.FileName).Append("\": \"")
                      .Append(LabelTable.FileName).Append("\"");
                sb.Append(" }\n}\n");
                File.WriteAllText(Path.Combine(baseDir, Corpus.DataVersionFile), sb.ToString(),
                    new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Error("write data_version", ex); }
        }
    }

    /// <summary>程序本体更新：下载新的安装包 -> 由外部替换助手完成热替换 -> 自动重启</summary>
    internal static class AppUpdater
    {
        public sealed class Report
        {
            public bool Checked;
            public bool Available;
            public string LocalVersion = AppVer.Number;
            public string RemoteVersion = "";
            public string Notes = "";
            public string Url = "";
            public string Sha256 = "";
            public string Message = "";
        }

        public const string VersionFileName = "app_version.json";
        public const string PendingDirName = "pending";
        public const string PendingExeName = "稽古-update.exe";

        public static Report Check()
        {
            Report r = new Report();
            byte[] bytes = Net.Get(Net.Client(), Net.BaseUrl() + "/" + VersionFileName, 7000);
            if (bytes == null) { r.Message = "未连上更新服务器"; return r; }
            AppManifest m;
            try
            {
                Dictionary<string, object> root = MiniJson.Parse(Encoding.UTF8.GetString(bytes))
                    as Dictionary<string, object>;
                if (root == null) { r.Message = "更新文件格式不正确"; return r; }
                m = new AppManifest();
                m.Version = MiniJson.Str(root, "version");
                m.Url = MiniJson.Str(root, "url");
                m.Sha256 = MiniJson.Str(root, "sha256");
                m.Notes = MiniJson.Str(root, "notes");
                object mand;
                if (root.TryGetValue("mandatory", out mand) && mand is bool) m.Mandatory = (bool)mand;
            }
            catch (Exception ex) { Log.Error("parse app_version", ex); r.Message = "更新文件无法解析"; return r; }

            r.Checked = true;
            r.RemoteVersion = m.Version;
            r.Notes = m.Notes;
            r.Url = m.Url;
            r.Sha256 = m.Sha256;
            r.Available = IsNewer(m.Version, AppVer.Number) && !string.IsNullOrEmpty(m.Url);
            r.Message = r.Available
                ? ("发现新版本 v" + m.Version)
                : "已是最新版本 v" + AppVer.Number;
            Log.Write("app check: local=" + AppVer.Number + " remote=" + m.Version
                + " available=" + r.Available);
            return r;
        }

        /// <summary>语义化版本比较：a 是否比 b 新</summary>
        public static bool IsNewer(string a, string b)
        {
            try
            {
                string[] pa = (a ?? "").Split('.'), pb = (b ?? "").Split('.');
                int n = Math.Max(pa.Length, pb.Length);
                for (int i = 0; i < n; i++)
                {
                    int va = i < pa.Length ? ParseInt(pa[i]) : 0;
                    int vb = i < pb.Length ? ParseInt(pb[i]) : 0;
                    if (va != vb) return va > vb;
                }
            }
            catch { }
            return false;
        }

        private static int ParseInt(string s)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        /// <summary>下载安装包到 pending 目录并校验</summary>
        public static string Download(string baseDir, Report r, Action<int> progress)
        {
            if (!r.Available) return null;
            string dir = Path.Combine(baseDir, PendingDirName);
            Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, PendingExeName);
            try
            {
                if (r.Url != null && r.Url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] local = Net.Get(null, r.Url, 5000);
                    if (local == null) return null;
                    File.WriteAllBytes(target, local);
                }
                else
                {
                WebClient c = Net.Client();
                byte[] data = null;
                // 先试流式下载（可报进度），失败退回一次性下载
                try
                {
                    c.DownloadProgressChanged += delegate(object s, DownloadProgressChangedEventArgs e)
                    {
                        if (progress != null) progress(e.ProgressPercentage);
                    };
                    ManualResetEvent done = new ManualResetEvent(false);
                    Exception err = null;
                    c.DownloadFileCompleted += delegate(object s, System.ComponentModel.AsyncCompletedEventArgs e)
                    { err = e.Error; done.Set(); };
                    c.DownloadFileAsync(new Uri(r.Url), target);
                    if (!done.WaitOne(180000) || err != null)
                    {
                        try { c.CancelAsync(); } catch { }
                        data = Net.Get(Net.Client(), r.Url, 180000);
                    }
                }
                catch { data = Net.Get(Net.Client(), r.Url, 180000); }

                if (data != null) File.WriteAllBytes(target, data);
                }
                if (!File.Exists(target) || new FileInfo(target).Length < 1024) return null;
                if (!string.IsNullOrEmpty(r.Sha256))
                {
                    string got = Net.Sha256File(target);
                    if (!string.Equals(got, r.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Write("update hash mismatch: want=" + r.Sha256 + " got=" + got);
                        return null;
                    }
                }
                Log.Write("update downloaded: " + target + " (" + new FileInfo(target).Length + " bytes)");
                return target;
            }
            catch (Exception ex) { Log.Error("update download", ex); return null; }
        }

        /// <summary>
        /// 启动替换助手：把当前安装包复制到临时目录，以 --apply-update 方式运行，
        /// 由它等本进程退出后替换文件并重启。
        /// </summary>
        public static bool LaunchReplace(string installBaseDir, string newExePath)
        {
            try
            {
                string helper = Path.Combine(Path.GetTempPath(),
                    "jigu-hotswap-" + Guid.NewGuid().ToString("N") + ".exe");
                File.Copy(Application.ExecutablePath, helper, true);
                ProcessStartInfo psi = new ProcessStartInfo(helper);
                psi.Arguments = "--apply-update \"" + installBaseDir.TrimEnd('\\') + "\" \""
                    + newExePath + "\" " + Process.GetCurrentProcess().Id;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process.Start(psi);
                Log.Write("hotswap helper launched: " + helper);
                return true;
            }
            catch (Exception ex) { Log.Error("launch hotswap", ex); return false; }
        }

        /// <summary>
        /// 替换助手主体：等目标进程退出 -> 用安装包内容覆盖 installDir -> 重启主程序。
        /// 安装包是自解的：直接以 --extract-to 方式运行它即可落地全部文件。
        /// </summary>
        public static int RunReplace(string installDir, string newExePath, int targetPid)
        {
            Log.Write("hotswap start: install=" + installDir + " new=" + newExePath + " pid=" + targetPid);
            try
            {
                // 1) 等旧进程退出
                for (int i = 0; i < 120; i++)
                {
                    try
                    {
                        Process p = Process.GetProcessById(targetPid);
                        if (p.HasExited) break;
                    }
                    catch { break; }
                    Thread.Sleep(250);
                }
                Thread.Sleep(600);

                // 2) 让新安装包自解压到安装目录（覆盖旧文件）
                ProcessStartInfo psi = new ProcessStartInfo(newExePath);
                psi.Arguments = "--extract-to \"" + installDir.TrimEnd('\\') + "\"";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process ex = Process.Start(psi);
                ex.WaitForExit(180000);
                Log.Write("hotswap extract exit=" + ex.ExitCode);

                // 3) 重启主程序（路径全部相对 installDir，不写死盘符）
                string main = Path.Combine(installDir, "稽古.exe");
                if (File.Exists(main))
                {
                    Process.Start(new ProcessStartInfo(main) { WorkingDirectory = installDir });
                    Log.Write("hotswap restarted: " + main);
                }
                else Log.Write("hotswap: main exe not found at " + main);
            }
            catch (Exception ex) { Log.Error("hotswap body", ex); }
            return 0;
        }

        /// <summary>更新提示弹窗（友好中文，按钮为「立即更新 / 稍后」）</summary>
        public static bool AskUser(Report r)
        {
            string notes = string.IsNullOrEmpty(r.Notes) ? "" : ("\r\n\r\n本次更新内容：\r\n" + r.Notes);
            string msg = "发现新版本 v" + r.RemoteVersion + "（当前 v" + AppVer.Number + "）。"
                + notes + "\r\n\r\n是否现在更新？更新完成后程序会自动重启。";
            DialogResult dr = MessageBox.Show(msg, "稽古 · 软件更新",
                MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            return dr == DialogResult.Yes;
        }
    }
}
