// FILE: jigu-app/src/Json.cs
// 极简 JSON 工具：流式扫描语料（逐条吐出文档，不构造整棵对象树）+ 转义。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Jigu
{
    internal static class Json
    {
        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length + 16);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// 语料文件扫描器。
    /// 支持两种结构：
    ///   { "items": [ {...}, {...} ] }
    ///   [ {...}, {...} ]
    /// 每条文档解析完立即交给回调，正文只在回调期间存在，不入全局缓存。
    /// </summary>
    internal static class JsonScan
    {
        // start/end 是这条文档在原文里的起止位置。Update.cs 的按书合并要用它把每条的
        // 原始 JSON 文本原样抠出来重新拼接（ObjOf(bytes, start, end)），所以不能删。
        public delegate void DocHandler(CorpusDoc doc, long start, long end);

        public static void ForEachDocument(byte[] bytes, DocHandler handler)
        {
            if (bytes == null || bytes.Length == 0) return;
            string text = Encoding.UTF8.GetString(bytes);
            int i = 0;
            SkipWs(text, ref i);
            if (i >= text.Length) return;

            if (text[i] == '[')
            {
                i++;
                ScanArray(text, ref i, handler, null);
                return;
            }
            if (text[i] == '{')
            {
                i++;
                string bookName = null;
                // 根对象里通常有 "book" 与 "items"。
                // 关键：先把 book 读出来，再交给 items 数组；
                // 绝不能把根对象当成一条文档去解析（否则会把 items 当成字符串跳过）。
                while (i < text.Length)
                {
                    SkipWs(text, ref i);
                    if (i >= text.Length) break;
                    if (text[i] == '}') { i++; break; }
                    if (text[i] == ',') { i++; continue; }
                    if (text[i] != '"') { i++; continue; }
                    string key = ReadString(text, ref i);
                    SkipWs(text, ref i);
                    if (i < text.Length && text[i] == ':') i++;
                    SkipWs(text, ref i);

                    if (key == "book" || key == "source_url")
                    {
                        string v = ReadString(text, ref i);
                        if (key == "book") bookName = v;
                    }
                    else if (key == "items" && i < text.Length && text[i] == '[')
                    {
                        i++;
                        ScanArray(text, ref i, handler, bookName);
                    }
                    else
                    {
                        SkipValue(text, ref i);
                    }
                }
            }
        }

        private static void ScanArray(string text, ref int i, DocHandler handler, string inheritBook)
        {
            while (i < text.Length)
            {
                SkipWs(text, ref i);
                if (i >= text.Length) break;
                if (text[i] == ']') { i++; break; }
                if (text[i] == ',') { i++; continue; }
                if (text[i] == '{')
                {
                    long start = i;
                    CorpusDoc doc = ReadDoc(text, ref i);
                    if (doc != null)
                    {
                        if (string.IsNullOrEmpty(doc.Book) && !string.IsNullOrEmpty(inheritBook))
                            doc.Book = inheritBook;
                        handler(doc, start, i);
                    }
                }
                else
                {
                    SkipValue(text, ref i);
                }
            }
        }

        private static CorpusDoc ReadDoc(string text, ref int i)
        {
            CorpusDoc doc = new CorpusDoc();
            if (i >= text.Length || text[i] != '{') return null;
            i++;
            while (i < text.Length)
            {
                SkipWs(text, ref i);
                if (i >= text.Length) break;
                if (text[i] == '}') { i++; break; }
                if (text[i] == ',') { i++; continue; }
                if (text[i] != '"') { i++; continue; }
                string key = ReadString(text, ref i);
                SkipWs(text, ref i);
                if (i < text.Length && text[i] == ':') i++;
                SkipWs(text, ref i);

                switch (key)
                {
                    case "book": doc.Book = ReadString(text, ref i); break;
                    case "chapter": doc.Chapter = ReadString(text, ref i); break;
                    case "title": doc.Title = ReadString(text, ref i); break;
                    case "original":
                    case "text":
                    case "content":
                        doc.Original = ReadString(text, ref i); break;
                    case "translation":
                    case "modern":
                    case "trans":
                        doc.Translation = ReadString(text, ref i); break;
                    case "decision": doc.Decision = ReadString(text, ref i); break;
                    case "outcome": doc.Outcome = ReadString(text, ref i); break;
                    case "figures": doc.Figures = ReadStringArray(text, ref i); break;
                    case "themes": doc.Themes = ReadStringArray(text, ref i); break;
                    case "pros": doc.Pros = ReadStringArray(text, ref i); break;
                    case "cons": doc.Cons = ReadStringArray(text, ref i); break;
                    default: SkipValue(text, ref i); break;
                }
            }
            return doc;
        }

        private static string[] ReadStringArray(string text, ref int i)
        {
            List<string> list = new List<string>(4);
            SkipWs(text, ref i);
            if (i >= text.Length || text[i] != '[') { SkipValue(text, ref i); return list.ToArray(); }
            i++;
            while (i < text.Length)
            {
                SkipWs(text, ref i);
                if (i >= text.Length) break;
                if (text[i] == ']') { i++; break; }
                if (text[i] == ',') { i++; continue; }
                if (text[i] == '"') list.Add(ReadString(text, ref i));
                else SkipValue(text, ref i);
            }
            return list.ToArray();
        }

        private static string ReadString(string text, ref int i)
        {
            SkipWs(text, ref i);
            if (i >= text.Length || text[i] != '"') return "";
            i++;
            StringBuilder sb = new StringBuilder(64);
            while (i < text.Length)
            {
                char c = text[i++];
                if (c == '"') break;
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= text.Length) break;
                char e = text[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case '/': sb.Append('/'); break;
                    case '\\': sb.Append('\\'); break;
                    case '"': sb.Append('"'); break;
                    case 'u':
                        if (i + 4 <= text.Length)
                        {
                            int code;
                            if (int.TryParse(text.Substring(i, 4), NumberStyles.HexNumber,
                                    CultureInfo.InvariantCulture, out code))
                            {
                                sb.Append((char)code);
                                i += 4;
                            }
                        }
                        break;
                    default: sb.Append(e); break;
                }
            }
            return sb.ToString();
        }

        private static void SkipValue(string text, ref int i)
        {
            SkipWs(text, ref i);
            if (i >= text.Length) return;
            char c = text[i];
            if (c == '"') { ReadString(text, ref i); return; }
            if (c == '{' || c == '[')
            {
                char open = c, close = (c == '{') ? '}' : ']';
                int depth = 0;
                bool inStr = false;
                while (i < text.Length)
                {
                    char d = text[i];
                    if (inStr)
                    {
                        if (d == '\\') { i += 2; continue; }
                        if (d == '"') inStr = false;
                        i++;
                        continue;
                    }
                    if (d == '"') { inStr = true; i++; continue; }
                    if (d == open) depth++;
                    else if (d == close)
                    {
                        depth--;
                        if (depth == 0) { i++; return; }
                    }
                    i++;
                }
                return;
            }
            while (i < text.Length && text[i] != ',' && text[i] != '}' && text[i] != ']') i++;
        }

        private static void SkipWs(string text, ref int i)
        {
            while (i < text.Length)
            {
                char c = text[i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') { i++; continue; }
                break;
            }
        }
    }
}
