# Architecture --- Plateforme musicale

## 1. Objectif

L'architecture doit être production-ready sans devenir inutilement
complexe.

Le principe directeur est :

> commencer avec un monolithe modulaire bien structuré, capable
> d'évoluer vers des composants séparés si la charge ou les besoins le
> justifient.

Aucun microservice n'est nécessaire au MVP.

## 2. Vue globale

``` text
                         Internet
                            |
                    Reverse Proxy
                            |
             +--------------+--------------+
             |                             |
        Web React                     API ASP.NET Core
             |                             |
             +---------------+-------------+
                             |
                    Application Layer
                             |
       +---------------------+---------------------+
       |                     |                     |
   PostgreSQL              Redis             File Storage
       |                                           |
       |                                    Audio / Covers
       |
       +-----------------------------+
                                     |
                              Background Worker
```

L'application mobile React Native consomme la même API.

## 3. Composants

### Frontend Web

Technologie : React.

Responsabilités :

-   interface utilisateur ;
-   authentification ;
-   navigation ;
-   recherche ;
-   player ;
-   upload ;
-   gestion des playlists ;
-   dashboard ;
-   administration.

Architecture frontend recommandée :

``` text
src/
  app/
  components/
  features/
    auth/
    tracks/
    playlists/
    player/
    search/
    profile/
    analytics/
    admin/
  services/
  hooks/
  types/
```

### Application mobile

Technologie : React Native.

Responsabilités :

-   expérience Android ;
-   player natif ;
-   lecture en arrière-plan ;
-   contrôles média système ;
-   authentification ;
-   navigation ;
-   upload ;
-   playlists ;
-   recherche ;
-   profils.

L'application mobile ne doit pas dupliquer la logique métier du backend.

## 4. Backend

Technologie : ASP.NET Core Web API.

Architecture recommandée :

``` text
API
 |
Application
 |
Domain
 |
Infrastructure
```

### API

Contient :

-   Controllers ;
-   DTOs ;
-   validation des requêtes ;
-   mapping ;
-   gestion des erreurs ;
-   authentification HTTP ;
-   documentation OpenAPI.

### Application

Contient les cas d'utilisation :

``` text
CreateTrack
UpdateTrack
DeleteTrack
UploadTrack
CreatePlaylist
AddTrackToPlaylist
LikeTrack
CommentTrack
FollowUser
Search
GetRecommendations
ExportUserData
DeleteAccount
```

Chaque cas d'utilisation doit rester facilement testable.

### Domain

Contient :

-   entités ;
-   value objects ;
-   enums ;
-   règles métier ;
-   interfaces métier.

Le domaine ne doit pas dépendre d'ASP.NET, PostgreSQL ou Redis.

### Infrastructure

Contient :

-   EF Core ;
-   PostgreSQL ;
-   Redis ;
-   stockage fichiers ;
-   implémentation JWT ;
-   services de traitement des métadonnées ;
-   services de statistiques ;
-   accès externes.

## 5. Style architectural

Le projet utilise une approche proche de la Clean Architecture /
architecture hexagonale.

L'objectif n'est pas d'appliquer des patterns pour eux-mêmes, mais de
garder les responsabilités séparées.

Règle principale :

``` text
Domain
   ↑
Application
   ↑
Infrastructure / API
```

Les dépendances doivent aller vers le cœur métier.

## 6. Base de données

PostgreSQL est la source de vérité.

EF Core est utilisé pour :

-   mapping ;
-   migrations ;
-   requêtes ;
-   transactions.

Les identifiants peuvent être des UUID afin de faciliter l'exposition
des ressources dans une API publique.

## 7. Redis

Redis est utilisé uniquement lorsqu'il apporte une vraie valeur.

Cas prévus :

-   cache des données fréquemment consultées ;
-   sessions/état court si nécessaire ;
-   rate limiting distribué si nécessaire ;
-   cache de résultats de recherche ou recommandations simples ;
-   compteurs temporaires d'écoute.

Redis ne doit jamais devenir la source de vérité des données métier.

## 8. Stockage des fichiers

Le MVP utilise le disque local.

Structure indicative :

``` text
/storage
  /audio
    /{userId}
      /{trackId}
  /covers
    /original
    /small
    /medium
    /large
```

Les chemins physiques ne doivent jamais être exposés directement à
l'utilisateur.

L'application passe par une abstraction :

``` text
IFileStorage
```

Implémentation initiale :

``` text
LocalFileStorage
```

Une implémentation cloud pourra être ajoutée ultérieurement sans
modifier le domaine.

## 9. Traitement des morceaux

Pipeline :

``` text
Upload
  |
Validation
  |
Metadata extraction
  |
Cover extraction
  |
Cover resize
  |
File persistence
  |
Database persistence
  |
Ready
```

Le traitement peut utiliser un worker/background service afin de ne pas
bloquer la requête HTTP pour les opérations coûteuses.

## 10. Streaming

Le backend fournit l'accès aux fichiers audio en supportant les requêtes
HTTP Range.

Objectifs :

-   seek ;
-   reprise ;
-   lecture progressive ;
-   consommation raisonnable de bande passante.

Le fichier ne doit pas être chargé entièrement en mémoire.

## 11. Lecture en arrière-plan

Web :

-   Media Session API ;
-   événements media ;
-   player persistant.

Android :

-   intégration avec les capacités natives de React Native ;
-   service de lecture approprié ;
-   contrôles de lock screen ;
-   Bluetooth/headset events.

