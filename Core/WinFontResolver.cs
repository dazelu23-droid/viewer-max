using System;
using System.Collections.Generic;
using System.IO;
using PdfSharp.Fonts;

namespace PdfEditor.Core
{
    // PdfSharp 6.x has no built-in font resolver, so drawing any text throws
    // unless we supply one. We map a few common families to the TrueType files
    // that ship with Windows. Anything unknown falls back to Arial so text
    // always renders rather than crashing the save.
    public sealed class WinFontResolver : IFontResolver
    {
        private static readonly string FontsDir =
            Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

        private static readonly Dictionary<string, string> FaceFiles =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "arial#regular",     "arial.ttf" },
            { "arial#bold",        "arialbd.ttf" },
            { "arial#italic",      "ariali.ttf" },
            { "arial#bolditalic",  "arialbi.ttf" },
            { "times new roman#regular",    "times.ttf" },
            { "times new roman#bold",       "timesbd.ttf" },
            { "times new roman#italic",     "timesi.ttf" },
            { "times new roman#bolditalic", "timesbi.ttf" },
            { "courier new#regular",    "cour.ttf" },
            { "courier new#bold",       "courbd.ttf" },
            { "courier new#italic",     "couri.ttf" },
            { "courier new#bolditalic", "courbi.ttf" },
            { "segoe ui#regular",   "segoeui.ttf" },
            { "segoe ui#bold",      "segoeuib.ttf" },
            { "calibri#regular",    "calibri.ttf" },
            { "calibri#bold",       "calibrib.ttf" },
        };

        private static readonly Dictionary<string, byte[]> Cache =
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        public static void Register()
        {
            if (GlobalFontSettings.FontResolver == null)
                GlobalFontSettings.FontResolver = new WinFontResolver();
        }

        public byte[] GetFont(string faceName)
        {
            lock (Cache)
            {
                if (Cache.TryGetValue(faceName, out var bytes))
                    return bytes;

                string file = FaceFiles.TryGetValue(faceName, out var f) ? f : "arial.ttf";
                string path = Path.Combine(FontsDir, file);
                if (!File.Exists(path))
                    path = Path.Combine(FontsDir, "arial.ttf");

                bytes = File.ReadAllBytes(path);
                Cache[faceName] = bytes;
                return bytes;
            }
        }

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            string style = isBold && isItalic ? "bolditalic"
                         : isBold ? "bold"
                         : isItalic ? "italic"
                         : "regular";

            string key = $"{familyName.ToLowerInvariant()}#{style}";
            if (!FaceFiles.ContainsKey(key))
            {
                // Fall back to Arial in the requested style, then Arial regular.
                string arialKey = $"arial#{style}";
                key = FaceFiles.ContainsKey(arialKey) ? arialKey : "arial#regular";
            }
            return new FontResolverInfo(key);
        }
    }
}
