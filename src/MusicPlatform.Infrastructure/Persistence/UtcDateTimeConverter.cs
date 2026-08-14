using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MusicPlatform.Infrastructure.Persistence;

/// <summary>
/// Force toutes les dates à être écrites et relues en UTC.
/// Sans cela, une date construite localement serait persistée avec un décalage silencieux.
/// </summary>
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    value => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime(),
    value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

/// <summary>Variante nullable de <see cref="UtcDateTimeConverter"/>.</summary>
public sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
    value => value == null ? null : value.Value.Kind == DateTimeKind.Utc ? value : value.Value.ToUniversalTime(),
    value => value == null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
