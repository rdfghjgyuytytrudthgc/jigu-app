using System;
using System.IO;
using System.Text;
using Jigu;
internal static class ScanTest {
  private static int Main(string[] a) {
    Console.OutputEncoding = Encoding.UTF8;
    byte[] b = File.ReadAllBytes(a[0]);
    int n = 0, withBook = 0, withItems = 0, emptyOrig = 0;
    JsonScan.ForEachDocument(b, delegate(CorpusDoc d, long s, long e) {
      n++;
      if (!string.IsNullOrEmpty(d.Book)) withBook++;
      if (!string.IsNullOrEmpty(d.Chapter)) withItems++;
      if (string.IsNullOrEmpty(d.Original)) emptyOrig++;
      if (n <= 3) Console.WriteLine("  #" + n + " book=[" + d.Book + "] chapter=[" + d.Chapter + "] origLen=" + d.Original.Length);
    });
    Console.WriteLine("docs=" + n + " withBook=" + withBook + " withChapter=" + withItems + " emptyOrig=" + emptyOrig);
    return 0;
  }
}