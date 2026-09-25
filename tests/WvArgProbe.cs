using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

internal static class WvArgProbe
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Form f = new Form();
        f.Shown += delegate
        {
            string udf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "JiguProbe" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Console.WriteLine("UDF=" + udf);
            int i = 0;
            foreach (string a in args)
            {
                i++;
                string label = a.Length == 0 ? "(default: no AdditionalBrowserArguments)"
                    : (a.Length > 70 ? a.Substring(0, 70) + "..." : a);
                try
                {
                    CoreWebView2EnvironmentOptions o = new CoreWebView2EnvironmentOptions();
                    if (a.Length > 0) o.AdditionalBrowserArguments = a;
                    Task<CoreWebView2Environment> t = CoreWebView2Environment.CreateAsync(null, udf, o);
                    bool ok = t.Wait(20000);
                    if (!ok) Console.WriteLine("[" + i + "] TIMEOUT   : " + label);
                    else Console.WriteLine("[" + i + "] OK        : " + label);
                }
                catch (Exception ex)
                {
                    Exception e = ex is AggregateException ? ((AggregateException)ex).InnerException : ex;
                    Console.WriteLine("[" + i + "] " + e.GetType().Name + " : " + e.Message);
                    Console.WriteLine("      ARGS: " + label);
                }
            }
            Console.WriteLine("PROBE-END");
            Environment.Exit(0);
        };
        Application.Run(f);
    }
}
