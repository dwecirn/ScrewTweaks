#nullable enable

using System.Collections.Generic;
using System.Globalization;

namespace ScrewTweaks.Panel
{
    public enum PanelLanguage
    {
        English,
        ChineseSimplified,
        Japanese
    }

    /// <summary>
    /// Panel text localisation, keyed by the English source string.
    ///
    ///      GUILayout.Label(Loc.T("Knee velocity"));
    ///
    /// Keying by the source text rather than by an identifier means there is no key bookkeeping and a
    /// missing translation degrades to readable English instead of to a raw key. A plugin that ships a
    /// panel section registers its own strings in its Start():
    ///
    ///      Loc.Add(PanelLanguage.Japanese, ("Knee velocity", "ニー速度"));
    ///
    /// English is the source language and needs no table: Loc.T returns its argument unchanged. Anything
    /// that is an identifier rather than prose - model and algorithm *names*, config keys, enum values -
    /// is deliberately not translated, because those same strings are what the config file stores.
    /// </summary>
    public static class Loc
    {
        private static readonly Dictionary<PanelLanguage, Dictionary<string, string>> Tables =
            new Dictionary<PanelLanguage, Dictionary<string, string>>();

        private static readonly PanelLanguage[] Order =
        {
            PanelLanguage.English,
            PanelLanguage.ChineseSimplified,
            PanelLanguage.Japanese
        };

        static Loc()
        {
            AddBuiltInPanelStrings();
        }

        /// <summary>In the order they should be offered in the UI.</summary>
        public static IReadOnlyList<PanelLanguage> All => Order;

        /// <summary>The language currently in use. Set from the panel's Settings section.</summary>
        public static PanelLanguage Language { get; private set; } = PanelLanguage.English;

        /// <summary>A language's name written in that language. Never translated.</summary>
        public static string DisplayName(PanelLanguage language)
        {
            switch (language)
            {
                case PanelLanguage.ChineseSimplified: return "中文";
                case PanelLanguage.Japanese: return "日本語";
                default: return "English";
            }
        }

        /// <summary>Translate, or return the English source when there is no translation.</summary>
        public static string T(string english)
        {
            if (string.IsNullOrEmpty(english)) return english;
            if (Language == PanelLanguage.English) return english;

            var table = TableFor(Language);
            if (table != null && table.TryGetValue(english, out string? translated) && !string.IsNullOrEmpty(translated))
                return translated;

            return english;
        }

        /// <summary>Translate a format string and fill it in. The source is used if untranslated.</summary>
        public static string Tf(string english, params object[] args)
        {
            string format = T(english);
            try
            {
                return string.Format(CultureInfo.InvariantCulture, format, args);
            }
            catch
            {
                // A translation with a broken placeholder must not take the panel down.
                return format;
            }
        }

        /// <summary>Add or replace one translation. English is the source language and is ignored.</summary>
        public static void Add(PanelLanguage language, string english, string translation)
        {
            if (language == PanelLanguage.English) return;
            if (string.IsNullOrEmpty(english) || string.IsNullOrEmpty(translation)) return;

            var table = TableFor(language, create: true)!;
            table[english] = translation;
        }

        /// <summary>Add or replace several translations at once.</summary>
        public static void Add(PanelLanguage language, params (string English, string Translation)[] entries)
        {
            foreach (var entry in entries)
                Add(language, entry.English, entry.Translation);
        }

        internal static void SetLanguage(PanelLanguage language)
        {
            Language = language;
        }

        private static Dictionary<string, string>? TableFor(PanelLanguage language, bool create = false)
        {
            if (Tables.TryGetValue(language, out var table)) return table;
            if (!create) return null;

            table = new Dictionary<string, string>();
            Tables[language] = table;
            return table;
        }

        /// <summary>Strings the panel itself draws. Everything else is registered by its own plugin.</summary>
        private static void AddBuiltInPanelStrings()
        {
            Add(PanelLanguage.ChineseSimplified,
                ("Settings", "设置"),
                ("Panel settings", "面板设置"),
                ("Tab side", "导航栏位置"),
                ("Left", "左侧"),
                ("Right", "右侧"),
                ("Language", "语言"),
                ("Panel file", "配置文件"),
                ("Version", "版本"),
                ("Settings are saved to {0}.", "设置保存在 {0}。"),
                ("Modules", "已加载模块"),
                ("Read from BepInEx, so this is what is loaded rather than what should be.",
                    "从 BepInEx 读取，所以这是实际加载的东西，而不是应该加载的东西。"),
                ("Drag the title bar to move the window, the dotted grip in a corner to resize it.",
                    "拖动标题栏移动窗口，拖动角落的点状手柄改变大小。"),
                ("No sections registered.", "没有插件注册板块。"),
                ("Section error: {0}", "板块出错：{0}"));

            Add(PanelLanguage.Japanese,
                ("Settings", "設定"),
                ("Panel settings", "パネルの設定"),
                ("Tab side", "タブの位置"),
                ("Left", "左"),
                ("Right", "右"),
                ("Language", "言語"),
                ("Panel file", "設定ファイル"),
                ("Version", "バージョン"),
                ("Settings are saved to {0}.", "設定は {0} に保存されます。"),
                ("Modules", "読み込み済みモジュール"),
                ("Read from BepInEx, so this is what is loaded rather than what should be.",
                    "BepInEx から取得しているので、あるべきものではなく実際に読み込まれているものです。"),
                ("Drag the title bar to move the window, the dotted grip in a corner to resize it.",
                    "タイトルバーで移動、隅のドット状グリップでサイズ変更できます。"),
                ("No sections registered.", "セクションが登録されていません。"),
                ("Section error: {0}", "セクションエラー: {0}"));
        }
    }
}
