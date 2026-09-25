// FILE: jigu-app/tests/DataCheck.cs
// 校验随包发布的语料与同义词数据：
//   1. 每条史事的五要素是否齐全（原文/白话/人物/决策/结果）
//   2. 是否带主题标签（检索的策展词汇依赖它）
//   3. 同义词表是否成组、有没有空项
//   4. 同义词表体检：哪些成员在语料里根本没有落点（只能起触发作用），
//      哪些长于 4 字（词表最长只发 4 字词元，永远无法作为扩展词生效）
//   5. UTF-8 中文是否完好（正文字符必须在 CJK 区间）
// 规则引擎（rules.json）已在 v1.6 移除，这里不再校验。
//
// 编译：csc /out:DataCheck.exe DataCheck.cs
// 用法：DataCheck.exe [项目根目录]     不给参数则用当前目录
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

internal static class DataCheck
{
    private static readonly List<string> Problems = new List<string>();
    private static int _docs;
    private static int _groups;
    private static int _members;
    private static int _membersInCorpus;
    private static int _membersTooLong;
    private static int _pros;
    private static int _cons;

    private static int Main(string[] args)
    {
        string root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }

        string dataDir = Path.Combine(root, @"resources\data");
        List<string> corpusPaths = new List<string>();
        foreach (string f in Directory.GetFiles(dataDir, "*.json"))
        {
            if (Path.GetFileName(f).ToLowerInvariant().IndexOf("version") < 0) corpusPaths.Add(f);
        }
        corpusPaths.Sort();
        if (corpusPaths.Count == 0) { Console.WriteLine("FAIL: no corpus"); return 1; }

        IDictionary synFile = (IDictionary)MiniJson.Parse(File.ReadAllText(Path.Combine(root, @"resources\labels.json"), Encoding.UTF8));

        // ---- 1/2. 史事完整性（遍历全部语料文件） ----
        HashSet<string> allText = new HashSet<string>();
        HashSet<string> titles = new HashSet<string>();
        foreach (string corpusPath in corpusPaths)
        {
            IDictionary corpus = (IDictionary)MiniJson.Parse(File.ReadAllText(corpusPath, Encoding.UTF8));
            string book = Str(corpus, "book");
            IList items = (IList)corpus["items"];
            Console.WriteLine("{0,-22} book={1,-8} items={2}", Path.GetFileName(corpusPath), book, items.Count);
            foreach (object o in items)
            {
                _docs++;
                IDictionary it = (IDictionary)o;
                string chapter = Str(it, "chapter");
                string label = book + "·" + chapter + "·" + Str(it, "title");
                if (!titles.Add(label)) Problem("条目重复: " + label);
                CheckField(it, label, "chapter", 2);
                CheckField(it, label, "title", 2);
                CheckField(it, label, "original", 8);
                CheckField(it, label, "translation", 8);
                CheckField(it, label, "decision", 4);
                CheckField(it, label, "outcome", 4);
                IList figures = it["figures"] as IList;
                if (figures == null || figures.Count == 0) Problem("缺少人物: " + label);
                IList themes = it["themes"] as IList;
                if (themes == null || themes.Count < 2) Problem("主题标签少于 2 个: " + label);
                else foreach (object t in themes) allText.Add(Convert.ToString(t, CultureInfo.InvariantCulture));

                // 决策分析栏的正文。缺了界面就少一整块，而且不会报错，所以在这里卡住：
                // 每条 2~3 句，句子不能是占位符。
                CheckList(it, label, "pros", 2, 3, 10, ref _pros);
                CheckList(it, label, "cons", 2, 3, 10, ref _cons);

                AppendText(allText, Str(it, "title"));
                AppendText(allText, Str(it, "original"));
                AppendText(allText, Str(it, "translation"));
                AppendText(allText, Str(it, "decision"));
                AppendText(allText, Str(it, "outcome"));
                if (figures != null) foreach (object f in figures) AppendText(allText, Convert.ToString(f, CultureInfo.InvariantCulture));
            }
        }
        Console.WriteLine();
        Console.WriteLine("docs            : " + _docs);
        Console.WriteLine("pros / cons     : " + _pros + " / " + _cons + " 句（决策分析栏的正文）");

