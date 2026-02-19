using PhotoSauce.MagicScaler;

namespace AzureAISearchSimulator.Search.DocumentCracking;

/// <summary>
/// Normalizes extracted images to match Azure AI Search's normalized_images schema.
/// Converts images to JPEG, resizes to max dimensions, and corrects EXIF rotation.
/// Uses PhotoSauce.MagicScaler (MIT license) for image processing.
/// </summary>
public static class ImageNormalizer
{
    /// <summary>
    /// Normalizes a CrackedImage: converts to JPEG, resizes to max dimensions, reads EXIF rotation.
    /// Returns a dictionary matching the Azure normalized_images schema.
    /// </summary>
    /// <param name="image">The extracted image to normalize.</param>
    /// <param name="maxWidth">Maximum width in pixels (default 2000, range 50-10000).</param>
    /// <param name="maxHeight">Maximum height in pixels (default 2000, range 50-10000).</param>
    /// <returns>Dictionary with keys: data, width, height, originalWidth, originalHeight, rotationFromOriginal, contentOffset, pageNumber, boundingPolygon.</returns>
    public static Dictionary<string, object> Normalize(
        CrackedImage image,
        int maxWidth = 2000,
        int maxHeight = 2000)
    {
        // Clamp max dimensions to Azure's allowed range
        maxWidth = Math.Clamp(maxWidth, 50, 10000);
        maxHeight = Math.Clamp(maxHeight, 50, 10000);

        // Read original image metadata (dimensions + EXIF orientation)
        var info = ImageFileInfo.Load(image.Data);
        var frame = info.Frames[0];

        // FrameInfo Width/Height are already EXIF-corrected
        int correctedWidth = frame.Width;
        int correctedHeight = frame.Height;

        // Recover the stored (pre-rotation) dimensions for reporting
        bool swaps = OrientationSwapsDimensions(frame.ExifOrientation);
        int originalWidth = swaps ? correctedHeight : correctedWidth;
        int originalHeight = swaps ? correctedWidth : correctedHeight;
        int rotation = OrientationToRotation(frame.ExifOrientation);

        // Calculate target dimensions (preserves aspect ratio, no upscale)
        var (newW, newH) = FitDimensions(correctedWidth, correctedHeight, maxWidth, maxHeight);

        // Process: auto-orient, resize, encode JPEG q=75
        var settings = new ProcessImageSettings
        {
            Width = newW,
            Height = newH,
            // OrientationMode defaults to Normalize (auto EXIF correction)
        };
        settings.TrySetEncoderFormat("image/jpeg");

        using var outMs = new MemoryStream();
        MagicImageProcessor.ProcessImage((ReadOnlySpan<byte>)image.Data, outMs, settings);
        byte[] jpegBytes = outMs.ToArray();

        return new Dictionary<string, object>
        {
            ["data"] = Convert.ToBase64String(jpegBytes),
            ["width"] = newW,
            ["height"] = newH,
            ["originalWidth"] = originalWidth,
            ["originalHeight"] = originalHeight,
            ["rotationFromOriginal"] = rotation,
            ["contentOffset"] = image.ContentOffset,
            ["pageNumber"] = image.PageNumber,
            ["boundingPolygon"] = BuildBoundingPolygon(newW, newH)
        };
    }

    /// <summary>
    /// Normalizes a CrackedImage without processing — passes through raw image data as base64.
    /// Used as a fallback when images cannot be loaded by the image processor.
    /// </summary>
    public static Dictionary<string, object> NormalizeFallback(CrackedImage image)
    {
        return new Dictionary<string, object>
        {
            ["data"] = Convert.ToBase64String(image.Data),
            ["width"] = image.Width,
            ["height"] = image.Height,
            ["originalWidth"] = image.Width,
            ["originalHeight"] = image.Height,
            ["rotationFromOriginal"] = 0,
            ["contentOffset"] = image.ContentOffset,
            ["pageNumber"] = image.PageNumber,
            ["boundingPolygon"] = BuildBoundingPolygon(image.Width, image.Height)
        };
    }

    /// <summary>
    /// Calculates dimensions that fit within max bounds while preserving aspect ratio.
    /// Does not upscale images smaller than the max.
    /// </summary>
    internal static (int w, int h) FitDimensions(int w, int h, int maxW, int maxH)
    {
        if (w <= 0 || h <= 0) return (w, h);
        if (w <= maxW && h <= maxH) return (w, h);
        double ratio = Math.Min((double)maxW / w, (double)maxH / h);
        return (Math.Max(1, (int)(w * ratio)), Math.Max(1, (int)(h * ratio)));
    }

    /// <summary>
    /// Converts a PhotoSauce Orientation enum value to counter-clockwise rotation degrees.
    /// EXIF orientation values: 1=Normal, 3=180°, 6=90°CW(270°CCW), 8=270°CW(90°CCW).
    /// </summary>
    internal static int OrientationToRotation(Orientation orientation)
    {
        return (int)orientation switch
        {
            3 => 180,  // Rotated 180°
            6 => 270,  // Rotated 90° CW → 270° CCW
            8 => 90,   // Rotated 270° CW → 90° CCW
            _ => 0     // Normal or mirrored (we don't handle mirror)
        };
    }

    /// <summary>
    /// Returns true if the EXIF orientation swaps the width and height dimensions.
    /// Orientations 5-8 involve 90° or 270° rotation which swaps axes.
    /// </summary>
    private static bool OrientationSwapsDimensions(Orientation orientation)
    {
        int val = (int)orientation;
        return val >= 5 && val <= 8;
    }

    /// <summary>
    /// Builds a bounding polygon string matching Azure's format.
    /// Returns a JSON-encoded nested array of polygon points covering the full image.
    /// </summary>
    internal static string BuildBoundingPolygon(int w, int h)
    {
        return $"[[{{\"x\":0.0,\"y\":0.0}},{{\"x\":{w}.0,\"y\":0.0}},{{\"x\":0.0,\"y\":{h}.0}},{{\"x\":{w}.0,\"y\":{h}.0}}]]";
    }
}
