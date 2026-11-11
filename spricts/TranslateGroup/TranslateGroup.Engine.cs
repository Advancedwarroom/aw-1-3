// TranslateGroup · Engine（翻译引擎：原生 TranslationServer 接入 + 多级匹配 + 持久化 + 缺词报告）
//
// 匹配顺序（每种语言独立缓存）：
//   1. 精确键（简中原文）
//   2. 反向索引：任意语言的译文 → 原键（已被 T() 翻译过的文本切换语言时也能重新翻译）
//   3. 带 {0} 占位符的模式键（正则匹配后代入参数）
//   4. 多行文本逐行翻译
//   5. 剥离首尾 emoji/符号/数字后翻译核心文本
//   6. 最长优先分词替换（西文/韩文自动补空格、全角标点转半角）
//   繁中：缺词时按简→繁单字表兜底；其它语言缺词记录到 user://missing_translations.csv
using Godot;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

public partial class TranslateGroup
{
    private const string SettingsPath = "user://settings.cfg";
    private const string UserOverridePath = "user://translations.csv";
    private const string MissingReportPath = "user://missing_translations.csv";
    private const int MaxSegmentKeyLength = 40;
    private const int MaxCacheEntries = 20000;

    private static readonly GameLanguage[] AllLanguages =
    {
        GameLanguage.English, GameLanguage.Japanese, GameLanguage.Korean, GameLanguage.German,
        GameLanguage.Russian, GameLanguage.SimplifiedChinese, GameLanguage.TraditionalChinese
    };

    private readonly object engineLock = new();
    private readonly Dictionary<string, string> reverseIndex = new();
    private readonly Dictionary<GameLanguage, Dictionary<string, string>> translateCache = new();
    private readonly List<(Regex re, string key)> patternKeys = new();
    private readonly Dictionary<char, char> tradCharMap = new();
    private readonly HashSet<string> missingTerms = new();
    private readonly List<GameTranslation> registeredTranslations = new();
    private int missingSavedCount = 0;
    private bool engineReady = false;

    public static string LocaleOf(GameLanguage lang) => lang switch
    {
        GameLanguage.English => "en",
        GameLanguage.Japanese => "ja",
        GameLanguage.Korean => "ko",
        GameLanguage.German => "de",
        GameLanguage.Russian => "ru",
        GameLanguage.SimplifiedChinese => "zh_CN",
        GameLanguage.TraditionalChinese => "zh_TW",
        _ => "en"
    };

    // ==================== 初始化 ====================
    private void InitEngine()
    {
        AddGeneratedTerms();
        LoadUserOverrides();

        for (int i = 0; i < TradMapFrom.Length && i < TradMapTo.Length; i++)
            tradCharMap[TradMapFrom[i]] = TradMapTo[i];

        BuildIndexes();

        foreach (var lang in AllLanguages)
        {
            var t = new GameTranslation(lang, LocaleOf(lang));
            TranslationServer.AddTranslation(t);
            registeredTranslations.Add(t);
        }
        engineReady = true;
    }

    private void BuildIndexes()
    {
        lock (engineLock)
        {
            reverseIndex.Clear();
            patternKeys.Clear();
            foreach (var lang in AllLanguages) translateCache[lang] = new Dictionary<string, string>();

            foreach (var kv in translations)
            {
                string key = kv.Key;
                foreach (var v in kv.Value.Values)
                {
                    if (string.IsNullOrEmpty(v) || v == key) continue;
                    // 过短的西文缩写（如 "W"、"U"）不入反向索引，避免误伤普通文本
                    if (v.Length < 2 && !ContainsCjk(v)) continue;
                    reverseIndex.TryAdd(v, key);
                }
                if (key.Contains("{0}"))
                {
                    string pattern = "^" + Regex.Replace(Regex.Escape(key), @"\\\{(\d+)\}", m => $"(?<p{m.Groups[1].Value}>.+?)") + "$";
                    try { patternKeys.Add((new Regex(pattern, RegexOptions.Singleline | RegexOptions.CultureInvariant), key)); }
                    catch { }
                }
            }
            patternKeys.Sort((a, b) => b.key.Length.CompareTo(a.key.Length));
        }
    }

