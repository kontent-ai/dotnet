namespace Kontent.Ai.AspNetCore.ImageTransformation;

/// <summary>
/// Options for the image transformation tag helpers.
/// </summary>
public sealed class ImageTransformationOptions
{
    /// <summary>
    /// Widths in which a given image is available. This property is used to generate the resulting srcset.
    /// </summary>
    public int[]? ResponsiveWidths { get; set; }
}
