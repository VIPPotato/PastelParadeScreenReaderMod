using System;
using System.Collections.Generic;
using System.Reflection;

namespace PastelParadeAccess;

/// <summary>
/// Localization for mod-owned spoken strings.
/// Dynamic game text remains sourced from the game itself.
/// </summary>
public static class Loc
{
    private static readonly Dictionary<string, string> _en = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["mod_loaded"] = "Pastel Parade accessibility mod loaded.",
        ["tip_prefix"] = "Tip: ",
        ["debug_mode_on"] = "Debug mode on.",
        ["debug_mode_off"] = "Debug mode off.",
        ["menu_position_on"] = "Menu position announcements on.",
        ["menu_position_off"] = "Menu position announcements off.",
        ["role_slider"] = "slider",
        ["menu_position_suffix"] = "{0} of {1}",
        ["settings_tab_sound"] = "Sound",
        ["settings_tab_display"] = "Display",
        ["settings_tab_with_position"] = "{0} tab {1} of {2}",
        ["settings_tab_without_position"] = "{0} tab",
        ["offset_slider_label"] = "Offset",
        ["ui_back"] = "Back",
        ["ui_unknown_item"] = "Item",
        ["hub_music_preview"] = "Music Preview",
        ["ui_fullscreen"] = "Fullscreen"
    };

    private static readonly Dictionary<string, string> _jp = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["mod_loaded"] = "パステルパレード アクセシビリティMODを読み込みました。",
        ["tip_prefix"] = "ヒント: ",
        ["debug_mode_on"] = "デバッグモードをオンにしました。",
        ["debug_mode_off"] = "デバッグモードをオフにしました。",
        ["menu_position_on"] = "メニュー位置読み上げをオンにしました。",
        ["menu_position_off"] = "メニュー位置読み上げをオフにしました。",
        ["role_slider"] = "スライダー",
        ["menu_position_suffix"] = "{1}中{0}",
        ["settings_tab_sound"] = "サウンド",
        ["settings_tab_display"] = "表示",
        ["settings_tab_with_position"] = "{0}タブ {1}/{2}",
        ["settings_tab_without_position"] = "{0}タブ",
        ["offset_slider_label"] = "オフセット",
        ["ui_back"] = "戻る",
        ["ui_unknown_item"] = "項目",
        ["hub_music_preview"] = "音楽プレビュー",
        ["ui_fullscreen"] = "フルスクリーン"
    };

    private static readonly Dictionary<string, string> _zhCn = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["mod_loaded"] = "Pastel Parade 无障碍模组已加载。",
        ["tip_prefix"] = "提示：",
        ["debug_mode_on"] = "调试模式已开启。",
        ["debug_mode_off"] = "调试模式已关闭。",
        ["menu_position_on"] = "菜单位置播报已开启。",
        ["menu_position_off"] = "菜单位置播报已关闭。",
        ["role_slider"] = "滑块",
        ["menu_position_suffix"] = "第{0}项，共{1}项",
        ["settings_tab_sound"] = "声音",
        ["settings_tab_display"] = "显示",
        ["settings_tab_with_position"] = "{0}选项卡 第{1}项，共{2}项",
        ["settings_tab_without_position"] = "{0}选项卡",
        ["offset_slider_label"] = "偏移",
        ["ui_back"] = "返回",
        ["ui_unknown_item"] = "项目",
        ["hub_music_preview"] = "音乐预览",
        ["ui_fullscreen"] = "全屏"
    };

    private static readonly Dictionary<string, string> _zhTw = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["mod_loaded"] = "Pastel Parade 無障礙模組已載入。",
        ["tip_prefix"] = "提示：",
        ["debug_mode_on"] = "除錯模式已開啟。",
        ["debug_mode_off"] = "除錯模式已關閉。",
        ["menu_position_on"] = "選單位置播報已開啟。",
        ["menu_position_off"] = "選單位置播報已關閉。",
        ["role_slider"] = "滑桿",
        ["menu_position_suffix"] = "第{0}項，共{1}項",
        ["settings_tab_sound"] = "聲音",
        ["settings_tab_display"] = "顯示",
        ["settings_tab_with_position"] = "{0}分頁 第{1}項，共{2}項",
        ["settings_tab_without_position"] = "{0}分頁",
        ["offset_slider_label"] = "偏移",
        ["ui_back"] = "返回",
        ["ui_unknown_item"] = "項目",
        ["hub_music_preview"] = "音樂預覽",
        ["ui_fullscreen"] = "全螢幕"
    };

    private static string _currentLanguage = "en";

    /// <summary>
    /// Initializes localization state.
    /// </summary>
    public static void Initialize()
    {
        RefreshLanguage();
    }

    /// <summary>
    /// Refreshes language from game save data when available.
    /// </summary>
    public static void RefreshLanguage()
    {
        string lang = "en";

        try
        {
            Type saveManagerType = AccessToolsBridge.TypeByName("PastelParade.SaveManager");
            object saveManagerInstance = AccessToolsBridge.GetStaticPropertyValue(saveManagerType, "I")
                ?? AccessToolsBridge.GetStaticFieldValue(saveManagerType, "I")
                ?? AccessToolsBridge.GetStaticPropertyValue(saveManagerType, "Instance")
                ?? AccessToolsBridge.GetStaticFieldValue(saveManagerType, "Instance");

            object current = AccessToolsBridge.GetPropertyValue(saveManagerInstance, "Current")
                ?? AccessToolsBridge.GetFieldValue(saveManagerInstance, "_current");

            string fromSave = AccessToolsBridge.GetPropertyValue(current, "Language") as string
                ?? AccessToolsBridge.GetFieldValue(current, "Language") as string;

            if (!string.IsNullOrWhiteSpace(fromSave))
            {
                lang = fromSave;
            }
        }
        catch
        {
            // Keep fallback language.
        }

        _currentLanguage = NormalizeLanguage(lang);
    }

    /// <summary>
    /// Gets a localized mod string.
    /// </summary>
    public static string Get(string key)
    {
        Dictionary<string, string> dict = GetDictionary(_currentLanguage);

        if (dict.TryGetValue(key, out string value))
        {
            return value;
        }

        if (_en.TryGetValue(key, out string fallback))
        {
            return fallback;
        }

        return key;
    }

    private static Dictionary<string, string> GetDictionary(string lang)
    {
        switch (lang)
        {
            case "jp":
                return _jp;
            case "zh-CN":
                return _zhCn;
            case "zh-TW":
                return _zhTw;
            default:
                return _en;
        }
    }

    private static string NormalizeLanguage(string lang)
    {
        if (string.IsNullOrWhiteSpace(lang))
        {
            return "en";
        }

        string normalized = lang.Trim();

        if (normalized.Equals("ja", StringComparison.OrdinalIgnoreCase) || normalized.Equals("jp", StringComparison.OrdinalIgnoreCase))
        {
            return "jp";
        }

        if (normalized.Equals("zh-cn", StringComparison.OrdinalIgnoreCase))
        {
            return "zh-CN";
        }

        if (normalized.Equals("zh-tw", StringComparison.OrdinalIgnoreCase))
        {
            return "zh-TW";
        }

        if (normalized.Equals("en", StringComparison.OrdinalIgnoreCase))
        {
            return "en";
        }

        return "en";
    }

    /// <summary>
    /// Minimal reflection helpers to avoid hard dependency on Harmony in localization.
    /// </summary>
    private static class AccessToolsBridge
    {
        public static Type TypeByName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = asm.GetType(typeName, throwOnError: false);
                    if (t != null)
                    {
                        return t;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        public static object GetStaticPropertyValue(Type type, string propertyName)
        {
            if (type == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return null;
            }

            PropertyInfo p = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return p?.GetValue(null);
        }

        public static object GetStaticFieldValue(Type type, string fieldName)
        {
            if (type == null || string.IsNullOrWhiteSpace(fieldName))
            {
                return null;
            }

            FieldInfo f = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return f?.GetValue(null);
        }

        public static object GetPropertyValue(object instance, string propertyName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return null;
            }

            PropertyInfo p = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return p?.GetValue(instance);
        }

        public static object GetFieldValue(object instance, string fieldName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(fieldName))
            {
                return null;
            }

            FieldInfo f = instance.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return f?.GetValue(instance);
        }
    }
}
