using System;
using System.IO;
using System.Text;
using Microsoft.Web.WebView2.Core;
using System.Threading.Tasks;
using System.Windows.Forms;
internal static class RunProbe
{
    [STAThread]
    private static void Main(string[] a)
    {
        Application.EnableVisualStyles();
        Form f = new Form();
        f.Shown += delegate
        {
            string udf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "JiguProbe" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Console.WriteLine("UDF=" + udf);
            string[] lines = File.ReadAllLines(a[0], Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                string s = lines[i];
                string label = s.Length > 64 ? s.Substring(0, 64) + "..." : (s.Length == 0 ? "(empty)" : s);
                try
                {
                    CoreWebView2EnvironmentOptions o = new CoreWebView2EnvironmentOptions();
                    if (s.Length > 0) o.AdditionalBrowserArguments = s;
                    Task<CoreWebView2Environment> t = CoreWebView2Environment.CreateAsync(null, udf, o);
                    bool ok = t.Wait(20000);
                    Console.WriteLine("[" + (i + 1) + "] " + (ok ? "OK      " : "TIMEOUT ") + " : " + label);
                }
                catch (Exception ex)
                {
                    Exception e = ex is AggregateException ? ((AggregateException)ex).InnerException : ex;
                    Console.WriteLine("[" + (i + 1) + "] ** " + e.GetType().Name + " : " + e.Message);
                    Console.WriteLine("      ARGS: " + label);
                }
            }
            Console.WriteLine("PROBE-END");
            Environment.Exit(0);
        };
        Application.Run(f);
    }
}
