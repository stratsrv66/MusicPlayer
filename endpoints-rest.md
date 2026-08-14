# Endpoints REST --- Plateforme musicale

## 1. Convention

Base URL :

``` text
/api/v1
```

Authentification :

``` text
Authorization: Bearer <access_token>
```

Format :

``` text
application/json
```

Les uploads utilisent :

``` text
multipart/form-data
```

Les erreurs utilisent Problem Details avec un champ métier `code`.

## 2. Authentification

### POST /auth/register

Créer un compte.

``` json
{
  "email": "user@example.com",
  "username": "artist123",
  "password": "********"
}
```

### POST /auth/login

``` json
{
  "email": "user@example.com",
  "password": "********"
}
```

Retour :

``` json
{
  "accessToken": "...",
  "refreshToken": "...",
  "expiresIn": 900
}
```

### POST /auth/refresh

Renouvelle l'access token.

### POST /auth/logout

Invalide la session/refresh token.

## 3. Utilisateur courant

### GET /me

Retourne le profil courant.

### PATCH /me

Modifie :

-   username ;
-   bio ;
-   avatar ;
-   liens sociaux ;
-   visibilité.

### GET /me/settings

### PATCH /me/settings

Modifie notamment :

``` json
{
  "showLikeCount": true,
  "showPlayCount": false
}
```

### GET /me/history

Historique d'écoute paginé.

### GET /me/likes

Morceaux aimés.

### GET /me/following

Utilisateurs suivis.

### GET /me/followers

Abonnés.

## 4. Export de données

### POST /me/data-export

Crée une demande d'export.

Retour :

``` json
{
  "id": "uuid",
  "status": "PENDING"
}
```

### GET /me/data-exports

Liste les exports.

### GET /me/data-exports/{exportId}

État d'un export.

### GET /me/data-exports/{exportId}/download

Télécharge l'export lorsqu'il est disponible.

### DELETE /me

Demande la suppression du compte.

Le backend doit refuser la suppression immédiate si une confirmation
explicite n'est pas fournie.

## 5. Profils

### GET /users/{username}

Retourne un profil public.

### GET /users/{username}/tracks

Morceaux publics d'un utilisateur.

### GET /users/{username}/playlists

Playlists publiques d'un utilisateur.

### POST /users/{userId}/follow

Suit un utilisateur.

### DELETE /users/{userId}/follow

Ne suit plus l'utilisateur.

### GET /users/{userId}/followers

### GET /users/{userId}/following

## 6. Morceaux

### GET /tracks

Liste paginée.

Filtres :

``` text
q
genre
tag
artist
minDuration
maxDuration
from
to
sort
page
pageSize
```

### GET /tracks/{trackId}

Retourne les informations publiques du morceau.

Les statistiques likes/vues respectent les préférences de visibilité du
propriétaire.

### POST /tracks

Crée un morceau/upload.

`multipart/form-data`.

Champs indicatifs :

``` text
file
title
description
artistName
albumId
genreId
visibility
tags[]
cover
```

### POST /tracks/{trackId}/upload

Permet d'envoyer/remplacer le fichier audio.

### GET /tracks/{trackId}/stream

Retourne le flux audio.

Support obligatoire de :

``` text
Range: bytes=...
```

### PATCH /tracks/{trackId}

Modifie les métadonnées.

### DELETE /tracks/{trackId}

Supprime le morceau du propriétaire.

### POST /tracks/{trackId}/publish

Publie le morceau.

### POST /tracks/{trackId}/unpublish

Retire le morceau de la publication.

## 7. Pochette

### POST /tracks/{trackId}/cover

Upload d'une pochette personnalisée.

### DELETE /tracks/{trackId}/cover

Supprime/remplace la pochette.

### GET /tracks/{trackId}/cover/{size}

Retourne une taille :

``` text
small
medium
large
```

## 8. Tags

### GET /tags

Liste/recherche les tags.

### GET /tags/{tag}/tracks

Morceaux associés au tag.

## 9. Genres

### GET /genres

Liste des genres.

### GET /genres/{genreId}/tracks

Morceaux du genre.

## 10. Likes

### POST /tracks/{trackId}/like

Like un morceau.

### DELETE /tracks/{trackId}/like

Retire le like.

### GET /tracks/{trackId}/like

Retourne l'état du like courant.

## 11. Écoute

### POST /tracks/{trackId}/plays

Enregistre une lecture validée.

Payload :

``` json
{
  "sessionId": "uuid",
  "positionSeconds": 10,
  "durationSeconds": 10,
  "source": "PLAYER"
}
```

Le serveur reste responsable de déterminer si l'événement constitue
réellement une écoute valide.

### PUT /tracks/{trackId}/progress

Sauvegarde la position d'écoute courante.

Payload :

``` json
{
  "positionSeconds": 143
}
```

### GET /tracks/{trackId}/progress

Retourne la dernière position de l'utilisateur courant.

## 12. Commentaires

### GET /tracks/{trackId}/comments

Liste paginée.

### POST /tracks/{trackId}/comments

``` json
{
  "content": "Très bon passage !",
  "timestampSeconds": 94
}
```

### PATCH /comments/{commentId}

Modifie son commentaire.

### DELETE /comments/{commentId}

Supprime son commentaire.

## 13. Playlists

### GET /playlists

Playlists visibles accessibles selon les permissions.

### POST /playlists

``` json
{
  "name": "Mes morceaux préférés",
  "description": "...",
  "visibility": "PUBLIC"
}
```

### GET /playlists/{playlistId}

### PATCH /playlists/{playlistId}

### DELETE /playlists/{playlistId}

### POST /playlists/{playlistId}/cover

Upload de couverture.

