namespace MusicPlatform.Domain.Enums;

/// <summary>Rôles applicatifs. L'ordre reflète le niveau de privilège croissant.</summary>
public enum UserRole
{
    User = 0,
    Artist = 1,
    Moderator = 2,
    Admin = 3,
}

/// <summary>Statut administratif d'un compte utilisateur.</summary>
public enum UserStatus
{
    Active = 0,
    Suspended = 1,
}

/// <summary>Visibilité d'un profil utilisateur.</summary>
public enum ProfileVisibility
{
    Public = 0,
    Private = 1,
}

/// <summary>Visibilité d'un contenu (morceau ou playlist).</summary>
public enum ContentVisibility
{
    /// <summary>Visible par tous et référencé dans la recherche.</summary>
    Public = 0,

    /// <summary>Accessible via son lien direct mais non référencé.</summary>
    Unlisted = 1,

    /// <summary>Accessible uniquement au propriétaire.</summary>
    Private = 2,
}

/// <summary>Étape du pipeline de traitement d'un morceau.</summary>
public enum TrackStatus
{
    Uploading = 0,
    Processing = 1,
    Ready = 2,
    Failed = 3,
}

/// <summary>Étape d'une opération d'upload de fichier audio.</summary>
public enum UploadOperationStatus
{
    Uploading = 0,
    Processing = 1,
    Ready = 2,
    Failed = 3,
    Cancelled = 4,
}

/// <summary>Tailles de pochettes générées lors du traitement.</summary>
public enum CoverSize
{
    Small = 0,
    Medium = 1,
    Large = 2,
    Original = 3,
}

/// <summary>Type de contenu visé par un signalement.</summary>
public enum ReportTargetType
{
    Track = 0,
    Comment = 1,
    User = 2,
    Playlist = 3,
}

/// <summary>Motif d'un signalement.</summary>
public enum ReportReason
{
    Copyright = 0,
    Offensive = 1,
    Spam = 2,
    Other = 3,
}

/// <summary>Cycle de vie d'un signalement côté modération.</summary>
public enum ReportStatus
{
    Pending = 0,
    Reviewing = 1,
    Resolved = 2,
    Rejected = 3,
}

/// <summary>Cycle de vie d'une demande d'export de données personnelles.</summary>
public enum UserExportStatus
{
    Pending = 0,
    Processing = 1,
    Ready = 2,
    Failed = 3,
    Expired = 4,
}
