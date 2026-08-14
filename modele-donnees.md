# Modèle de données --- Plateforme musicale

## 1. Principes

Base de données : **PostgreSQL**.

Les identifiants utilisent de préférence des UUID.

Les timestamps sont stockés en UTC.

Les suppressions doivent être pensées avec soin pour préserver
l'intégrité et les statistiques nécessaires.

## 2. Vue conceptuelle

``` text
User
 ├── Track
 │    ├── TrackFile
 │    ├── TrackCover
 │    ├── TrackTag
 │    ├── Like
 │    ├── Comment
 │    ├── PlayEvent
 │    └── PlaylistItem
 │
 ├── Playlist
 │    └── PlaylistItem
 │
 ├── Follow
 └── ListeningHistory

Album
Genre
Tag

Report
AuditLog

UserExport
```

## 3. User

``` text
User
----
id UUID PK
email VARCHAR UNIQUE NOT NULL
password_hash VARCHAR NOT NULL
username VARCHAR UNIQUE NOT NULL
bio TEXT NULL
avatar_file_id UUID NULL
profile_visibility ENUM NOT NULL
role ENUM NOT NULL
created_at TIMESTAMP NOT NULL
updated_at TIMESTAMP NOT NULL
deleted_at TIMESTAMP NULL
```

Contraintes :

-   email unique ;
-   username unique ;
-   mot de passe jamais stocké en clair ;
-   compte supprimé logiquement ou physiquement selon la politique
    finale.

## 4. UserSettings

``` text
UserSettings
------------
user_id UUID PK/FK
show_like_count BOOLEAN NOT NULL
show_play_count BOOLEAN NOT NULL
```

Cette table pourra évoluer pour contenir les préférences utilisateur.

## 5. Track

``` text
Track
-----
id UUID PK
owner_id UUID FK User
album_id UUID NULL FK Album
genre_id UUID NULL FK Genre
title VARCHAR NOT NULL
artist_name VARCHAR NOT NULL
duration_seconds INTEGER NOT NULL
visibility ENUM NOT NULL
status ENUM NOT NULL
description TEXT NULL
year INTEGER NULL
like_count BIGINT NOT NULL DEFAULT 0
play_count BIGINT NOT NULL DEFAULT 0
created_at TIMESTAMP NOT NULL
updated_at TIMESTAMP NOT NULL
published_at TIMESTAMP NULL
deleted_at TIMESTAMP NULL
```

Le `play_count` et `like_count` sont des compteurs optimisés pour
lecture. Les événements restent la source détaillée.

## 6. TrackFile

``` text
TrackFile
---------
id UUID PK
track_id UUID FK Track
storage_path VARCHAR NOT NULL
mime_type VARCHAR NOT NULL
file_size BIGINT NOT NULL
checksum VARCHAR NOT NULL
created_at TIMESTAMP NOT NULL
```

Le chemin physique ne doit jamais être envoyé directement au client.

## 7. TrackMetadata

``` text
TrackMetadata
-------------
track_id UUID PK/FK Track
original_filename VARCHAR NULL
embedded_title VARCHAR NULL
embedded_artist VARCHAR NULL
embedded_album VARCHAR NULL
embedded_genre VARCHAR NULL
embedded_year INTEGER NULL
metadata_json JSONB NULL
```

Les données non critiques peuvent être conservées en JSONB pour éviter
de multiplier inutilement les colonnes.

## 8. TrackCover

``` text
TrackCover
----------
id UUID PK
track_id UUID FK Track
size ENUM
storage_path VARCHAR NOT NULL
width INTEGER NOT NULL
height INTEGER NOT NULL
file_size BIGINT NOT NULL
created_at TIMESTAMP NOT NULL
```

Tailles recommandées :

``` text
SMALL
MEDIUM
LARGE
```

## 9. Album

``` text
Album
-----
id UUID PK
name VARCHAR NOT NULL
artist_name VARCHAR NOT NULL
cover_id UUID NULL
created_at TIMESTAMP NOT NULL
updated_at TIMESTAMP NOT NULL
```

Un album peut contenir plusieurs morceaux.

## 10. Genre

``` text
Genre
-----
id UUID PK
name VARCHAR UNIQUE NOT NULL
slug VARCHAR UNIQUE NOT NULL
created_at TIMESTAMP NOT NULL
```

## 11. Tag

``` text
Tag
---
id UUID PK
name VARCHAR UNIQUE NOT NULL
slug VARCHAR UNIQUE NOT NULL
created_at TIMESTAMP NOT NULL
```

Le préfixe `#` est une convention d'affichage/recherche et ne doit pas
nécessairement être stocké.

## 12. TrackTag

``` text
TrackTag
--------
track_id UUID FK Track
tag_id UUID FK Tag
PRIMARY KEY(track_id, tag_id)
```

## 13. Playlist

``` text
Playlist
--------
id UUID PK
owner_id UUID FK User
name VARCHAR NOT NULL
description TEXT NULL
visibility ENUM NOT NULL
cover_file_id UUID NULL
created_at TIMESTAMP NOT NULL
updated_at TIMESTAMP NOT NULL
```

## 14. PlaylistItem

``` text
PlaylistItem
------------
playlist_id UUID FK Playlist
track_id UUID FK Track
position INTEGER NOT NULL
added_at TIMESTAMP NOT NULL
PRIMARY KEY(playlist_id, track_id)
```

La position permet le drag & drop.

Si une playlist doit pouvoir contenir plusieurs fois le même morceau,
utiliser un UUID propre pour l'item au lieu de la clé composite.

