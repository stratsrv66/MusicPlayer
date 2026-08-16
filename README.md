# MusicPlatform

Plateforme musicale inspirée de SoundCloud : les artistes y publient leurs morceaux,
les auditeurs écoutent, commentent, constituent des playlists et suivent leurs artistes.

Le projet est constitué d'une **API REST ASP.NET Core**, d'un **site React/TypeScript**,
d'une base **PostgreSQL**, d'un cache **Redis** et d'un stockage de fichiers local,
le tout démarrable par une seule commande Docker.

---

## Sommaire

- [Fonctionnalités](#fonctionnalités)
- [Architecture](#architecture)
- [Prérequis](#prérequis)
- [Démarrage rapide (Docker)](#démarrage-rapide-docker)
- [Développement local](#développement-local)
- [Variables d'environnement](#variables-denvironnement)
- [Import depuis un lien YouTube](#import-depuis-un-lien-youtube)
- [Import de playlists](#import-de-playlists)
- [Base de données et migrations](#base-de-données-et-migrations)
- [Tests](#tests)
- [API](#api)
- [Sécurité](#sécurité)
- [Observabilité](#observabilité)
- [Déploiement sur VPS](#déploiement-sur-vps)
- [Décisions techniques](#décisions-techniques)

---

## Fonctionnalités

**Comptes et profils** — inscription par email, connexion par jeton, rotation des refresh
tokens, profil public ou privé, avatar, biographie, liens sociaux, export des données
personnelles, suppression de compte avec anonymisation.

**Morceaux** — import de fichiers audio (20 Mo maximum) ou depuis un lien YouTube,
extraction des métadonnées et de la pochette embarquée, génération de trois tailles de
pochette, visibilité publique, non répertoriée ou privée, remplacement du fichier,
publication et dépublication.

**Import de playlists YouTube** — aperçu du contenu avant import, rapprochement des
morceaux déjà présents pour éviter les doublons, téléchargement en file avec parallélisme
configurable, progression en temps réel, annulation, reprise après interruption et relance
des morceaux en échec.

**Écoute** — streaming HTTP avec prise en charge des requêtes `Range`, lecteur persistant
entre les pages, file d'attente, lecture aléatoire, répétition, mini-lecteur, lecteur plein
écran, reprise à la dernière position, intégration à la Media Session du système.

**Social** — likes, commentaires positionnés dans le morceau, abonnements, playlists
publiques ou privées avec réordonnancement par glisser-déposer, duplication, partage et
mise en favori.

**Découverte** — recherche multi-types avec filtres et tags `#`, page d'accueil
personnalisée, recommandations déterministes et explicables.

**Artistes** — tableau de bord : écoutes, likes, abonnés, évolution temporelle, morceaux
les plus écoutés, présence en playlist.

**Modération** — signalement de morceaux, commentaires, profils et playlists ; console
d'administration (utilisateurs, rôles, contenus, genres, statistiques) et journal d'audit.

---

## Architecture

### Backend — monolithe modulaire

```
src/
  MusicPlatform.Domain/          Entités, énumérations, règles métier. Aucune dépendance technique.
  MusicPlatform.Application/     Cas d'utilisation, contrats (DTO), abstractions (stockage, cache, jetons).
  MusicPlatform.Infrastructure/  EF Core, PostgreSQL, Redis, stockage local, JWT, médias, tâches de fond.
  MusicPlatform.Api/             Contrôleurs, authentification, limitation de débit, OpenAPI, santé.

tests/
  MusicPlatform.UnitTests/       Règles métier et validation, sans infrastructure.
  MusicPlatform.IntegrationTests/ API complète sur PostgreSQL éphémère (Testcontainers).

web/                             Site React 19 + TypeScript + Vite.
```

Les dépendances vont toujours vers le cœur métier :
`Api → Infrastructure → Application → Domain`.

Le `Domain` ne référence ni ASP.NET Core, ni Entity Framework, ni PostgreSQL, ni Redis.

### Frontend

```
web/src/
  app/         Coquille applicative et routage.
  components/  Composants réutilisables (listes, cartes, boutons, dialogues).
  features/    auth, player, ... — état et logique par domaine fonctionnel.
  pages/       Écrans rattachés aux routes.
  services/    Client HTTP et contrats d'API.
  hooks/       Hooks transverses.
  types/       Types partagés avec l'API.
```

L'état est volontairement séparé : **état serveur** (TanStack Query), **état
d'authentification** et **état du lecteur** (Zustand), **état d'interface** (local aux
composants). Le lecteur audio est monté une seule fois à la racine : la lecture n'est
jamais interrompue par une navigation.

---

## Prérequis

| Outil | Version | Nécessaire pour |
|---|---|---|
| Docker + Docker Compose | 24+ | Lancer la pile complète |
| SDK .NET | 10.0 | Développement backend |
| Node.js | 24+ | Développement frontend |
| yt-dlp + ffmpeg | à jour | Import d'un morceau depuis un lien YouTube |

Docker seul suffit pour faire tourner le projet : l'image de l'API embarque déjà yt-dlp
et ffmpeg. En développement local hors Docker, ces deux outils doivent être installés
pour que l'import par lien fonctionne — voir « Import depuis un lien YouTube ».

---

## Démarrage rapide (Docker)

```bash
git clone <url-du-depot>
cd MusicPlatform

cp .env.example .env
# Renseignez au minimum POSTGRES_PASSWORD et JWT_SECRET.
# Génération de valeurs robustes :
#   openssl rand -base64 24   # mot de passe
#   openssl rand -base64 48   # clé de signature (32 caractères minimum)

docker compose up -d --build
```

Au démarrage, l'API applique les migrations et insère les genres de référence.

| Service | URL |
|---|---|
| Site web | http://localhost:8080 |
| API | http://localhost:5080/api/v1 |
| Swagger | http://localhost:8080/swagger *(si `SWAGGER_ENABLED=true`)* |
| Santé | http://localhost:5080/health/ready |

### Compte administrateur

Aucun compte n'est codé en dur. Pour en créer un au premier démarrage, renseignez
`SEED_ADMIN_EMAIL`, `SEED_ADMIN_USERNAME` et `SEED_ADMIN_PASSWORD` dans `.env`.
Si ces variables sont vides, aucun administrateur n'est créé ; un compte existant peut
alors être promu directement en base :

```bash
docker compose exec postgres psql -U musicplatform -d musicplatform \
  -c "UPDATE users SET role = 'Admin' WHERE username = 'mon-pseudo';"
```

### Commandes utiles

```bash
docker compose logs -f api      # Journaux de l'API
docker compose ps               # État et santé des services
docker compose down             # Arrêt (les volumes sont conservés)
docker compose down -v          # Arrêt et suppression des données
```

---

## Développement local

Les services d'infrastructure tournent dans Docker, l'API et le site en local.

```bash
# 1. PostgreSQL et Redis
docker compose up -d postgres redis

# 2. API — http://localhost:5080
cd src/MusicPlatform.Api
dotnet run
# Swagger : http://localhost:5080/swagger

# 3. Site — http://localhost:5173
cd web
npm install
npm run dev
```

Le serveur de développement Vite relaie `/api` vers `http://localhost:5080` : le
navigateur ne voit qu'une seule origine, aucune configuration CORS n'est nécessaire.

`appsettings.Development.json` fournit des valeurs de développement prêtes à l'emploi.
Elles ne conviennent qu'au poste local et ne doivent jamais servir en production.

---

## Variables d'environnement

Toutes les valeurs sensibles proviennent de l'environnement ; aucune n'est écrite dans le
code. `.env.example` liste l'ensemble des clés. Les plus importantes :

| Variable | Obligatoire | Description |
|---|---|---|
| `POSTGRES_PASSWORD` | oui | Mot de passe PostgreSQL |
| `JWT_SECRET` | oui | Clé de signature des jetons, 32 caractères minimum |
| `WEB_ORIGIN` | recommandé | Origine autorisée par CORS (URL publique du site) |
| `JWT_ACCESS_TOKEN_LIFETIME` | non | Durée de l'access token, en secondes (900 par défaut) |
| `JWT_REFRESH_TOKEN_LIFETIME_DAYS` | non | Durée du refresh token, en jours (30 par défaut) |
| `SWAGGER_ENABLED` | non | Expose `/swagger`. À laisser à `false` en production |
| `OTLP_ENDPOINT` | non | Collecteur OpenTelemetry. Vide = export désactivé |
| `SEED_ADMIN_*` | non | Compte administrateur initial |
| `YTDLP_AUDIO_QUALITY` | non | Débit de l'encodage MP3 à l'import (`128K` par défaut) |
| `YTDLP_MAX_DURATION_SECONDS` | non | Durée maximale d'une vidéo importable (900 par défaut) |
| `YTDLP_TIMEOUT_SECONDS` | non | Délai maximal d'un téléchargement (300 par défaut) |

L'API refuse de démarrer si `Jwt:Secret` est absent ou trop court : une mauvaise
configuration est détectée immédiatement plutôt qu'à la première connexion.

Les quotas de limitation de débit sont ajustables sans recompilation, par exemple
`RateLimiting__auth__PermitLimit=20`.

---

## Import depuis un lien YouTube

Depuis la page « Importer un morceau », choisir la source « Lien YouTube » suffit : le
serveur télécharge la piste audio au format MP3 et utilise la miniature de la vidéo comme
pochette. Titre, artiste et année sont repris de la vidéo lorsque les champs sont laissés
vides. Le morceau rejoint ensuite le pipeline habituel — analyse des métadonnées,
génération des trois tailles de pochette, puis publication.

Le téléchargement s'appuie sur le paquet Python [`yt-dlp`](https://github.com/yt-dlp/yt-dlp),
appelé comme processus enfant, et sur `ffmpeg` pour l'extraction audio. **L'image Docker de
l'API les installe elle-même** : `docker compose up -d --build` suffit, rien n'est à
installer sur la machine hôte en dehors de Docker.

Pour un développement local hors Docker, en revanche :

```bash
python -m pip install --upgrade yt-dlp
# ffmpeg : apt install ffmpeg | brew install ffmpeg | winget install Gyan.FFmpeg
```

Si `yt-dlp` est absent, seul l'import par lien est indisponible : l'endpoint répond `503`
et le reste de l'application fonctionne normalement.

**Maintenir yt-dlp à jour.** YouTube modifie régulièrement son lecteur, ce qui casse les
versions anciennes de yt-dlp. La version installée est celle du jour de la construction de
l'image : si les imports commencent à échouer en `422`, reconstruire l'image la met à jour.

```bash
docker compose build --no-cache api && docker compose up -d api
```

Réglages (section `YtDlp`, surchargeable par variables d'environnement) :

| Clé | Défaut | Rôle |
|---|---|---|
| `YtDlp__ExecutablePath` | `yt-dlp` | Commande à exécuter |
| `YtDlp__PythonPath` | vide | Interpréteur à utiliser si yt-dlp n'est installé que comme module (`python -m yt_dlp`) |
| `YtDlp__AudioQuality` | `128K` | Débit de l'encodage MP3 |
| `YtDlp__MaxDurationSeconds` | `900` | Durée maximale d'une vidéo. À `128K`, 15 minutes tiennent sous la limite de 20 Mo |
| `YtDlp__TimeoutSeconds` | `300` | Délai au-delà duquel le téléchargement est abandonné |
| `YtDlp__WorkingDirectory` | dossier temporaire | Emplacement des téléchargements en cours |
| `YtDlp__CookiesFile` | vide | Cookies au format Netscape, pour les vidéos à accès restreint |

Deux garde-fous encadrent la fonctionnalité : seules les URL des domaines YouTube sont
acceptées — l'outil de téléchargement ne peut donc pas être dirigé vers une adresse
arbitraire, notamment interne au réseau du serveur — et l'URL est passée en argument
d'un processus, sans shell, ce qui exclut toute injection de commande.

Il revient enfin à l'utilisateur de s'assurer qu'il dispose des droits nécessaires sur le
contenu qu'il importe.

---

## Import de playlists

Depuis « Importer une playlist », on colle le lien d'une playlist YouTube publique — ou on
parcourt les playlists publiques d'une chaîne — puis on vérifie le contenu avant de lancer
l'opération. La progression s'affiche ensuite morceau par morceau.

### Un import de playlist n'est qu'une suite d'imports unitaires

Les morceaux sont traités **un par un**, et chacun passe par la fonction qui importe un
lien YouTube isolé : `TrackImportService.ImportForOwnerAsync`. Même appel yt-dlp, mêmes
métadonnées lues sur la vidéo, même pochette issue de la miniature. Un morceau importé via
une playlist est donc indiscernable d'un morceau importé seul.

Pour chaque morceau, dans l'ordre :

```
Rapprochement → Import yt-dlp → Traitement du fichier → Ajout à la playlist
```

Le morceau n'est ajouté à la playlist qu'une fois **réellement écoutable**. Le traitement
du fichier est déclenché immédiatement plutôt que confié à la file de travaux : celle-ci
est consommée séquentiellement, et l'import occupant son unique consommateur, les morceaux
seraient restés en attente jusqu'à la fin de l'import complet.

| Étape | Contrat | Implémentation |
|---|---|---|
| Énumération | `IPlaylistProvider` | `YoutubePlaylistProvider` (`yt-dlp --flat-playlist`) |
| Normalisation | `MetadataNormalizer` | retire l'habillage éditorial, les accents et la ponctuation |
| Rapprochement | `TrackMatcher` | identifiant de vidéo → clé artiste+titre → durée |
| Import | `TrackImportService` | chemin d'import YouTube unitaire, partagé |
| Traitement | `TrackProcessingService` | durée réelle, pochette, promotion du fichier |

L'énumération utilise `--flat-playlist` : elle liste le contenu sans transférer le moindre
média, ce qui rend l'aperçu immédiat.

### Visibilité des morceaux importés

Le lecteur du navigateur diffuse le son via un élément `<audio>`, qui **ne transmet aucun
en-tête d'authentification**. La requête de streaming arrive donc anonyme, et un morceau
`PRIVATE` lui est refusé : un morceau privé n'est pas écoutable depuis l'interface.

L'import propose par conséquent **« non répertorié »** par défaut — écoutable par son
propriétaire, absent des recherches et de la page d'accueil. Le choix `PRIVATE` reste
possible et signalé comme tel dans le formulaire.

Rendre les morceaux privés diffusables demanderait des URL de streaming signées, à courte
durée de vie, que le lecteur pourrait présenter sans en-tête.

### Éviter les doublons

Avant tout téléchargement, chaque morceau est comparé à la bibliothèque de l'utilisateur :
d'abord par identifiant de vidéo YouTube, puis par clé « artiste|titre » normalisée
départagée par la durée à cinq secondes près. Un morceau reconnu est rattaché sans être
retéléchargé et son état passe à « Déjà présent ».

L'identifiant de la vidéo est conservé sur le morceau (`track_external_ids`), y compris
pour un import unitaire : réimporter la même playlist, ou une playlist qui recoupe la
précédente, ne retélécharge rien.

### Robustesse

L'inventaire des morceaux est écrit en base au lancement : l'import survit donc à un
redémarrage du serveur, `StalledJobRecoveryService` le remettant en file au démarrage. Les
morceaux avancent par lots dont la taille est celle du parallélisme configuré, chaque état
étant persisté à la fin du lot. Un arrêt brutal ne fait perdre qu'un lot, une annulation
est prise en compte entre deux lots, et les morceaux en échec se relancent depuis
l'interface sans retraiter ceux déjà importés.

Une playlist est limitée à 500 morceaux.

---

## Base de données et migrations

Les migrations sont appliquées automatiquement au démarrage de l'API. Pour les piloter
manuellement :

```bash
dotnet tool install --global dotnet-ef

export ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=musicplatform;Username=musicplatform;Password=..."

# Appliquer
dotnet ef database update -p src/MusicPlatform.Infrastructure -s src/MusicPlatform.Infrastructure

# Créer une migration après modification du modèle
dotnet ef migrations add NomDeLaMigration \
  -p src/MusicPlatform.Infrastructure -s src/MusicPlatform.Infrastructure \
  -o Persistence/Migrations

# Vérifier qu'aucune modification du modèle n'est en attente (exécuté aussi en CI)
dotnet ef migrations has-pending-model-changes \
  -p src/MusicPlatform.Infrastructure -s src/MusicPlatform.Infrastructure
```

Le schéma compte 25 tables. Les identifiants sont des UUID, les dates sont stockées en UTC
et les noms suivent la convention `snake_case`. Les règles structurantes sont portées par
la base : unicité de l'email et du pseudo, clé composite empêchant le double like,
contrainte interdisant de s'abonner à soi-même, positions de playlist non négatives,
suppressions en cascade.

---

## Tests

```bash
# Backend
dotnet test                                   # tout
dotnet test tests/MusicPlatform.UnitTests     # règles métier, rapide
dotnet test tests/MusicPlatform.IntegrationTests  # API réelle, nécessite Docker

# Frontend
cd web
npm test
npm run test:coverage
```

| Suite | Nombre | Portée |
|---|---|---|
| Unitaires (backend) | 92 | Visibilité des contenus, permissions, réordonnancement, validation des fichiers, normalisation des tags, pagination, agrégation statistique |
| Intégration (backend) | 43 | Parcours complets sur PostgreSQL réel : authentification, upload et traitement, streaming `Range`, likes, écoutes, commentaires, playlists, recherche, modération, export, suppression de compte, limitation de débit |
| Frontend | 39 | File d'attente et enchaînement du lecteur, lecture aléatoire, répétition, composants, formatage |

Les tests d'intégration démarrent un PostgreSQL éphémère via **Testcontainers** : ils
s'exécutent contre le vrai moteur, donc les migrations, les contraintes et le SQL généré
sont réellement éprouvés. Redis n'est volontairement pas démarré, ce qui vérifie en
continu que l'application fonctionne sans cache.

---

## API

Base : `/api/v1`. Authentification : `Authorization: Bearer <access_token>`.
La documentation interactive complète est exposée par Swagger.

| Domaine | Endpoints |
|---|---|
| Authentification | `POST /auth/register`, `/auth/login`, `/auth/refresh`, `/auth/logout` |
| Compte | `GET|PATCH /me`, `/me/settings`, `/me/avatar`, `/me/tracks`, `/me/playlists`, `/me/likes`, `/me/history`, `/me/followers`, `/me/following`, `DELETE /me` |
| Export | `POST /me/data-export`, `GET /me/data-exports`, `/{id}`, `/{id}/download` |
| Statistiques | `GET /me/analytics/overview`, `/tracks`, `/plays`, `/top-tracks` |
| Profils | `GET /users/{username}`, `/tracks`, `/playlists`, `POST|DELETE /users/{id}/follow`, `GET /users/{id}/followers`, `/following` |
| Morceaux | `GET /tracks`, `/tracks/{id}`, `POST /tracks`, `/tracks/{id}/upload`, `GET /tracks/{id}/stream`, `PATCH|DELETE /tracks/{id}`, `POST /tracks/{id}/publish`, `/unpublish` |
| Pochettes | `POST|DELETE /tracks/{id}/cover`, `GET /tracks/{id}/cover/{size}` |
| Likes | `POST|DELETE|GET /tracks/{id}/like` |
| Écoute | `POST /tracks/{id}/plays`, `PUT|GET /tracks/{id}/progress` |
| Commentaires | `GET|POST /tracks/{id}/comments`, `PATCH|DELETE /comments/{id}` |
| Playlists | `GET|POST /playlists`, `GET|PATCH|DELETE /playlists/{id}`, `/cover`, `/tracks`, `/tracks/reorder`, `/duplicate`, `/follow`, `/favorite` |
| Découverte | `GET /search`, `/home`, `/recommendations/tracks`, `/recommendations/artists`, `/genres`, `/tags`, `/tags/{tag}/tracks` |
| Signalements | `POST /reports`, `GET /me/reports` |
| Administration | `GET|PATCH|DELETE /admin/users`, `/admin/tracks`, `/admin/reports`, `/admin/audit-logs`, `/admin/statistics`, `/admin/genres` |
| Santé | `GET /health`, `/health/live`, `/health/ready` |

### Format des erreurs

Toutes les erreurs suivent Problem Details, enrichi d'un code métier et d'un identifiant
de trace :

```json
{
  "type": "https://musicplatform.dev/errors/track-not-found",
  "title": "Not found",
  "status": 404,
  "code": "TRACK_NOT_FOUND",
  "detail": "The requested track does not exist.",
  "traceId": "00-4bf92f...-01"
}
```

### Pagination

```json
{ "items": [], "page": 1, "pageSize": 20, "totalItems": 150, "totalPages": 8 }
```

La taille de page est bornée à 100 côté serveur : aucune requête client ne peut déclencher
un chargement non maîtrisé.

### Streaming

`GET /tracks/{id}/stream` gère `Range` : `200` pour une requête complète, `206` avec
`Content-Range` pour un fragment, `416` pour une plage invalide. Le fichier n'est jamais
chargé en mémoire — il est diffusé depuis un flux positionnable.

---

## Sécurité

- **Mots de passe** : PBKDF2-HMAC-SHA512 (implémentation ASP.NET Core Identity), sel
  aléatoire, réhachage automatique si le facteur de coût évolue. Aucun mot de passe n'est
  stocké ni journalisé en clair.
- **Jetons** : access token JWT à durée courte, refresh token opaque dont seul le **hash
  SHA-256** est conservé. Chaque renouvellement fait tourner le jeton et révoque le
  précédent ; une réutilisation est donc rejetée.
- **Autorisation à deux niveaux** : policies sur les endpoints d'administration *et*
  vérification explicite des droits dans la couche applicative, au plus près de la règle
  métier. Un utilisateur ne peut ni modifier le contenu d'un autre, ni consulter ses
  statistiques, ni accéder à ses fichiers privés.
- **Ressources privées** : un contenu invisible pour l'appelant renvoie `404`, jamais
  `403` — répondre « interdit » révélerait son existence.
- **Uploads** : extension, taille (20 Mo) et **signature binaire** vérifiées. Un exécutable
  renommé en `.mp3` est rejeté. Un échec supprime le fichier temporaire et laisse la base
  cohérente.
- **Stockage** : chemins logiques uniquement. La résolution vérifie que le chemin final
  reste sous la racine configurée, ce qui bloque toute traversée de répertoire. Aucun
  chemin physique n'est exposé.
- **Injections** : requêtes exclusivement paramétrées via EF Core ; les jokers `%` et `_`
  saisis par l'utilisateur sont échappés avec un caractère d'échappement explicite.
- **Limitation de débit** : connexion, inscription, upload, recherche, écriture sociale et
  administration, partitionnées par utilisateur ou par adresse IP. L'implémentation est
  celle d'ASP.NET Core, en mémoire : elle reste correcte même si Redis est indisponible.
- **Secrets** : aucun secret dans le dépôt. L'application refuse de démarrer sans clé de
  signature valide.
- **Erreurs** : aucune trace d'exécution hors développement.
- **Suspension** : suspendre un compte révoque immédiatement ses sessions actives.

---

## Observabilité

- **Journaux structurés** (Serilog) avec identifiant de corrélation sur chaque requête.
- **Sondes de santé** :
  - `/health/live` — l'application répond, sans interroger ses dépendances ;
  - `/health/ready` — PostgreSQL, Redis et le stockage sont vérifiés ;
  - `/health` — état général.
  Redis indisponible dégrade l'état sans rendre l'instance indisponible : le cache n'est
  jamais la source de vérité.
- **Traces et métriques** OpenTelemetry, exportées en OTLP dès que `OTLP_ENDPOINT` est
  renseigné.

---

## Déploiement sur VPS

1. **Préparer la machine** — installer Docker et Docker Compose ; n'ouvrir que les ports
   `80` et `443`. PostgreSQL et Redis ne sont publiés que sur `127.0.0.1` par la
   configuration Compose fournie : ils ne sont jamais joignables depuis l'extérieur.

2. **Configurer** — copier `.env.example` vers `.env`, générer les secrets, définir
   `WEB_ORIGIN` sur l'URL publique et laisser `SWAGGER_ENABLED=false`.

3. **Démarrer** — `docker compose up -d --build`.

4. **Terminer TLS** — placer un reverse proxy (Caddy, Traefik ou nginx + Certbot) devant le
   service `web`. HTTPS est obligatoire : les jetons transitent dans les en-têtes. L'API
   lit `X-Forwarded-For` et `X-Forwarded-Proto`, ce dont dépend le partitionnement de la
   limitation de débit.

5. **Sauvegarder** — la base et les fichiers, tous deux nécessaires à une restauration :

   ```bash
   docker compose exec -T postgres pg_dump -U musicplatform musicplatform | gzip > db-$(date +%F).sql.gz
   docker run --rm -v musicplatform_storage-data:/data -v "$PWD":/backup alpine \
     tar czf /backup/storage-$(date +%F).tar.gz -C /data .
   ```

6. **Mettre à jour** — `git pull && docker compose up -d --build`. Les migrations
   s'appliquent au démarrage.

---

## Décisions techniques

Les points sur lesquels plusieurs options étaient défendables, et le motif du choix.

**Traitements différés en processus plutôt qu'un conteneur worker.** L'analyse audio, la
génération des pochettes et les exports passent par une file en mémoire consommée par un
service hébergé, derrière l'abstraction `IBackgroundJobQueue`. Cela évite d'introduire un
courtier de messages pour un volume qui ne le justifie pas. Un travail perdu lors d'un
arrêt brutal est repris au redémarrage par un service de reprise ; le remplacement par une
file distribuée ne toucherait aucun cas d'utilisation.

**Services applicatifs plutôt que classes de commande.** Chaque cas d'utilisation est une
méthode publique d'un service dédié, testable isolément. Une classe et deux enregistrements
par cas d'utilisation auraient multiplié les fichiers sans bénéfice à cette échelle.

**`IAppDbContext` dans la couche Application.** Les cas d'utilisation composent directement
leurs requêtes derrière une interface, plutôt que d'empiler un dépôt par entité. Cela évite
une couche d'indirection qui, en pratique, se contente de réexposer les mêmes requêtes.

**Suppression de compte par anonymisation.** Les données personnelles, contenus et fichiers
sont réellement supprimés, mais la ligne utilisateur est conservée sous une identité neutre
afin de ne pas briser les événements d'écoute ni le journal d'audit. Les statistiques
agrégées de la plateforme restent exactes sans conserver de donnée personnelle.

**Recommandations par score explicite.** Artiste suivi, genre écouté, tags partagés,
popularité récente et fraîcheur sont additionnés en un score documenté dans le code.
Le résultat est reproductible et se justifie devant un utilisateur, ce qu'un modèle
statistique n'offrirait pas ici.

**Globalisation ICU activée.** `InvariantGlobalization` est désactivé volontairement : sans
ICU, la normalisation Unicode ne fonctionne pas et « Électro » produirait un slug erroné.
Les images Docker embarquent donc ICU.

**Compteurs dénormalisés.** `like_count` et `play_count` sont maintenus sur le morceau et
mis à jour par instruction SQL atomique, à l'intérieur d'une transaction avec l'insertion
correspondante. Les événements détaillés restent la source de vérité pour les statistiques.