        // ---- 6. 中文完好性 ----
        int cjk = 0, total = 0;
        foreach (string s in allText)
        {
            foreach (char c in s)
            {
                if (c > 0x2E80) { total++; if (c >= 0x4E00 && c <= 0x9FFF) cjk++; }
            }
        }
        Console.WriteLine("cjk chars       : " + cjk + " / " + total + " non-ascii");
        if (cjk < 5000) Problem("CJK 字符过少，可能是编码损坏: " + cjk);
        if (_docs < 200) Problem("史事条数不足 200: " + _docs);

        // ---- 3/4. 同义词表：结构 + 体检 ----
        IList groups = (IList)synFile["labels"];   // labels.json 的键是 labels
        _groups = groups.Count;
        List<string> deadMembers = new List<string>();
        List<string> tooLongMembers = new List<string>();
        foreach (object o in groups)
        {
            IDictionary g = (IDictionary)o;
            string key = Str(g, "label");
            IList terms = g["triggers"] as IList;
            if (string.IsNullOrEmpty(key)) Problem("同义词组缺少 key");
            if (terms == null || terms.Count < 2) Problem("同义词组词量不足: " + key);

            // 组键也算一个成员：它同样会被当作扩展候选，也必须能当触发器。
            // 表里常有「组键也列在 terms 里」的写法，这里去重，否则同一项会被数两次。
            List<string> all = new List<string>();
            if (!string.IsNullOrEmpty(key)) all.Add(key);
            if (terms != null)
            {
                foreach (object t in terms)
                {
                    string s = Convert.ToString(t, CultureInfo.InvariantCulture);
                    if (!string.IsNullOrEmpty(s) && !all.Contains(s)) all.Add(s);
                }
            }

            foreach (string m in all)
            {
                if (string.IsNullOrEmpty(m)) continue;
                _members++;
                bool inCorpus = false;
                foreach (string txt in allText)
                {
                    if (txt.IndexOf(m, StringComparison.Ordinal) >= 0) { inCorpus = true; break; }
                }
                if (!inCorpus) { deadMembers.Add(key + " → " + m); continue; }
                _membersInCorpus++;
                // 词表最长只发 4 字词元（见 Corpus.EmitBlock），更长的成员永不会成为索引键，
                // 因此只能起触发作用，扩展不出任何东西。
                if (m.Length > 4) { _membersTooLong++; tooLongMembers.Add(key + " → " + m); }
            }
        }
        Console.WriteLine("label entries   : " + _groups);
        Console.WriteLine("trigger phrases : " + _members + " total, " + _membersInCorpus
            + " present in corpus, " + (_members - _membersInCorpus) + " absent (trigger-only)");
        Console.WriteLine("  over 4 chars  : " + _membersTooLong
            + " (never indexed, so never usable as expansion terms)");
        Console.WriteLine("  note: this checks substring presence in the corpus text; the search engine"
            + " additionally requires the member to be an index key.");
        Console.WriteLine();