    // ==================== 核心：翻译任意文本 ====================
    public string Translate(string src, GameLanguage lang)
    {
        if (string.IsNullOrEmpty(src) || translations == null) return src ?? "";
        bool hasCjk = ContainsCjk(src);
        if (!hasCjk && src.IndexOf('\n') < 0 && !reverseIndex.ContainsKey(src)) return src;

        lock (engineLock)
        {
            var cache = translateCache[lang];
            if (cache.TryGetValue(src, out var hit)) return hit;
            string result = TranslateUncached(src, lang);
            if (cache.Count >= MaxCacheEntries) cache.Clear();
            cache[src] = result;
            return result;
        }
    }

    private string TranslateUncached(string src, GameLanguage lang)
    {
        if (TryWhole(src, lang, out var whole)) return whole;

        if (src.IndexOf('\n') >= 0)
        {
            var lines = src.Split('\n');
            for (int i = 0; i < lines.Length; i++)
                lines[i] = TranslateLine(lines[i], lang);
            return string.Join("\n", lines);
        }
        return TranslateLine(src, lang);
    }

    private string TranslateLine(string line, GameLanguage lang)
    {
        if (string.IsNullOrEmpty(line)) return line;
        if (TryWhole(line, lang, out var whole)) return whole;
        if (!ContainsCjk(line)) return line;

        // 剥离首尾非文字字符（emoji、图标、数字、括号、空白）
        int a = 0, b = line.Length;
        while (a < b && !IsWordChar(line[a])) a++;
        while (b > a && !IsWordChar(line[b - 1])) b--;
        if (a > 0 || b < line.Length)
        {
            string core = line.Substring(a, b - a);
            if (core.Length > 0 && TryWhole(core, lang, out var coreT))
                return ConvertPunct(line.Substring(0, a), lang) + coreT + ConvertPunct(line.Substring(b), lang);
        }
        return Segment(line, lang);
    }

    private bool TryWhole(string s, GameLanguage lang, out string result)
    {
        result = null;
        if (translations.ContainsKey(s)) { result = Lookup(s, lang); return true; }
        if (reverseIndex.TryGetValue(s, out var key)) { result = Lookup(key, lang); return true; }
        if (ContainsCjk(s))
        {
            foreach (var (re, pk) in patternKeys)
            {
                var m = re.Match(s);
                if (!m.Success) continue;
                string fmt = Lookup(pk, lang);
                result = Regex.Replace(fmt, @"\{(\d+)\}", x =>
                {
                    var g = m.Groups["p" + x.Groups[1].Value];
                    return g.Success ? Translate(g.Value, lang) : x.Value;
                });
                return true;
            }
        }
        return false;
    }

    private string Lookup(string key, GameLanguage lang)
    {
        if (lang == GameLanguage.SimplifiedChinese) return key;
        if (translations.TryGetValue(key, out var d))
        {
            if (d.TryGetValue(lang, out var v) && !string.IsNullOrEmpty(v)) return v;
            if (lang == GameLanguage.TraditionalChinese) return ToTraditional(key);
            if (d.TryGetValue(GameLanguage.English, out var en) && !string.IsNullOrEmpty(en)) return en;
        }
        return lang == GameLanguage.TraditionalChinese ? ToTraditional(key) : key;
    }

    // 最长优先分词替换
    private string Segment(string s, GameLanguage lang)
    {
        var sb = new StringBuilder(s.Length * 2);
        bool spaced = UsesSpaces(lang);
        bool lastTranslated = false;
        int i = 0, n = s.Length;
        StringBuilder unknown = null;

        while (i < n)
        {
            char ch = s[i];
            if (IsCjk(ch))
            {
                int maxLen = Math.Min(MaxSegmentKeyLength, n - i);
                bool found = false;
                for (int len = maxLen; len >= 1; len--)
                {
                    string sub = s.Substring(i, len);
                    if (!translations.ContainsKey(sub)) continue;
                    FlushUnknown(ref unknown, lang);
                    string piece = Lookup(sub, lang);
                    AppendPiece(sb, piece, spaced, true, ref lastTranslated);
                    i += len;
                    found = true;
                    break;
                }
                if (found) continue;

                if (lang == GameLanguage.TraditionalChinese)
                    AppendPiece(sb, ToTraditional(ch.ToString()), false, false, ref lastTranslated);
                else
                {
                    if (lang != GameLanguage.SimplifiedChinese) (unknown ??= new StringBuilder()).Append(ch);
                    AppendPiece(sb, ch.ToString(), false, false, ref lastTranslated);
                }
                i++;
                continue;
            }

            FlushUnknown(ref unknown, lang);
            string p = ConvertPunctChar(ch, lang, i + 1 < n ? s[i + 1] : '\0', sb.Length > 0 ? sb[sb.Length - 1] : '\0');
            AppendPiece(sb, p, spaced, false, ref lastTranslated);
            i++;
        }
        FlushUnknown(ref unknown, lang);
        return sb.ToString();
    }

