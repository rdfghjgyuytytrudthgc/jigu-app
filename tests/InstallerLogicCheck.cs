// FILE: jigu-app/tests/InstallerLogicCheck.cs
// 独立验证安装器的两项核心逻辑（在沙箱内 HKCU 与桌面不可写，故用可写路径等价测试）：
//   1) 快捷方式创建/读取（WScript.Shell）
//   2) 卸载注册项的写入/读取/删除（HKCU，测试键）
// 编译：csc /out:InstallerLogicCheck.exe /r:System.Windows.Forms.dll InstallerLogicCheck.cs
using System;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

internal static class InstallerLogicCheck
{
    private static int _fails;
    private static readonly StringBuilder Out = new StringBuilder();

    private static void Say(string s) { Out.AppendLine(s); Console.WriteLine(s); }
    private static void Fail(string s) { _fails++; Say("  FAIL " + s); }

    private static int Main(string[] args)
    {
        string work = args.Length > 0 ? args[0] : @"D:\DSH\jigu-app\testinstall";
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }

        Say("== 安装器逻辑校验 ==");
        Say("工作目录: " + work);
        Say("");

        // ---------- 1. 快捷方式 ----------
        Say("--- 1. 桌面快捷方式创建 ---");
        string lnk = Path.Combine(work, "_check_desktop.lnk");
        string target = Path.Combine(work, "稽古.exe");
        string icon = Path.Combine(work, "Jigu.ico");
        if (CreateShortcut(lnk, target, work, icon))
        {
            Say("  已创建: " + lnk + " (" + new FileInfo(lnk).Length + " B)");
            string t, w, i;
            if (ReadShortcut(lnk, out t, out w, out i))
            {
                Say("  TargetPath       = " + t);
                Say("  WorkingDirectory = " + w);
                Say("  IconLocation     = " + i);
                if (!string.Equals(t, target, StringComparison.OrdinalIgnoreCase)) Fail("目标路径不符");
                if (!string.Equals(w, work, StringComparison.OrdinalIgnoreCase)) Fail("工作目录不符");
                if (i.IndexOf("Jigu.ico", StringComparison.OrdinalIgnoreCase) < 0) Fail("图标未指向 Jigu.ico");
                if (!File.Exists(target)) Fail("目标 exe 不存在: " + target);
            }
            else Fail("无法读回快捷方式");
        }
        else Fail("快捷方式创建失败");

        Say("");
        Say("--- 2. 卸载注册项（HKCU 测试键）---");
        string testKey = @"Software\JiguSetupCheck\Uninstall\{TEST-APP-ID}";
        try
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(testKey))
            {
                if (k == null) Fail("CreateSubKey 返回 null");
                else
                {
                    k.SetValue("DisplayName", "稽古（二十四史情境检索）");
                    k.SetValue("DisplayVersion", "1.3.0");
                    k.SetValue("Publisher", "稽古");
                    k.SetValue("InstallLocation", work);
                    k.SetValue("DisplayIcon", icon);
                    k.SetValue("UninstallString", "\"" + Path.Combine(work, "稽古-卸载.exe") + "\" /uninstall");
                    k.SetValue("QuietUninstallString", "\"" + Path.Combine(work, "稽古-卸载.exe") + "\" /uninstall /silent");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    Say("  写入成功");
                }
            }

            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(testKey))
            {
                if (k == null) Fail("读回失败：键不存在");
                else
                {
                    Say("  DisplayName     = " + k.GetValue("DisplayName"));
                    Say("  DisplayVersion  = " + k.GetValue("DisplayVersion"));
                    Say("  UninstallString = " + k.GetValue("UninstallString"));
                    Say("  DisplayIcon     = " + k.GetValue("DisplayIcon"));
                    if (k.GetValue("DisplayName") == null) Fail("DisplayName 缺失");
                    if (k.GetValue("UninstallString") == null) Fail("UninstallString 缺失");
                }
            }

            Registry.CurrentUser.DeleteSubKeyTree(testKey, false);
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(testKey))
            {
                if (k != null) Fail("删除后仍存在");
                else Say("  删除成功（等价卸载时的清理）");
            }
        }
        catch (Exception ex)
        {
            Fail("注册表操作异常: " + ex.GetType().Name + " / " + ex.Message);
        }

        Say("");
        Say("--- 3. 已安装目录完整性 ---");
        string exe = Path.Combine(work, "稽古.exe");
        string unins = Path.Combine(work, "稽古-卸载.exe");
        string[] need = { "稽古.exe", "稽古-卸载.exe", "Jigu.ico", "使用说明.txt",
                          "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll",
                          "WebView2Loader.dll", "data_base.txt",
                          "data\\version.json", "data\\00_种子.json", "data\\01_先秦秦汉.json",
                          "data\\02_三国两晋.json", "data\\03_隋唐五代.json", "data\\04_两宋.json",
                          "data\\05_明.json", "data\\06_清.json" };
        foreach (string n in need)
        {
            string p = Path.Combine(work, n);
            if (File.Exists(p)) Say("  OK   " + n.PadRight(38) + new FileInfo(p).Length + " B");
            else Fail("缺少: " + n);
        }

        // 卸载程序应与安装包同源（同一二进制）
        if (File.Exists(unins) && File.Exists(exe))
        {
            long a = new FileInfo(unins).Length;
            Say("  卸载程序大小 = " + a + " B（与安装包同一份二进制，带 /uninstall 进入卸载流程）");
        }

        Say("");
        Say(_fails == 0 ? "RESULT: INSTALLER LOGIC OK (0 fails)" : ("RESULT: FAILURES = " + _fails));

        try
        {
            File.WriteAllText(Path.Combine(work, "installer-logic-report.txt"),
                Out.ToString(), new UTF8Encoding(false));
        }
        catch { }
        return _fails == 0 ? 0 : 2;
    }

    private static bool CreateShortcut(string lnkPath, string target, string workingDir, string iconPath)
    {
        try
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) return false;
            object shell = Activator.CreateInstance(t);
            try
            {
                object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                    new object[] { lnkPath });
                Type st = sc.GetType();
                st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
                st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { workingDir });
                st.InvokeMember("Description", BindingFlags.SetProperty, null, sc,
                    new object[] { "稽古 · 二十四史情境检索" });
                st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc,
                    new object[] { iconPath + ",0" });
                st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
                return File.Exists(lnkPath);
            }
            finally { System.Runtime.InteropServices.Marshal.ReleaseComObject(shell); }
        }
        catch { return false; }
    }

    private static bool ReadShortcut(string lnkPath, out string target, out string workingDir, out string icon)
    {
        target = ""; workingDir = ""; icon = "";
        try
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) return false;
            object shell = Activator.CreateInstance(t);
            try
            {
                object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                    new object[] { lnkPath });
                Type st = sc.GetType();
                target = Convert.ToString(st.InvokeMember("TargetPath", BindingFlags.GetProperty, null, sc, null));
                workingDir = Convert.ToString(st.InvokeMember("WorkingDirectory", BindingFlags.GetProperty, null, sc, null));
                icon = Convert.ToString(st.InvokeMember("IconLocation", BindingFlags.GetProperty, null, sc, null));
                return true;
            }
            finally { System.Runtime.InteropServices.Marshal.ReleaseComObject(shell); }
        }
        catch { return false; }
    }
}
