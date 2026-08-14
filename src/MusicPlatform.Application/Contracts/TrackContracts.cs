using MusicPlatform.Domain.Enums;

namespace MusicPlatform.Application.Contracts;

/// <summary>Genre musical.</summary>
public sealed record GenreDto(Guid Id, string Name, string Slug, int? TrackCount);

/// <summary>Tag associé aux morceaux.</summary>
public sealed record TagDto(Guid Id, string Name, string Slug, int? TrackCount);

/// <summary>Album regroupant des morceaux.</summary>
public sealed record AlbumDto(Guid Id, string Name, string ArtistName, int? TrackCount);

/// <summary>Représentation d'un morceau dans les listes.</summary>
public sealed record TrackDto
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required string ArtistName { get; init; }
    public required int DurationSeconds { get; init; }
    public required ContentVisibility Visibility { get; init; }
    public required TrackStatus Status { get; init; }
    public required UserRefDto Owner { get; init; }
    public GenreDto? Genre { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public required CoverUrlsDto CoverUrls { get; init; }
    public required string StreamUrl { get; init; }

    /// <summary>Nul lorsque le propriétaire a masqué le compteur et que l'appelant n'est pas lui.</summary>
    public long? LikeCount { get; init; }

    /// <summary>Nul lorsque le propriétaire a masqué le compteur et que l'appelant n'est pas lui.</summary>
    public long? PlayCount { get; init; }

    /// <summary>Vrai si l'utilisateur courant a liké ce morceau. Nul pour un appel anonyme.</summary>
    public bool? IsLikedByCurrentUser { get; init; }

    public required DateTime CreatedAt { get; init; }
    public DateTime? PublishedAt { get; init; }
}

/// <summary>Détail complet d'un morceau.</summary>
public sealed record TrackDetailsDto
{
    public required TrackDto Track { get; init; }
    public string? Description { get; init; }
    public int? Year { get; init; }
    public AlbumDto? Album { get; init; }
    public required int CommentCount { get; init; }

    /// <summary>Présent uniquement pour le propriétaire et les administrateurs.</summary>
    public string? FailureReason { get; init; }

    /// <summary>Vrai si le morceau est masqué par la modération. Visible du propriétaire et de la modération.</summary>
    public bool? IsHidden { get; init; }
}

/// <summary>Création d'un morceau accompagnée de son fichier audio.</summary>
public sealed record CreateTrackRequest
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? ArtistName { get; init; }
    public Guid? AlbumId { get; init; }
    public Guid? GenreId { get; init; }
    public ContentVisibility Visibility { get; init; } = ContentVisibility.Private;
    public IReadOnlyList<string>? Tags { get; init; }
    public int? Year { get; init; }
}

/// <summary>Mise à jour partielle des métadonnées d'un morceau.</summary>
public sealed record UpdateTrackRequest
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? ArtistName { get; init; }
    public Guid? AlbumId { get; init; }
    public Guid? GenreId { get; init; }
    public ContentVisibility? Visibility { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public int? Year { get; init; }

    /// <summary>Détache le morceau de son album lorsque vrai.</summary>
    public bool ClearAlbum { get; init; }

    /// <summary>Détache le morceau de son genre lorsque vrai.</summary>
    public bool ClearGenre { get; init; }
}

/// <summary>Contenu binaire envoyé par le client, indépendant d'ASP.NET Core.</summary>
/// <param name="FileName">Nom d'origine fourni par le client.</param>
/// <param name="ContentType">Type MIME déclaré.</param>
/// <param name="Length">Taille annoncée en octets.</param>
/// <param name="OpenReadStream">Ouvre le flux de lecture du contenu.</param>
public sealed record UploadedFile(string FileName, string ContentType, long Length, Func<Stream> OpenReadStream);

/// <summary>Réponse renvoyée après l'acceptation d'un upload.</summary>
public sealed record UploadAcceptedDto(Guid TrackId, Guid UploadOperationId, TrackStatus Status);

/// <summary>Filtres de listage et de recherche des morceaux.</summary>
public sealed record TrackFilter
{
    public string? Query { get; init; }
    public string? Genre { get; init; }
    public string? Tag { get; init; }
    public string? Artist { get; init; }
    public int? MinDuration { get; init; }
    public int? MaxDuration { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }

    /// <summary>Tri demandé : <c>recent</c>, <c>popular</c>, <c>likes</c>, <c>title</c>, <c>duration</c>.</summary>
    public string? Sort { get; init; }
}

/// <summary>État du like de l'utilisateur courant sur un morceau.</summary>
public sealed record LikeStateDto(bool Liked, long? LikeCount);

/// <summary>Commentaire posté sur un morceau.</summary>
public sealed record CommentDto
{
    public required Guid Id { get; init; }
    public required Guid TrackId { get; init; }
    public required UserRefDto Author { get; init; }
    public required string Content { get; init; }
    public int? TimestampSeconds { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }

    /// <summary>Vrai si l'utilisateur courant peut modifier ce commentaire.</summary>
    public required bool CanEdit { get; init; }

    /// <summary>Vrai si l'utilisateur courant peut supprimer ce commentaire.</summary>
    public required bool CanDelete { get; init; }
}

/// <summary>Création d'un commentaire, éventuellement positionné dans le morceau.</summary>
public sealed record CreateCommentRequest(string Content, int? TimestampSeconds);

/// <summary>Modification du texte d'un commentaire.</summary>
public sealed record UpdateCommentRequest(string Content);

/// <summary>Déclaration d'une écoute par le client.</summary>
public sealed record RegisterPlayRequest
{
    public Guid? SessionId { get; init; }
    public int PositionSeconds { get; init; }
    public int DurationSeconds { get; init; }
    public string? Source { get; init; }
}

/// <summary>Résultat de l'enregistrement d'une écoute.</summary>
/// <param name="Counted">Vrai si l'écoute a été comptabilisée.</param>
/// <param name="Reason">Motif de rejet lorsque l'écoute n'a pas été comptée.</param>
/// <param name="PlayCount">Nouveau compteur, nul s'il est masqué pour l'appelant.</param>
public sealed record RegisterPlayResultDto(bool Counted, string? Reason, long? PlayCount);

/// <summary>Sauvegarde de la position de lecture courante.</summary>
public sealed record SaveProgressRequest(int PositionSeconds);
