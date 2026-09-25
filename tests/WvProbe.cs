using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal static class WvProbe
{
    [STAThread]
    private static void Main(string[] args)
    {
        string flags = args.Length > 0 ? args[0] : "";
        string udf = Path.Combine(Path.GetTempPath(), "jigu-probe-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Application.EnableVisualStyles();
        Form f = new Form();
        f.Text = "PROBE";
        f.ClientSize = new Size(700, 460);
        WebView2 v = new WebView2();
        v.Dock = DockStyle.Fill;
        f.Controls.Add(v);
        f.Shown += delegate
        {
            try
            {
                CoreWebView2EnvironmentOptions o = new CoreWebView2EnvironmentOptions();
                if (flags.Length > 0) o.AdditionalBrowserArguments = flags;
                Console.WriteLine("UDF=" + udf);
                Console.WriteLine("FLAGS=" + flags);
                Task<CoreWebView2Environment> t = CoreWebView2Environment.CreateAsync(null, udf, o);
                if (t.Wait(25000))
                {
                    Console.WriteLine("CREATE=ok");
                    v.EnsureCoreWebView2Async(t.Result);
                }
                else Console.WriteLine("CREATE=timeout");
            }
            catch (Exception ex) { Console.WriteLine("CREATE=ex " + ex.GetType().Name + ": " + ex.Message); }
        };
        v.CoreWebView2InitializationCompleted += delegate(object s, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (e.InitializationException != null)
            {
                Console.WriteLine("CORE=fail " + e.InitializationException.GetType().Name
                    + ": " + e.InitializationException.Message);
                return;
            }
            Console.WriteLine("CORE=ok version=" + v.CoreWebView2.Environment.BrowserVersionString);
            v.CoreWebView2.NavigateToString(
                "<html><body style='background:#f8f5ee;font-family:KaiTi'><h1 style='color:#c0392b'>"
                + "\u7a3d\u53e4 PROBE OK</h1></body></html>");
            Timer tm = new Timer();
            tm.Interval = 2500;
            tm.Tick += delegate { Console.WriteLine("DONE"); tm.Stop(); Application.Exit(); };
            tm.Start();
        };
        Application.Run(f);
    }
}
