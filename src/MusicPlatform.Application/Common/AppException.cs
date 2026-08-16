namespace MusicPlatform.Application.Common;

/// <summary>Codes métier exposés dans le champ <c>code</c> des Problem Details.</summary>
public static class ErrorCodes
{
    public const string AuthInvalidCredentials = "AUTH_INVALID_CREDENTIALS";
    public const string AuthUnauthorized = "AUTH_UNAUTHORIZED";
    public const string AuthEmailTaken = "AUTH_EMAIL_TAKEN";
    public const string AuthUsernameTaken = "AUTH_USERNAME_TAKEN";
    public const string AuthInvalidRefreshToken = "AUTH_INVALID_REFRESH_TOKEN";
    public const string AuthAccountSuspended = "AUTH_ACCOUNT_SUSPENDED";
    public const string Forbidden = "FORBIDDEN";
    public const string UserNotFound = "USER_NOT_FOUND";
    public const string TrackNotFound = "TRACK_NOT_FOUND";
    public const string TrackNotReady = "TRACK_NOT_READY";
    public const string TrackAccessDenied = "TRACK_ACCESS_DENIED";
    public const string TrackUploadInvalid = "TRACK_UPLOAD_INVALID";
    public const string TrackUploadTooLarge = "TRACK_UPLOAD_TOO_LARGE";
    public const string TrackFileMissing = "TRACK_FILE_MISSING";
    public const string TrackImportFailed = "TRACK_IMPORT_FAILED";
    public const string TrackImportUnavailable = "TRACK_IMPORT_UNAVAILABLE";
    public const string PlaylistImportNotFound = "PLAYLIST_IMPORT_NOT_FOUND";
    public const string PlaylistImportUnreadable = "PLAYLIST_IMPORT_UNREADABLE";
    public const string PlaylistImportUnsupportedSource = "PLAYLIST_IMPORT_UNSUPPORTED_SOURCE";
    public const string PlaylistImportNotConfigured = "PLAYLIST_IMPORT_NOT_CONFIGURED";
    public const string PlaylistImportTooLarge = "PLAYLIST_IMPORT_TOO_LARGE";
    public const string PlaylistImportAlreadyRunning = "PLAYLIST_IMPORT_ALREADY_RUNNING";
    public const string CoverNotFound = "COVER_NOT_FOUND";
    public const string PlaylistNotFound = "PLAYLIST_NOT_FOUND";
    public const string PlaylistAccessDenied = "PLAYLIST_ACCESS_DENIED";
    public const string PlaylistTrackAlreadyPresent = "PLAYLIST_TRACK_ALREADY_PRESENT";
    public const string PlaylistTrackNotPresent = "PLAYLIST_TRACK_NOT_PRESENT";
    public const string PlaylistFull = "PLAYLIST_FULL";
    public const string CommentNotFound = "COMMENT_NOT_FOUND";
    public const string ReportNotFound = "REPORT_NOT_FOUND";
    public const string ReportTargetNotFound = "REPORT_TARGET_NOT_FOUND";
    public const string GenreNotFound = "GENRE_NOT_FOUND";
    public const string GenreInUse = "GENRE_IN_USE";
    public const string GenreAlreadyExists = "GENRE_ALREADY_EXISTS";
    public const string AlbumNotFound = "ALBUM_NOT_FOUND";
    public const string ExportNotFound = "EXPORT_NOT_FOUND";
    public const string ExportNotReady = "EXPORT_NOT_READY";
    public const string ExportAlreadyRunning = "EXPORT_ALREADY_RUNNING";
    public const string AccountDeletionNotConfirmed = "ACCOUNT_DELETION_NOT_CONFIRMED";
    public const string InvalidVisibility = "INVALID_VISIBILITY";
    public const string ValidationError = "VALIDATION_ERROR";
    public const string RateLimitExceeded = "RATE_LIMIT_EXCEEDED";
    public const string UnsupportedMediaType = "UNSUPPORTED_MEDIA_TYPE";
    public const string Conflict = "CONFLICT";
}

/// <summary>Exception applicative portant un code métier et un statut HTTP.</summary>
public abstract class AppException : Exception
{
    protected AppException(string code, string message, int statusCode) : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public int StatusCode { get; }
}

/// <summary>Ressource inexistante ou invisible pour l'appelant : 404.</summary>
public sealed class NotFoundException(string code, string message) : AppException(code, message, 404);

/// <summary>Appelant authentifié mais sans droit sur la ressource : 403.</summary>
public sealed class ForbiddenException(string message = "You are not allowed to perform this action.", string code = ErrorCodes.Forbidden)
    : AppException(code, message, 403);

/// <summary>Appelant non authentifié alors que la ressource l'exige : 401.</summary>
public sealed class UnauthorizedException(string message = "Authentication is required.", string code = ErrorCodes.AuthUnauthorized)
    : AppException(code, message, 401);

/// <summary>Conflit avec l'état courant de la ressource : 409.</summary>
public sealed class ConflictException(string code, string message) : AppException(code, message, 409);

/// <summary>Requête syntaxiquement correcte mais métier invalide : 422.</summary>
public sealed class UnprocessableException(string code, string message) : AppException(code, message, 422);

/// <summary>Entrée invalide, avec le détail par champ : 400.</summary>
public sealed class InputValidationException : AppException
{
    public InputValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base(ErrorCodes.ValidationError, "One or more validation errors occurred.", 400) => Errors = errors;

    public InputValidationException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = [message] })
    {
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

/// <summary>Fichier envoyé trop volumineux : 413.</summary>
public sealed class PayloadTooLargeException(string message) : AppException(ErrorCodes.TrackUploadTooLarge, message, 413);

/// <summary>Type de contenu non pris en charge : 415.</summary>
public sealed class UnsupportedMediaTypeException(string message)
    : AppException(ErrorCodes.UnsupportedMediaType, message, 415);

/// <summary>Dépendance externe indisponible ou non configurée : 503.</summary>
public sealed class ServiceUnavailableException(string code, string message) : AppException(code, message, 503);
