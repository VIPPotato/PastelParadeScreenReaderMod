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
        ["tip_prefix"] = "Tip: "
    };

    private static readonly Dictionary<string, string> _jp = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["mod_loaded"] = "パステルパレード アクセシビリティMODを読み込みました。",
        ["tip_prefix"] = "ヒント: "
    };

    private static readonly Dictionary<string, string> _zhCn = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["mod_loaded"] = "Pastel Parade 无障碍模组已加载。",
        ["tip_prefix"] = "提示："
    };

    private static readonly Dictionary<string, string> _zhTw = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["mod_loaded"] = "Pastel Parade 無障礙模組已載入。",
        ["tip_prefix"] = "提示："
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
