// TermsExtract: dump TextAssets (and an asset inventory) from an Among Us Addressables bundle COPY (tools/terms/run.ps1).
// usage: TermsExtract <bundle-copy> <outDir> [tsvDir] [glossary-keys.tsv]
//   outDir   : inventory.txt and textassets\ (the raw TextAssets)
//   tsvDir   : official-<lang>.tsv and glossary.tsv (default: outDir)
//   glossary-keys.tsv : the keys of glossary.tsv (default: <tsvDir>\glossary-keys.tsv; no file = no glossary)
// The output is the game's own text (Innersloth's): keep it local, never commit it (tools/terms/local is git-ignored).
using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

if (args.Length < 2) { Console.Error.WriteLine("usage: TermsExtract <bundle-copy> <outDir> [tsvDir] [glossary-keys.tsv]"); return 1; }
string bundlePath = args[0];
string outDir = args[1];
Directory.CreateDirectory(outDir);
string rawDir = Path.Combine(outDir, "textassets");
Directory.CreateDirectory(rawDir);

var am = new AssetsManager();
var bun = am.LoadBundleFile(bundlePath, true); // decompress LZ4/LZMA in memory
var bf = bun.file;
var inv = new StringBuilder();
var langAssets = new Dictionary<string, byte[]>();
inv.AppendLine($"unityVersion\t{bf.Header.EngineVersion}\tformat\t{bf.Header.Version}");
int dirCount = bf.BlockAndDirInfo.DirectoryInfos.Count;
for (int i = 0; i < dirCount; i++)
{
    string fname = bf.GetFileName(i);
    bool isAssets = bf.IsAssetsFile(i);
    inv.AppendLine($"entry\t{i}\t{fname}\tassetsFile={isAssets}");
    if (!isAssets) continue;
    var fi = am.LoadAssetsFileFromBundle(bun, i, false);
    var af = fi.file;
    inv.AppendLine($"  metadataUnityVersion\t{af.Metadata.UnityVersion}\ttypeTree={af.Metadata.TypeTreeEnabled}\tassets={af.AssetInfos.Count}");
    var byType = af.AssetInfos.GroupBy(a => a.TypeId).OrderBy(g => g.Key);
    foreach (var g in byType) inv.AppendLine($"  type\t{g.Key}\t{(AssetClassID)g.Key}\tcount\t{g.Count()}");

    var reader = af.Reader;
    foreach (var info in af.AssetInfos)
    {
        if (info.TypeId != (int)AssetClassID.TextAsset) continue;
        long off = info.GetAbsoluteByteOffset(af);
        reader.Position = off;
        int nlen = reader.ReadInt32();
        string name = Encoding.UTF8.GetString(reader.ReadBytes(nlen));
        reader.Align();
        int slen = reader.ReadInt32();
        byte[] script = reader.ReadBytes(slen);
        string safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        string outFile = Path.Combine(rawDir, $"{safe}__{info.PathId}.txt");
        File.WriteAllBytes(outFile, script);
        langAssets[name] = script;
        inv.AppendLine($"  TextAsset\tpathId={info.PathId}\tname={name}\tbytes={slen}\t-> {Path.GetFileName(outFile)}");
    }

    // MonoBehaviour names for inventory only (uses the bundle's own type tree)
    foreach (var info in af.AssetInfos)
    {
        if (info.TypeId != (int)AssetClassID.MonoBehaviour) continue;
        try
        {
            var bfld = am.GetBaseField(fi, info);
            string mname = bfld["m_Name"].AsString;
            inv.AppendLine($"  MonoBehaviour\tpathId={info.PathId}\tname={mname}\tbytes={info.ByteSize}");
        }
        catch (Exception e) { inv.AppendLine($"  MonoBehaviour\tpathId={info.PathId}\t(err {e.GetType().Name})\tbytes={info.ByteSize}"); }
    }
}
File.WriteAllText(Path.Combine(outDir, "inventory.txt"), inv.ToString(), new UTF8Encoding(false));
Console.WriteLine(string.Join("\n", inv.ToString().Split('\n').Where(l => !l.Contains("MonoBehaviour\tpathId"))));