        // ---- 汇总 ----
        if (Problems.Count == 0)
        {
            Console.WriteLine("RESULT: DATA OK  (" + _docs + " docs / " + _groups + " labels / "
                + _members + " members)");
            if (deadMembers.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("TRIGGER-ONLY members (absent from the corpus, first 25 of "
                    + deadMembers.Count + ") -- fine as user-language triggers, useless as expansion:");
                for (int i = 0; i < deadMembers.Count && i < 25; i++) Console.WriteLine("  " + deadMembers[i]);
            }
            if (tooLongMembers.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("OVER-4-CHAR members (first 15 of " + tooLongMembers.Count
                    + ") -- trigger-only by construction:");
                for (int i = 0; i < tooLongMembers.Count && i < 15; i++) Console.WriteLine("  " + tooLongMembers[i]);
            }
            return 0;
        }
        Console.WriteLine("PROBLEMS (" + Problems.Count + "):");
        foreach (string p in Problems) Console.WriteLine("  - " + p);
        Console.WriteLine();
        Console.WriteLine("RESULT: DATA PROBLEMS FOUND");
        return 2;
    }

    /// <summary>校验 pros / cons：条数在 [min, max] 内，每句不短于 minChars。total 累计句数。</summary>
    private static void CheckList(IDictionary it, string label, string key, int min, int max, int minChars, ref int total)
    {
        IList list = it[key] as IList;
        if (list == null || list.Count < min || list.Count > max)
        {
            Problem("字段 " + key + " 条数不在 " + min + "~" + max + ": " + label
                + " (" + (list == null ? 0 : list.Count) + ")");
            return;
        }
        foreach (object o in list)
        {
            string s = Convert.ToString(o, CultureInfo.InvariantCulture);
            if (s.Length < minChars) Problem("字段 " + key + " 句子过短(" + s.Length + "): " + label);
        }
        total += list.Count;
    }

    private static void CheckField(IDictionary it, string label, string key, int minLen)
    {
        if (!it.Contains(key) || it[key] == null) { Problem("字段不存在 " + key + ": " + label); return; }
        string v = Str(it, key);
        if (v.Length < minLen) Problem("字段 " + key + " 过短(" + v.Length + "): " + label);
    }

    private static void AppendText(HashSet<string> set, string s) { if (!string.IsNullOrEmpty(s)) set.Add(s); }

    private static string Str(IDictionary d, string key)
    {
        if (d == null || !d.Contains(key) || d[key] == null) return "";
        return Convert.ToString(d[key], CultureInfo.InvariantCulture);
    }

    private static void Problem(string p) { Problems.Add(p); }
}

/// <summary>极简 JSON 解析（与宿主同源实现，避免额外依赖）</summary>
internal static class MiniJson
{
    public static object Parse(string text)
    {
        int i = 0;
        return Value(text, ref i);
    }

    private static object Value(string s, ref int i)
    {
        Skip(s, ref i);
        char c = s[i];
        if (c == '{') return Obj(s, ref i);
        if (c == '[') return Arr(s, ref i);
        if (c == '"') return Str(s, ref i);
        if (c == 't') { i += 4; return true; }
        if (c == 'f') { i += 5; return false; }
        if (c == 'n') { i += 4; return null; }
        return Num(s, ref i);
    }

    private static Dictionary<string, object> Obj(string s, ref int i)
    {
        Dictionary<string, object> m = new Dictionary<string, object>();
        i++;
        while (i < s.Length)
        {
            Skip(s, ref i);
            if (s[i] == '}') { i++; break; }
            if (s[i] == ',') { i++; continue; }
            string k = Str(s, ref i);
            Skip(s, ref i);
            if (i < s.Length && s[i] == ':') i++;
            m[k] = Value(s, ref i);
        }
        return m;
    }

    private static List<object> Arr(string s, ref int i)
    {
        List<object> l = new List<object>();
        i++;
        while (i < s.Length)
        {
            Skip(s, ref i);
            if (s[i] == ']') { i++; break; }
            if (s[i] == ',') { i++; continue; }
            l.Add(Value(s, ref i));
        }
        return l;
    }

    private static string Str(string s, ref int i)
    {
        if (s[i] != '"') return null;
        i++;
        StringBuilder sb = new StringBuilder();
        while (i < s.Length)
        {
            char c = s[i++];
            if (c == '"') break;
            if (c == '\\' && i < s.Length)
            {
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'u':
                        if (i + 4 <= s.Length)
                        {
                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 4;
                        }
                        break;
                    default: sb.Append(e); break;
                }
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static object Num(string s, ref int i)
    {
        int st = i;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
        double d;
        if (double.TryParse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
        return null;
    }

    private static void Skip(string s, ref int i)
    {
        while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
    }
}
