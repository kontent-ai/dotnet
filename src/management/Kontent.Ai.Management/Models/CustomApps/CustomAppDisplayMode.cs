namespace Kontent.Ai.Management.Models.CustomApps;

/// <summary>
/// Represents the display mode of a custom app.
/// </summary>
public enum CustomAppDisplayMode
{
    /// <summary>
    /// The custom app is displayed in full screen.
    /// </summary>
    [JsonStringEnumMemberName("fullScreen")]
    FullScreen,

    /// <summary>
    /// The custom app is displayed in a dialog.
    /// </summary>
    [JsonStringEnumMemberName("dialog")]
    Dialog
}