## 15. PlaylistFollow

``` text
PlaylistFollow
--------------
playlist_id UUID FK Playlist
user_id UUID FK User
created_at TIMESTAMP NOT NULL
PRIMARY KEY(playlist_id, user_id)
```

## 16. PlaylistFavorite

``` text
PlaylistFavorite
----------------
playlist_id UUID FK Playlist
user_id UUID FK User
created_at TIMESTAMP NOT NULL
PRIMARY KEY(playlist_id, user_id)
```

## 17. TrackLike

``` text
TrackLike
---------
track_id UUID FK Track
user_id UUID FK User
created_at TIMESTAMP NOT NULL
PRIMARY KEY(track_id, user_id)
```

## 18. Follow

``` text
Follow
------
follower_id UUID FK User
followed_id UUID FK User
created_at TIMESTAMP NOT NULL
PRIMARY KEY(follower_id, followed_id)
```

Contrainte :

``` text
follower_id != followed_id
```

## 19. Comment

``` text
Comment
-------
id UUID PK
track_id UUID FK Track
author_id UUID FK User
content TEXT NOT NULL
timestamp_seconds INTEGER NULL
created_at TIMESTAMP NOT NULL
updated_at TIMESTAMP NOT NULL
deleted_at TIMESTAMP NULL
```

`timestamp_seconds` permet un commentaire positionné dans le morceau.

## 20. PlayEvent

``` text
PlayEvent
---------
id UUID PK
track_id UUID FK Track
user_id UUID NULL FK User
session_id UUID NULL
played_at TIMESTAMP NOT NULL
duration_seconds INTEGER NOT NULL
source VARCHAR NULL
```

Une lecture validée est créée lorsque la lecture atteint 10 secondes.

## 21. ListeningHistory

``` text
ListeningHistory
----------------
id UUID PK
user_id UUID FK User
track_id UUID FK Track
last_position_seconds INTEGER NOT NULL
last_played_at TIMESTAMP NOT NULL
```

Une contrainte unique `(user_id, track_id)` peut être utilisée pour
conserver la dernière position par morceau.

## 22. Report

``` text
Report
------
id UUID PK
reporter_id UUID FK User
target_type ENUM NOT NULL
target_id UUID NOT NULL
reason ENUM NOT NULL
description TEXT NULL
status ENUM NOT NULL
reviewed_by UUID NULL FK User
reviewed_at TIMESTAMP NULL
created_at TIMESTAMP NOT NULL
```

Motifs :

``` text
COPYRIGHT
OFFENSIVE
SPAM
OTHER
```

## 23. AuditLog

``` text
AuditLog
--------
id UUID PK
actor_id UUID NULL FK User
action VARCHAR NOT NULL
target_type VARCHAR NULL
target_id UUID NULL
metadata JSONB NULL
created_at TIMESTAMP NOT NULL
```

Ne pas stocker de données secrètes.

## 24. UserExport

``` text
UserExport
----------
id UUID PK
user_id UUID FK User
status ENUM NOT NULL
storage_path VARCHAR NULL
expires_at TIMESTAMP NULL
created_at TIMESTAMP NOT NULL
completed_at TIMESTAMP NULL
```

États :

``` text
PENDING
PROCESSING
READY
FAILED
EXPIRED
```

## 25. UploadOperation

``` text
UploadOperation
---------------
id UUID PK
user_id UUID FK User
track_id UUID NULL FK Track
status ENUM NOT NULL
original_filename VARCHAR NOT NULL
mime_type VARCHAR NOT NULL
file_size BIGINT NOT NULL
temporary_path VARCHAR NULL
created_at TIMESTAMP NOT NULL
updated_at TIMESTAMP NOT NULL
completed_at TIMESTAMP NULL
```

États :

``` text
UPLOADING
PROCESSING
READY
FAILED
CANCELLED
```

## 26. Relations principales

``` text
User 1 ---- N Track
User 1 ---- N Playlist
User N ---- N User        (Follow)
User N ---- N Track       (Like)
User N ---- N Playlist    (Favorite/Follow)

Track 1 ---- N Comment
Track 1 ---- N PlayEvent
Track 1 ---- N TrackFile
Track 1 ---- N TrackCover

Track N ---- N Tag
Playlist N ---- N Track

Album 1 ---- N Track
Genre 1 ---- N Track
```

## 27. Indexes importants

Prévoir notamment :

``` text
User(email)
User(username)

Track(owner_id)
Track(visibility, published_at)
Track(genre_id)
Track(created_at)
Track(play_count)

TrackLike(track_id)
TrackLike(user_id)

Comment(track_id, created_at)

PlayEvent(track_id, played_at)
PlayEvent(user_id, played_at)

Playlist(owner_id)
Playlist(visibility, created_at)

PlaylistItem(playlist_id, position)

Follow(follower_id)
Follow(followed_id)

Tag(slug)
Genre(slug)
```

Des indexes supplémentaires seront ajoutés après observation des
requêtes réelles.

## 28. Transactions importantes

Transaction requise notamment pour :

-   création d'un morceau et de ses métadonnées ;
-   ajout d'un morceau à une playlist ;
-   déplacement de morceaux dans une playlist ;
-   like/unlike et mise à jour du compteur ;
-   suppression d'un compte ;
-   traitement d'un signalement ;
-   création d'un export.

Les fichiers physiques et la base de données ne partagent pas de
transaction native. Le système doit donc utiliser des opérations
compensatoires et un nettoyage des fichiers orphelins.
