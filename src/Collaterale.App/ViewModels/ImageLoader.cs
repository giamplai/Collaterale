using Collaterale.Core.Imaging;
using SkiaSharp;

namespace Collaterale.App.ViewModels;

/// <summary>Decodes picture files (PNG, JPEG, WebP, GIF, BMP, ICO…) into the plain RGBA buffers the comparison works on.</summary>
public static class ImageLoader
{
    /// <summary>Pictures larger than this many pixels are refused (RGBA needs 4 bytes each, and we keep several copies).</summary>
    public const long MaxPixels = 150_000_000;

    public static RgbaImage Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Decode(stream);
    }

    public static RgbaImage Decode(Stream stream)
    {
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("This file is not a picture format Collaterale can read.");
        var info = codec.Info;
        if ((long)info.Width * info.Height > MaxPixels)
        {
            throw new InvalidDataException($"The picture is too large ({info.Width}×{info.Height}).");
        }

        var target = new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(target);
        var result = codec.GetPixels(target, bitmap.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            throw new InvalidDataException($"The picture could not be decoded ({result}).");
        }

        return new RgbaImage(info.Width, info.Height, bitmap.GetPixelSpan().ToArray());
    }

    /// <summary>Encodes pixels as PNG (used by tests and by "save difference image").</summary>
    public static byte[] EncodePng(RgbaImage image)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        image.Pixels.AsSpan().CopyTo(bitmap.GetPixelSpan());
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
