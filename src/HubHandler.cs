namespace PastelParadeAccess;

/// <summary>
/// Hub-related speech formatting and normalization.
/// </summary>
internal static class HubHandler
{
    /// <summary>
    /// Builds a localized tip announcement for hub tips.
    /// </summary>
    public static string BuildTipAnnouncement(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return Loc.Get("tip_prefix") + text;
    }
}
