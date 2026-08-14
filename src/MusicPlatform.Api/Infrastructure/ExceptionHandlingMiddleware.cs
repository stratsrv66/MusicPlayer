using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MusicPlatform.Application.Common;
using MusicPlatform.Domain.Exceptions;

namespace MusicPlatform.Api.Infrastructure;

/// <summary>
/// Convertit toute exception non gérée en réponse Problem Details homogène,
/// enrichie d'un code métier et d'un identifiant de trace.
///
/// Aucun détail interne n'est renvoyé hors développement : les erreurs inattendues
/// sont journalisées côté serveur et présentées au client sous une forme générique.
/// </summary>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    /// <summary>Préfixe des URI de type d'erreur exposées dans le champ <c>type</c>.</summary>
    private const string ErrorTypeBaseUri = "https://musicplatform.dev/errors/";

    /// <inheritdoc cref="RequestDelegate" />
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            await HandleAsync(context, exception);
        }
    }

    /// <summary>Écrit la réponse d'erreur si aucune réponse n'a encore été envoyée.</summary>
    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        if (context.Response.HasStarted)
        {
            // La réponse est déjà partie (streaming en cours) : on ne peut que journaliser.
            logger.LogError(exception, "An exception occurred after the response had started.");
            return;
        }

        var descriptor = Describe(exception);
        LogException(exception, descriptor.Status);

        context.Response.Clear();
        context.Response.StatusCode = descriptor.Status;

        var problem = new ProblemDetails
        {
            Type = ErrorTypeBaseUri + descriptor.Code.ToLowerInvariant().Replace('_', '-'),
            Title = descriptor.Title,
            Status = descriptor.Status,
            Detail = descriptor.Detail,
            Instance = context.Request.Path,
        };

        problem.Extensions["code"] = descriptor.Code;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;

        if (descriptor.Errors is not null)
        {
            problem.Extensions["errors"] = descriptor.Errors;
        }

        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    /// <summary>Traduit une exception en statut HTTP, code métier et message présentable.</summary>
    private ErrorDescriptor Describe(Exception exception) => exception switch
    {
        InputValidationException validation => new ErrorDescriptor(
            400, validation.Code, "Validation failed", validation.Message, validation.Errors),

        AppException app => new ErrorDescriptor(
            app.StatusCode, app.Code, TitleFor(app.StatusCode), app.Message, null),

        DomainException domain => new ErrorDescriptor(
            422, domain.Code, "Business rule violation", domain.Message, null),

        BadHttpRequestException badRequest => new ErrorDescriptor(
            badRequest.StatusCode, ErrorCodes.ValidationError, "Bad request", badRequest.Message, null),

        OperationCanceledException => new ErrorDescriptor(
            499, "REQUEST_CANCELLED", "Request cancelled", "The request was cancelled by the client.", null),

        _ => new ErrorDescriptor(
            500,
            "INTERNAL_ERROR",
            "Internal server error",
            environment.IsDevelopment() ? exception.Message : "An unexpected error occurred.",
            null),
    };

    /// <summary>Journalise en erreur les défauts serveur, en avertissement les erreurs client.</summary>
    private void LogException(Exception exception, int status)
    {
        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled exception resulting in status {Status}.", status);
            return;
        }

        logger.LogDebug(exception, "Request rejected with status {Status}.", status);
    }

    /// <summary>Titre standard associé à un statut HTTP.</summary>
    private static string TitleFor(int status) => status switch
    {
        400 => "Bad request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not found",
        409 => "Conflict",
        413 => "Payload too large",
        415 => "Unsupported media type",
        422 => "Unprocessable entity",
        429 => "Too many requests",
        _ => "Error",
    };

    /// <summary>Description normalisée d'une erreur avant sérialisation.</summary>
    private sealed record ErrorDescriptor(
        int Status,
        string Code,
        string Title,
        string Detail,
        IReadOnlyDictionary<string, string[]>? Errors);
}
