using MusicPlatform.Application.Abstractions;
using MusicPlatform.Application.Common;
using MusicPlatform.Domain.Enums;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace MusicPlatform.Infrastructure.Media;

/// <summary>
/// Traitement d'images via ImageSharp. Les pochettes sont recadrées en carré puis
/// encodées en WebP, format qui réduit nettement le poids à qualité équivalente.
/// </summary>
public sealed class ImageSharpProcessor : IImageProcessor
{
    /// <summary>Arêtes des déclinaisons de pochette, en pixels.</summary>
    private const int SmallEdge = 120;
    private const int MediumEdge = 300;
    private const int LargeEdge = 800;

    /// <summary>Qualité d'encodage WebP, compromis entre poids et rendu.</summary>
    private const int WebpQuality = 82;

    /// <summary>Dimension maximale acceptée en entrée, pour borner le coût du décodage.</summary>
    private const int MaxInputEdge = 6000;

    /// <inheritdoc />
    public IReadOnlyList<ResizedImage> CreateCoverVariants(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var image = Decode(source);

        return
        [
            Render(image, CoverSize.Small, SmallEdge),
            Render(image, CoverSize.Medium, MediumEdge),
            Render(image, CoverSize.Large, LargeEdge),
        ];
    }

    /// <inheritdoc />
    public ResizedImage CreateSquare(byte[] source, int edgeSizePixels)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(edgeSizePixels, 16);

        using var image = Decode(source);
        return Render(image, CoverSize.Original, edgeSizePixels);
    }

    /// <summary>Décode l'image en refusant les contenus invalides ou démesurés.</summary>
    private static Image Decode(byte[] source)
    {
        Image image;

        try
        {
            image = Image.Load(source);
        }
        catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException)
        {
            throw new UnprocessableException(ErrorCodes.TrackUploadInvalid, "The uploaded file is not a valid image.");
        }

        if (image.Width > MaxInputEdge || image.Height > MaxInputEdge)
        {
            image.Dispose();
            throw new UnprocessableException(
                ErrorCodes.TrackUploadInvalid,
                $"The image dimensions cannot exceed {MaxInputEdge}x{MaxInputEdge} pixels.");
        }

        return image;
    }

    /// <summary>Recadre l'image en carré à la taille demandée et l'encode en WebP.</summary>
    private static ResizedImage Render(Image source, CoverSize size, int edge)
    {
        using var clone = source.Clone(context => context.Resize(new ResizeOptions
        {
            Size = new Size(edge, edge),
            Mode = ResizeMode.Crop,
            Position = AnchorPositionMode.Center,
        }));

        using var buffer = new MemoryStream();
        clone.Save(buffer, new WebpEncoder { Quality = WebpQuality });

        return new ResizedImage(size, buffer.ToArray(), clone.Width, clone.Height);
    }
}