    private static void AppendPiece(StringBuilder sb, string piece, bool spaced, bool translated, ref bool lastTranslated)
    {
        if (string.IsNullOrEmpty(piece)) return;
        // 词条自带前导空格（如 " tiles"）时，行首或已有空白后去掉
        if (translated && piece[0] == ' ' && (sb.Length == 0 || char.IsWhiteSpace(sb[sb.Length - 1]) || sb[sb.Length - 1] == '('))
            piece = piece.TrimStart(' ');
        if (piece.Length == 0) return;
        if (spaced && sb.Length > 0 && (translated || lastTranslated))
        {
            char prev = sb[sb.Length - 1];
            char next = piece[0];
            if (IsSpaceJoinableLeft(prev) && IsSpaceJoinableRight(next)) sb.Append(' ');
        }
        sb.Append(piece);
        lastTranslated = translated;
    }

    private static bool IsSpaceJoinableLeft(char c) =>
        char.IsLetterOrDigit(c) && !IsCjk(c) || c == ')' || c == '%' || c == ']';

    private static bool IsSpaceJoinableRight(char c) =>
        char.IsLetterOrDigit(c) && !IsCjk(c) || c == '(' || c == '[';

    private void FlushUnknown(ref StringBuilder unknown, GameLanguage lang)
    {
        if (unknown == null || unknown.Length == 0) return;
        if (missingTerms.Add(unknown.ToString()) && missingTerms.Count - missingSavedCount >= 50)
            CallDeferred(nameof(SaveMissingReport));
        unknown.Clear();
    }

    private static bool UsesSpaces(GameLanguage lang) =>
        lang == GameLanguage.English || lang == GameLanguage.German ||
        lang == GameLanguage.Russian || lang == GameLanguage.Korean;

    private static bool IsChineseFamily(GameLanguage lang) =>
        lang == GameLanguage.SimplifiedChinese || lang == GameLanguage.TraditionalChinese;

    private static string ConvertPunct(string s, GameLanguage lang)
    {
        if (string.IsNullOrEmpty(s) || IsChineseFamily(lang) || lang == GameLanguage.Japanese) return s;
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
            sb.Append(ConvertPunctChar(s[i], lang, i + 1 < s.Length ? s[i + 1] : '\0', sb.Length > 0 ? sb[sb.Length - 1] : '\0'));
        return sb.ToString();
    }

    // 全角标点 → 西文标点（日文保留全角，仅把中文逗号换成「、」）
    private static string ConvertPunctChar(char c, GameLanguage lang, char next, char prev)
    {
        if (IsChineseFamily(lang)) return c.ToString();
        if (lang == GameLanguage.Japanese) return c == '，' ? "、" : c.ToString();
        bool needSpace = next != '\0' && !char.IsWhiteSpace(next) && next != '\n';
        string sp = needSpace ? " " : "";
        switch (c)
        {
            case '：': return ":" + sp;
            case '，': return "," + sp;
            case '、': return "," + sp;
            case '；': return ";" + sp;
            case '。': return "." + sp;
            case '！': return "!" + sp;
            case '？': return "?" + sp;
            case '（': return (prev != '\0' && !char.IsWhiteSpace(prev) && prev != '(' ? " (" : "(");
            case '）': return ")";
            case '【': return "[";
            case '】': return "]";
            case '《': case '》': case '“': case '”': return "\"";
            case '‘': case '’': return "'";
            case '～': return "~";
            case '　': return " ";
            default: return c.ToString();
        }
    }

