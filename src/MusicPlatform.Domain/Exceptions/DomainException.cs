namespace MusicPlatform.Domain.Exceptions;

/// <summary>
/// Erreur de règle métier détectée dans le domaine.
/// Le code est réutilisé tel quel dans le champ <c>code</c> des Problem Details.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string code, string message) : base(message) => Code = code;

    /// <summary>Code métier stable, par exemple <c>TRACK_NOT_READY</c>.</summary>
    public string Code { get; }
}
