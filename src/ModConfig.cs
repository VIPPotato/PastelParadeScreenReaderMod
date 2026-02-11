using System;
using MelonLoader;

namespace PastelParadeAccess;

/// <summary>
/// Persistent mod settings stored through MelonPreferences.
/// </summary>
internal static class ModConfig
{
	private static bool _initialized;
	private static MelonPreferences_Category _category;
	private static MelonPreferences_Entry<bool> _debugMode;
	private static MelonPreferences_Entry<bool> _menuPositionAnnouncements;

	/// <summary>
	/// Current debug logging toggle.
	/// </summary>
	internal static bool DebugModeEnabled => _debugMode != null && _debugMode.Value;

	/// <summary>
	/// Current menu position announcement toggle.
	/// </summary>
	internal static bool MenuPositionAnnouncementsEnabled => _menuPositionAnnouncements != null && _menuPositionAnnouncements.Value;

	/// <summary>
	/// Initializes config entries once.
	/// </summary>
	internal static void Initialize()
	{
		if (_initialized) return;
		try
		{
			_category = MelonPreferences.CreateCategory("PastelParadeAccess");
			_debugMode = _category.CreateEntry("DebugMode", false);
			_menuPositionAnnouncements = _category.CreateEntry("MenuPositionAnnouncements", false);
			_initialized = true;
		}
		catch (Exception ex)
		{
			try { MelonLogger.Warning("TolkExporter: ModConfig.Initialize failed: " + ex); } catch { }
		}
	}

	/// <summary>
	/// Persists debug mode.
	/// </summary>
	internal static void SetDebugMode(bool enabled)
	{
		try
		{
			if (!_initialized) Initialize();
			if (_debugMode == null) return;
			_debugMode.Value = enabled;
			MelonPreferences.Save();
		}
		catch (Exception ex)
		{
			try { MelonLogger.Warning("TolkExporter: ModConfig.SetDebugMode failed: " + ex); } catch { }
		}
	}

	/// <summary>
	/// Persists menu position announcement mode.
	/// </summary>
	internal static void SetMenuPositionAnnouncements(bool enabled)
	{
		try
		{
			if (!_initialized) Initialize();
			if (_menuPositionAnnouncements == null) return;
			_menuPositionAnnouncements.Value = enabled;
			MelonPreferences.Save();
		}
		catch (Exception ex)
		{
			try { MelonLogger.Warning("TolkExporter: ModConfig.SetMenuPositionAnnouncements failed: " + ex); } catch { }
		}
	}
}

