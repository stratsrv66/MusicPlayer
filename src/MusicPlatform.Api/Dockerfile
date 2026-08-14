# syntax=docker/dockerfile:1

# ---------------------------------------------------------------------------
# Étape de compilation.
# Les fichiers projet sont copiés avant le code source afin que la restauration
# des paquets NuGet reste en cache tant que les dépendances ne changent pas.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /
COPY Directory.Build.props ./
COPY MusicPlatform.slnx ./
COPY src/ ./src/

RUN dotnet restore src/MusicPlatform.Api/MusicPlatform.Api.csproj

RUN dotnet publish src/MusicPlatform.Api/MusicPlatform.Api.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

# ---------------------------------------------------------------------------
# Image d'exécution.
# ICU est conservé : la normalisation Unicode est indispensable à la génération
# des slugs de tags et de genres accentués.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl sert uniquement à la sonde de santé du conteneur ; l'application s'exécute
# sous le compte sans privilège fourni par l'image de base.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data/storage \
    && chown -R app:app /data

COPY --from=build --chown=app:app /app/publish ./

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    Storage__RootPath=/data/storage

USER app
EXPOSE 8080

# Le volume conserve les fichiers audio, les pochettes et les archives d'export.
VOLUME ["/data/storage"]

HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
    CMD curl --fail --silent http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "MusicPlatform.Api.dll"]
