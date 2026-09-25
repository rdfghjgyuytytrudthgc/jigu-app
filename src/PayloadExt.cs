// FILE: jigu-app/src/PayloadExt.cs
// 安装包负载的自解压实现（与 build.ps1 生成的 InstallerPayload 类配合）。
// 用途：
//   1. 安装向导把全部文件释放到用户选择的安装目录；
//   2. 热更新时新的安装包以 --extract-to <目录> 运行，覆盖旧文件。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Jigu
{
    internal static partial class InstallerPayload
    {
        /// <summary>把负载里的每个文件写入 destDir，返回写入数量</summary>
        public static int ExtractTo(string destDir)
        {
            if (string.IsNullOrEmpty(destDir)) return 0;
            int n = 0;
            try
            {
                if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                foreach (KeyValuePair<string, string> kv in Table())
                {
                    string rel = kv.Key.Replace('/', Path.DirectorySeparatorChar);
                    string path = Path.Combine(destDir, rel);
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    byte[] bytes;
                    try { bytes = Convert.FromBase64String(kv.Value); }
                    catch { continue; }
                    // 原子写入，避免覆盖过程中被占用导致半截文件
                    string tmp = path + ".tmp";
                    File.WriteAllBytes(tmp, bytes);
                    if (File.Exists(path))
                    {
                        try { File.Delete(path); }
                        catch
                        {
                            // 文件被占用（例如正在运行的主程序）：先改名再写入
                            string bak = path + ".old";
                            try { if (File.Exists(bak)) File.Delete(bak); } catch { }
                            try { File.Move(path, bak); } catch { }
                        }
                    }
                    File.Move(tmp, path);
                    n++;
                }
            }
            catch (Exception ex) { Log.Error("ExtractTo", ex); }
            return n;
        }
    }
}
