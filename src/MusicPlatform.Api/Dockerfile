# syntax=docker/dockerfile:1

# ---------------------------------------------------------------------------
# Étape de compilation.
# Les fichiers projet sont copiés avant le code source afin que la restauration
# des paquets NuGet reste en cache tant que les dépendances ne changent pas.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app
COPY Directory.Build.props ./
COPY MusicPlatform.slnx ./
COPY src/ ./src/

RUN dotnet restore src/MusicPlatform.Api/MusicPlatform.Api.csproj
RUN dotnet publish src/MusicPlatform.Api/MusicPlatform.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---------------------------------------------------------------------------
# Image d'exécution.
# ICU est conservé : la normalisation Unicode est indispensable à la génération
# des slugs de tags et de genres accentués.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl sert uniquement à la sonde de santé du conteneur ; l'application s'exécute
# sous le compte sans privilège fourni par l'image de base.
#
# L'import depuis un lien YouTube ajoute trois dépendances :
#   - yt-dlp, paquet Python installé dans un environnement virtuel dédié afin de ne
#     pas entrer en conflit avec les paquets Python du système ;
#   - ffmpeg, dont yt-dlp se sert pour extraire la piste audio en MP3 ;
#   - nodejs, requis par yt-dlp pour résoudre les challenges JavaScript de YouTube
#     (signature et n-challenge) que servent les clients « web ».
#
# yt-dlp doit rester à jour : YouTube modifie régulièrement ses extracteurs, et une
# version figée finit par échouer même avec des cookies valides. Reconstruire l'image
# sans cache (docker compose build --no-cache api) réinstalle la dernière version.
# Retirer ces paquets ne casse pas l'application : seul l'import par lien devient
# indisponible et répond alors 503.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ffmpeg python3 python3-venv nodejs \
    && python3 -m venv /opt/ytdlp \
    && /opt/ytdlp/bin/pip install --no-cache-dir --upgrade yt-dlp \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data/storage /data/import /data/cache \
    && chown -R app:app /data

COPY --from=build --chown=app:app /app/publish ./

# XDG_CACHE_HOME donne à yt-dlp un emplacement de cache accessible en écriture :
# l'application tourne sous un compte sans privilège dont le dossier personnel
# ne l'est pas nécessairement.
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    Storage__RootPath=/data/storage \
    YtDlp__ExecutablePath=/opt/ytdlp/bin/yt-dlp \
    YtDlp__WorkingDirectory=/data/import \
    XDG_CACHE_HOME=/data/cache

USER app
EXPOSE 8080

# Le volume conserve les fichiers audio, les pochettes et les archives d'export.
VOLUME ["/data/storage"]

HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
    CMD curl --fail --silent http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "MusicPlatform.Api.dll"]