    private string ToTraditional(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var arr = s.ToCharArray();
        for (int i = 0; i < arr.Length; i++)
            if (tradCharMap.TryGetValue(arr[i], out var t)) arr[i] = t;
        return new string(arr);
    }

    public static bool IsCjk(char c) => c >= '\u4e00' && c <= '\u9fff';

    public static bool ContainsCjk(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (char c in text) if (IsCjk(c)) return true;
        return false;
    }

    private static bool IsWordChar(char c) => IsCjk(c) || char.IsLetter(c);

    // ==================== 语言持久化 ====================
    private bool LoadSavedLanguage()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(SettingsPath) != Error.Ok) return false;
        if (!cfg.HasSectionKey("general", "language")) return false;
        string loc = (string)cfg.GetValue("general", "language", "");
        foreach (var lang in AllLanguages)
        {
            if (LocaleOf(lang) == loc) { CurrentLanguage = lang; return true; }
        }
        return false;
    }

    private void SaveLanguage()
    {
        var cfg = new ConfigFile();
        cfg.Load(SettingsPath); // 保留其它设置
        cfg.SetValue("general", "language", LocaleOf(CurrentLanguage));
        cfg.Save(SettingsPath);
    }

    private void ApplyLocale()
    {
        TranslationServer.SetLocale(LocaleOf(CurrentLanguage));
    }

    // ==================== 外部覆盖词表 ====================
    // user://translations.csv：key,en,ja,ko,de,ru,zh_CN,zh_TW（首行可为表头；空单元格不覆盖）
    private void LoadUserOverrides()
    {
        if (!FileAccess.FileExists(UserOverridePath)) return;
        using var f = FileAccess.Open(UserOverridePath, FileAccess.ModeFlags.Read);
        if (f == null) return;
        int count = 0;
        while (!f.EofReached())
        {
            string[] cols = f.GetCsvLine();
            if (cols == null || cols.Length < 2 || string.IsNullOrEmpty(cols[0])) continue;
            if (cols[0] == "key") continue;
            if (!translations.TryGetValue(cols[0], out var d))
            {
                d = new Dictionary<GameLanguage, string> { [GameLanguage.SimplifiedChinese] = cols[0] };
                translations[cols[0]] = d;
            }
            for (int i = 1; i < cols.Length && i <= AllLanguages.Length; i++)
                if (!string.IsNullOrEmpty(cols[i])) d[AllLanguages[i - 1]] = cols[i];
            count++;
        }
        GD.Print($"[TranslateGroup] Loaded {count} overrides from {UserOverridePath}");
    }

    // ==================== 缺词报告 ====================
    public void SaveMissingReport()
    {
        lock (engineLock)
        {
            if (missingTerms.Count == 0 || missingTerms.Count == missingSavedCount) return;
            using var f = FileAccess.Open(MissingReportPath, FileAccess.ModeFlags.Write);
            if (f == null) return;
            f.StoreCsvLine(new[] { "key", "en", "ja", "ko", "de", "ru", "zh_CN", "zh_TW" });
            foreach (var m in missingTerms)
                f.StoreCsvLine(new[] { m, "", "", "", "", "", m, "" });
            missingSavedCount = missingTerms.Count;
        }
    }

    public int MissingTermCount => missingTerms.Count;

    public override void _ExitTree()
    {
        SaveMissingReport();

        // Explicitly release the language selector UI before the autoload leaves the tree.
        // This is also important for headless test shutdown, where the engine otherwise
        // reports the Panel/Label and their theme resources as leaked instances.
        if (uiLayer != null && IsInstanceValid(uiLayer))
            uiLayer.Free();
        uiLayer = null;
        langButton = null;
        langPanel = null;
        langList = null;

        foreach (var t in registeredTranslations)
            if (IsInstanceValid(t)) TranslationServer.RemoveTranslation(t);
        registeredTranslations.Clear();

        // SetText retains scene controls in a static list for language refresh.
        _registered.Clear();
        Instance = null;
    }
}
