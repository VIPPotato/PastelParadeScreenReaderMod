using MelonLoader;

namespace PastelParadeAccess;

/// <summary>
/// Lightweight debug logger gated by the runtime debug mode flag.
/// </summary>
internal static class DebugLogger
{
	private static volatile bool _enabled;

	/// <summary>
	/// Gets whether debug logging is currently enabled.
	/// </summary>
	internal static bool Enabled => _enabled;

	/// <summary>
	/// Updates debug logging state.
	/// </summary>
	internal static void SetEnabled(bool enabled)
	{
		_enabled = enabled;
	}

	/// <summary>
	/// Writes a debug line to MelonLoader log when debug mode is enabled.
	/// </summary>
	internal static void Log(string message)
	{
		if (!_enabled) return;
		if (string.IsNullOrWhiteSpace(message)) return;
		try
		{
			MelonLogger.Msg("[Debug] " + message);
		}
		catch
		{
		}
	}
}

