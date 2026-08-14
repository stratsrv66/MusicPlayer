using MusicPlatform.Application.Common;
using MusicPlatform.Application.Contracts;
using MusicPlatform.Application.Features.Tracks;

namespace MusicPlatform.Api.Infrastructure;

/// <summary>
/// Adapte les fichiers reçus par ASP.NET Core vers les contrats applicatifs,
/// afin que la couche Application ne dépende pas d'<c>IFormFile</c>.
/// </summary>
public static class FormFileExtensions
{
    /// <summary>
    /// Convertit un fichier de formulaire en contrat applicatif. Le contenu n'est pas lu :
    /// le flux n'est ouvert qu'au moment de l'écriture dans le stockage.
    /// </summary>
    public static UploadedFile ToUploadedFile(this IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            throw new InputValidationException("file", "An audio file is required.");
        }

        return new UploadedFile(
            file.FileName,
            file.ContentType ?? "application/octet-stream",
            file.Length,
            file.OpenReadStream);
    }

    /// <summary>
    /// Charge une image en mémoire après validation de son nom et de sa taille.
    /// Les images sont bornées à 5 Mo, ce qui rend le chargement complet acceptable.
    /// </summary>
    public static async Task<UploadedImage> ToUploadedImageAsync(this IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            throw new InputValidationException("file", "An image file is required.");
        }

        ImageFileValidator.Validate(file.FileName, file.Length);

        using var buffer = new MemoryStream((int)file.Length);
        await using (var source = file.OpenReadStream())
        {
            await source.CopyToAsync(buffer, cancellationToken);
        }

        return new UploadedImage(file.FileName, buffer.ToArray());
    }
}