// ---- phase 2: language TextAssets -> official-<code>.tsv (key<TAB>text) ----
// Source format: first line "KEY ID<TAB><Language>", then "StringName<TAB>text" (LF).
// Text keeps the file's own escapes (literal \n, \t, \r). A real TAB inside the text
// (5 Tooltip* rows) is written as literal \t so each TSV row stays exactly 2 columns.
// Rows without a TAB are continuation lines of the previous value (a few Xbox error texts)
// and are appended as literal \n. Rows with an empty key (spreadsheet spacer rows) are skipped.
var codes = new (string unity, string code)[] { ("English", "en"), ("SChinese", "zh-CN"), ("TChinese", "zh-TW"), ("Japanese", "ja") };
string tsvDir = args.Length >= 3 ? args[2] : outDir;
var tables = new Dictionary<string, Dictionary<string, string>>();
foreach (var (unity, code) in codes)
{
    if (!langAssets.TryGetValue(unity, out var bytes)) { Console.WriteLine($"MISSING language TextAsset: {unity}"); continue; }
    string text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿');
    var rows = new List<(string k, string v)>();
    int cont = 0, spacer = 0;
    foreach (var rawLine in text.Split('\n'))
    {
        string line = rawLine.TrimEnd('\r');
        if (line.StartsWith("KEY ID\t")) continue;
        int tab = line.IndexOf('\t');
        if (tab < 0)
        {
            if (line.Length == 0) { if (rows.Count > 0) { /* blank line: may be inside a multi-line value */ } continue; }
            if (rows.Count > 0) { var last = rows[^1]; rows[^1] = (last.k, last.v + "\\n" + line); cont++; }
            continue;
        }
        string key = line[..tab];
        string val = line[(tab + 1)..].Replace("\t", "\\t");
        if (key.Length == 0) { spacer++; continue; }
        rows.Add((key, val));
    }
    var sb = new StringBuilder();
    foreach (var (k, v) in rows) sb.Append(k).Append('\t').Append(v).Append('\n');
    string tsv = Path.Combine(tsvDir, $"official-{code}.tsv");
    File.WriteAllText(tsv, sb.ToString(), new UTF8Encoding(false));
    Console.WriteLine($"{unity} -> {tsv}: {rows.Count} keys (continuations {cont}, spacer rows {spacer})");
    tables[code] = rows.ToDictionary(r => r.k, r => r.v);
}

// ---- phase 3: glossary.tsv from glossary-keys.tsv (concept<TAB>key; '#' lines ignored) ----
string keysFile = args.Length >= 4 ? args[3] : Path.Combine(tsvDir, "glossary-keys.tsv");
if (File.Exists(keysFile))
{
    var g = new StringBuilder();
    g.Append("concept\tkey\ten\tzh-CN\tzh-TW\tja\n");
    int missing = 0;
    foreach (var line in File.ReadAllLines(keysFile, Encoding.UTF8))
    {
        if (line.Length == 0 || line.StartsWith("#")) continue;
        var p = line.Split('\t');
        if (p.Length < 2) continue;
        string key = p[1].Trim();
        g.Append(p[0]).Append('\t').Append(key);
        foreach (var c in new[] { "en", "zh-CN", "zh-TW", "ja" })
        {
            string v = tables.TryGetValue(c, out var t) && t.TryGetValue(key, out var s) ? s : "(NOT FOUND)";
            if (v == "(NOT FOUND)") missing++;
            g.Append('\t').Append(v);
        }
        g.Append('\n');
    }
    File.WriteAllText(Path.Combine(tsvDir, "glossary.tsv"), g.ToString(), new UTF8Encoding(false));
    Console.WriteLine($"glossary.tsv written ({missing} missing cells)");
}
return 0;