### GET /playlists/{playlistId}/tracks

Liste les morceaux.

### POST /playlists/{playlistId}/tracks

``` json
{
  "trackId": "uuid"
}
```

### DELETE /playlists/{playlistId}/tracks/{trackId}

Supprime un morceau.

### PATCH /playlists/{playlistId}/tracks/reorder

Exemple :

``` json
{
  "items": [
    {
      "trackId": "uuid-1",
      "position": 0
    },
    {
      "trackId": "uuid-2",
      "position": 1
    }
  ]
}
```

### POST /playlists/{playlistId}/duplicate

Duplique la playlist.

### POST /playlists/{playlistId}/follow

Suit une playlist.

### DELETE /playlists/{playlistId}/follow

Ne suit plus la playlist.

### POST /playlists/{playlistId}/favorite

Ajoute aux favoris.

### DELETE /playlists/{playlistId}/favorite

Retire des favoris.

## 14. Recherche

### GET /search

Paramètres :

``` text
q
type
genre
tag
artist
minDuration
maxDuration
sort
page
pageSize
```

Types :

``` text
TRACK
USER
ALBUM
PLAYLIST
TAG
ALL
```

Exemple :

``` text
GET /api/v1/search?q=%23rock&type=TRACK
```

## 15. Accueil

### GET /home

Retourne :

-   morceaux récents ;
-   populaires ;
-   artistes populaires ;
-   playlists populaires ;
-   recommandations ;
-   contenus des artistes suivis.

## 16. Recommandations

### GET /recommendations/tracks

Retourne les recommandations personnalisées.

### GET /recommendations/artists

Retourne des artistes recommandés.

Le moteur du MVP utilise des règles simples.

## 17. Statistiques artiste

### GET /me/analytics/overview

Retourne :

-   écoutes ;
-   likes ;
-   followers ;
-   nombre de morceaux.

### GET /me/analytics/tracks

Statistiques par morceau.

### GET /me/analytics/plays

Paramètres :

``` text
from
to
groupBy=day|week|month
```

### GET /me/analytics/top-tracks

Morceaux les plus écoutés.

## 18. Signalements

### POST /reports

``` json
{
  "targetType": "TRACK",
  "targetId": "uuid",
  "reason": "COPYRIGHT",
  "description": "..."
}
```

### GET /me/reports

Historique des signalements créés.

## 19. Administration

Toutes les routes suivantes nécessitent la permission admin.

### GET /admin/users

Liste des utilisateurs.

### GET /admin/users/{userId}

### PATCH /admin/users/{userId}

Modifier statut/rôle.

### DELETE /admin/users/{userId}

Suppression administrative.

### GET /admin/tracks

Liste globale des morceaux.

### DELETE /admin/tracks/{trackId}

Suppression administrative.

### POST /admin/tracks/{trackId}/hide

Masque un morceau.

### POST /admin/tracks/{trackId}/restore

Restaure un morceau.

### GET /admin/reports

Liste des signalements.

Filtres :

``` text
status
reason
targetType
date
```

### GET /admin/reports/{reportId}

### PATCH /admin/reports/{reportId}

Traite un signalement.

### GET /admin/audit-logs

Liste les actions administratives.

### GET /admin/statistics

Statistiques globales de la plateforme.

## 20. Genres administration

### POST /admin/genres

### PATCH /admin/genres/{genreId}

### DELETE /admin/genres/{genreId}

## 21. Health checks

### GET /health

État général.

### GET /health/ready

Vérifie les dépendances nécessaires :

-   PostgreSQL ;
-   Redis ;
-   stockage.

### GET /health/live

Vérifie uniquement que l'application fonctionne.

## 22. Format de pagination

Réponse recommandée :

``` json
{
  "items": [],
  "page": 1,
  "pageSize": 20,
  "totalItems": 150,
  "totalPages": 8
}
```

## 23. Format d'erreur

Exemple :

``` json
{
  "type": "https://example.com/errors/track-not-found",
  "title": "Track not found",
  "status": 404,
  "code": "TRACK_NOT_FOUND",
  "detail": "The requested track does not exist.",
  "traceId": "..."
}
```

Codes métier recommandés :

``` text
AUTH_INVALID_CREDENTIALS
AUTH_UNAUTHORIZED
FORBIDDEN
USER_NOT_FOUND
TRACK_NOT_FOUND
TRACK_NOT_READY
TRACK_ACCESS_DENIED
TRACK_UPLOAD_INVALID
TRACK_UPLOAD_TOO_LARGE
PLAYLIST_NOT_FOUND
PLAYLIST_ACCESS_DENIED
COMMENT_NOT_FOUND
REPORT_NOT_FOUND
INVALID_VISIBILITY
VALIDATION_ERROR
RATE_LIMIT_EXCEEDED
```

## 24. Principes REST

-   utiliser les verbes HTTP correctement ;
-   retourner les bons codes HTTP ;
-   ne jamais exposer les chemins physiques de stockage ;
-   utiliser DTOs ;
-   ne jamais retourner directement les entités EF Core ;
-   paginer les collections ;
-   filtrer et trier explicitement ;
-   documenter les endpoints avec OpenAPI ;
-   versionner l'API ;
-   protéger les endpoints privés ;
-   journaliser les erreurs avec un trace ID.

## 25. Codes HTTP principaux

``` text
200 OK
201 Created
202 Accepted
204 No Content
400 Bad Request
401 Unauthorized
403 Forbidden
404 Not Found
409 Conflict
413 Payload Too Large
415 Unsupported Media Type
422 Unprocessable Entity
429 Too Many Requests
500 Internal Server Error
```

Les opérations asynchrones comme la génération d'un export peuvent
retourner `202 Accepted`.