## 12. Authentification

Approche recommandée :

``` text
Access Token
+
Refresh Token
```

L'API utilise ASP.NET Core Authentication/Authorization.

Les permissions sont exprimées avec des policies lorsque cela apporte de
la clarté.

Exemples :

``` text
CanManageTrack
CanManagePlaylist
CanViewPrivateProfile
CanModerateContent
CanAccessAdmin
```

## 13. Gestion des erreurs

Toutes les erreurs API utilisent un format uniforme basé sur Problem
Details.

Exemple conceptuel :

``` json
{
  "type": "https://example.com/errors/track-not-found",
  "title": "Track not found",
  "status": 404,
  "code": "TRACK_NOT_FOUND",
  "traceId": "..."
}
```

Aucune stack trace ne doit être exposée en production.

## 14. Validation

Les requêtes entrantes sont validées avant exécution.

La validation couvre :

-   taille ;
-   format ;
-   chaînes ;
-   UUID ;
-   enum ;
-   pagination ;
-   filtres ;
-   droits d'accès.

Les règles métier complexes restent dans Application/Domain.

## 15. Upload et concurrence

Un morceau possède un état de traitement :

``` text
UPLOADING
PROCESSING
READY
FAILED
```

Un identifiant d'opération d'upload permet d'éviter les ambiguïtés.

En cas d'interruption :

-   suppression du fichier partiel ;
-   nouvelle tentative depuis zéro ;
-   aucune référence en base vers un fichier invalide.

En cas de remplacement :

1.  nouvelle opération validée ;
2.  ancien fichier supprimé ;
3.  nouvelle référence enregistrée.

Le système doit éviter les courses entre deux modifications
concurrentes.

## 16. Statistiques

Les événements d'écoute ne doivent pas bloquer la lecture.

Flux :

``` text
Player
  |
10 seconds reached
  |
TrackPlay event
  |
Background processing
  |
Statistics
```

Une première implémentation peut stocker les événements en PostgreSQL.

Redis peut être utilisé pour absorber les compteurs très fréquents.

## 17. Recherche

MVP :

``` text
PostgreSQL
  |
Full-text / trigram search
```

L'accès à la recherche doit être encapsulé derrière une abstraction
permettant de remplacer l'implémentation par OpenSearch/Elasticsearch si
nécessaire.

## 18. Recommandations

Le MVP utilise un moteur déterministe.

Exemple :

``` text
Historique
   +
Likes
   +
Genres
   +
Tags
   +
Popularité récente
   =
Score simple
```

Aucun modèle ML n'est requis.

## 19. Background jobs

Les traitements pouvant être longs ou asynchrones sont exécutés en
arrière-plan :

-   traitement de couverture ;
-   nettoyage de fichiers ;
-   génération d'exports ;
-   agrégation de statistiques ;
-   tâches de maintenance.

Une abstraction de job doit être conservée afin de pouvoir évoluer vers
une solution dédiée si nécessaire.

## 20. Observabilité

Le projet utilise :

-   logs structurés ;
-   correlation/trace ID ;
-   métriques ;
-   health checks ;
-   traces lorsque pertinent.

OpenTelemetry est recommandé comme standard d'instrumentation.

Pour le VPS, une stack légère peut être utilisée au départ.

Exemple :

``` text
ASP.NET
  |
OpenTelemetry
  |
Prometheus-compatible metrics
```

Grafana peut être ajouté pour la visualisation.

## 21. Docker

Services locaux :

``` text
web
api
worker
postgres
redis
```

L'application doit pouvoir être démarrée par :

``` bash
docker compose up
```

Les volumes PostgreSQL et stockage sont persistants.

## 22. Production VPS

Architecture cible :

``` text
Internet
   |
HTTPS
   |
Reverse Proxy
   |
+--+----------------+
|                   |
Web               API
                    |
             +------+------+
             |             |
         PostgreSQL      Redis
             |
         File Storage
```

HTTPS est obligatoire.

Les secrets sont fournis par variables d'environnement ou mécanisme
équivalent.

## 23. CI/CD

GitHub Actions :

``` text
Push / Pull Request
       |
       +--> Build
       +--> Unit Tests
       +--> Integration Tests
       +--> Static checks
       +--> Docker build
       |
       +--> Deploy production
```

Le déploiement production doit être contrôlé et ne pas exposer de
secrets dans les logs.

## 24. Tests

Niveaux :

### Unitaires

Domain et Application.

### Intégration

API + PostgreSQL + Redis lorsque nécessaire.

### End-to-end

Scénarios critiques :

-   inscription ;
-   authentification ;
-   upload ;
-   création playlist ;
-   lecture ;
-   like ;
-   commentaire ;
-   export ;
-   suppression.

Testcontainers est recommandé pour rendre les tests d'intégration
reproductibles.

## 25. Sécurité infrastructure

Le VPS doit limiter les ports exposés.

Typiquement :

``` text
80  -> HTTP
443 -> HTTPS
```

PostgreSQL et Redis ne doivent pas être accessibles publiquement.

Les sauvegardes PostgreSQL doivent être prévues en production.

## 26. Évolution

Si la charge augmente, les premiers composants susceptibles d'être
isolés sont :

-   traitement audio ;
-   statistiques ;
-   recherche ;
-   notifications.

Le projet ne doit pas commencer en microservices.
