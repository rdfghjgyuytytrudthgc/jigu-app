// FILE: jigu-app/tools/HtmlShot.cs
// 用系统自带的旧版浏览器控件（IE 内核，注册表 FEATURE_BROWSER_EMULATION 设为 IE11 模式）
// 加载一个本地 HTML 并截图。用途：在沙箱内查看前端古风界面的实际渲染效果。
// WebView2 在本沙箱被命名管道限制拦住，这个控件不走那条路径，因此可以出图。
// 用法: HtmlShot.exe <html路径> <输出png> [等待毫秒]
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

internal sealed class ShotForm : Form
{
    private readonly string _url;
    private readonly string _out;
    private readonly int _waitMs;
    private WebBrowser _wb;
    private bool _saved;

    public ShotForm(string url, string output, int waitMs)
    {
        _url = url;
        _out = output;
        _waitMs = waitMs;

        Text = "稽古 · 界面预览";
        ClientSize = new Size(1120, 900);
        StartPosition = FormStartPosition.Manual;
        Location = new Point(20, 20);
        TopMost = true;
        BackColor = Color.FromArgb(248, 245, 238);

        _wb = new WebBrowser();
        _wb.Dock = DockStyle.Fill;
        _wb.ScriptErrorsSuppressed = true;
        _wb.IsWebBrowserContextMenuEnabled = false;
        _wb.WebBrowserShortcutsEnabled = false;
        _wb.AllowWebBrowserDrop = false;
        Controls.Add(_wb);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try { _wb.Navigate(new Uri(_url)); }
        catch (Exception ex) { Console.WriteLine("navigate error: " + ex.Message); }
        ThreadPool.QueueUserWorkItem(delegate
        {
            Thread.Sleep(_waitMs);
            try { BeginInvoke((MethodInvoker)Capture); } catch { }
        });
    }

    private void Capture()
    {
        if (_saved) return;
        _saved = true;
        try
        {
            Rectangle r = Bounds;
            using (Bitmap bmp = new Bitmap(r.Width, r.Height))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(r.Left, r.Top, 0, 0, bmp.Size);
                }
                bmp.Save(_out, ImageFormat.Png);
                Console.WriteLine("saved=" + _out + " " + r.Width + "x" + r.Height);
            }
        }
        catch (Exception ex) { Console.WriteLine("capture error: " + ex.Message); }
        Close();
    }
}

internal static class HtmlShot
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("usage: HtmlShot <html> <out.png> [waitMs]"); return; }
        string html = Path.GetFullPath(args[0]);
        string output = Path.GetFullPath(args[1]);
        int wait = args.Length > 2 ? int.Parse(args[2]) : 3500;

        // 让内嵌控件以 IE11 模式渲染（否则会退化成 IE7，CSS 会走样）
        try
        {
            string exe = Path.GetFileName(Application.ExecutablePath);
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION"))
            {
                if (k != null) k.SetValue(exe, 11001, RegistryValueKind.DWord);
            }
        }
        catch { }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new ShotForm(html, output, wait));
    }
}
