# Matrice de vérification

Contrôle de chaque exigence des quatre documents de spécification
(`cahier-des-charges.md`, `architecture.md`, `modele-donnees.md`, `endpoints-rest.md`)
face au code réellement présent dans le dépôt.

Chaque ligne renvoie au code, à l'endpoint et au test qui la couvrent.
Les points non couverts sont signalés explicitement en fin de document.

**Vérifié le :** 14 août 2026
**État des suites :** 92 tests unitaires · 43 tests d'intégration · 39 tests frontend — **174 au total, tous verts**
**Volumétrie :** 100 endpoints HTTP · 25 tables · 78 index · 7 contraintes `CHECK` · 72 fichiers C# · 32 fichiers TypeScript

Légende : **[x]** implémenté et vérifié — **[~]** implémenté avec une réserve explicitée.

---

## 1. Comptes, authentification et sécurité

| # | Exigence | Code | Endpoint | Test |
|---|---|---|---|---|
| [x] | Inscription email + mot de passe | `Features/Auth/AuthService.cs` → `RegisterAsync` | `POST /auth/register` | `AccountTests.RegistrationRejectsADuplicateEmailOrUsername` |
| [x] | Connexion | `AuthService.LoginAsync` | `POST /auth/login` | `AccountTests.LoginWithWrongPasswordIsRejectedWithoutRevealingTheAccount` |
| [x] | Access token (JWT) | `Infrastructure/Security/Security.cs` → `JwtTokenService` | — | `AccountTests.RefreshRotatesTheTokenAndInvalidatesThePreviousOne` |
| [x] | Refresh token avec rotation et révocation | `AuthService.RefreshAsync`, entité `RefreshToken` | `POST /auth/refresh` | idem |
| [x] | Déconnexion | `AuthService.LogoutAsync` | `POST /auth/logout` | `AccountTests.LogoutRevokesTheRefreshToken` |
| [x] | Mot de passe jamais stocké en clair | `IdentityPasswordHasher` (PBKDF2-HMAC-SHA512) | — | `AccountTests.AccountDeletionRemovesPersonalDataContentAndFiles` (hash vidé) |
| [x] | Rôles `USER` / `ARTIST` / `MODERATOR` / `ADMIN` | `Domain/Enums/Enums.cs` → `UserRole` | — | `SocialRulesTests` |
| [x] | Policies d'autorisation | `Api/Infrastructure/AuthorizationPolicies.cs` | `[Authorize(Policy = …)]` | `AdminTests.AdminEndpointsAreClosedToRegularUsers` |
| [x] | Endpoints privés réellement protégés | Vérification explicite dans chaque service | tous | `AccountTests.ProtectedEndpointsRequireAuthentication` |
| [x] | Interdiction de modifier le contenu d'autrui | `TrackService.LoadForManagementAsync`, `PlaylistService.LoadForManagementAsync` | — | `TrackLifecycleTests.AnotherUser_CannotModifyOrDeleteSomeoneElsesTrack` |
| [x] | Interdiction de lire une ressource privée | `Track.IsAccessibleBy`, `Playlist.IsAccessibleBy` | — | `TrackLifecycleTests.PrivateTrack_…`, `PlaylistTests.PrivatePlaylistIsInvisible…` |
| [x] | Interdiction d'accéder aux statistiques d'autrui | `AnalyticsService` (borné à `RequireUserId`) | `/me/analytics/*` | `SearchAndSocialTests.ArtistAnalyticsCoverOwnedTracksOnly` |
| [x] | Interdiction de télécharger un fichier privé | `TrackStreamService.OpenAsync` | `GET /tracks/{id}/stream` | `TrackLifecycleTests.PrivateTrack_…` |
| [x] | Rate limiting des endpoints sensibles | `Api/Infrastructure/RateLimitPolicies.cs` | politiques `auth`, `upload`, `search`, `write`, `admin` | `RateLimitTests.RepeatedLoginAttemptsAreThrottled…` |
| [x] | Gestion sécurisée des secrets | Configuration/environnement ; démarrage refusé sans clé valide | — | — |
| [x] | Journaux sans donnée sensible | Serilog ; aucun mot de passe ni jeton journalisé | — | — |

---

## 2. Profils

| # | Exigence | Code | Endpoint | Test |
|---|---|---|---|---|
| [x] | Consultation du profil courant | `Features/Users/UserService.GetMeAsync` | `GET /me` | `AccountTests.ProtectedEndpointsRequireAuthentication` |
| [x] | Modification (pseudo, bio, liens, visibilité) | `UserService.UpdateProfileAsync` | `PATCH /me` | `SearchAndSocialTests.UsernameChangeIsRejectedWhenAlreadyTaken` |
| [x] | Avatar | `UserService.SetAvatarAsync` / `RemoveAvatarAsync` | `POST|DELETE /me/avatar` | — |
| [x] | Liens sociaux (validés, http(s) uniquement) | `UserService.ValidateSocialLinks` | `PATCH /me` | — |
| [x] | Profil public / privé | `UserMapper.ToProfileDto` | `GET /users/{username}` | `SearchAndSocialTests.PrivateProfileHidesItsContentFromVisitors` |
| [x] | Profil privé n'expose pas ses données | idem (mode restreint) | idem | idem |
| [x] | Préférences de compteurs | `UserService.GetSettingsAsync` / `UpdateSettingsAsync` | `GET|PATCH /me/settings` | `TrackLifecycleTests.HiddenLikeCounter_…` |
| [x] | Morceaux et playlists publics d'un profil | `TrackService.ListByUserAsync`, `PlaylistService.ListByUsernameAsync` | `GET /users/{username}/tracks`, `/playlists` | `SearchAndSocialTests.PrivateProfileHides…` |

---

## 3. Upload et gestion des morceaux

| # | Exigence | Code | Endpoint | Test |
|---|---|---|---|---|
| [x] | Upload réel (multipart, en flux) | `TrackService.CreateAsync` → `StoreUploadAsync` | `POST /tracks` | `TrackLifecycleTests.UploadedTrack_IsProcessedThenStreamable…` |
| [x] | Taille maximale 20 Mo | `Track.MaxAudioFileSizeBytes`, `AudioFileValidator` | — | `AudioFileValidatorTests.RejectsAFileLargerThanTwentyMegabytes` |
| [x] | Validation du type | `AudioFileValidator.ValidateNameAndSize` | — | `TrackLifecycleTests.UploadingAnUnsupportedFormat_…` (415) |
| [x] | Validation du **contenu** (signature binaire) | `AudioFileValidator.HasKnownAudioSignature` | — | `TrackLifecycleTests.UploadingANonAudioFile_…` (422) |
| [x] | Fichier jamais chargé entièrement en mémoire | `LocalFileStorage.SaveAsync` (copie par flux) | — | — |
| [x] | Extraction des métadonnées | `Infrastructure/Media/AudioMetadataExtractor.cs` (TagLib#) | — | durée détectée dans `TrackLifecycleTests` |
| [x] | Extraction de la pochette embarquée | idem → `ExtractCover` | — | — |
| [x] | Génération de plusieurs tailles de pochette | `Infrastructure/Media/ImageSharpProcessor.cs` (small/medium/large, WebP) | `GET /tracks/{id}/cover/{size}` | vérifié en pile Docker (3 variantes WebP) |
| [x] | Pochette personnalisée | `TrackCoverService.ReplaceAsync` | `POST|DELETE /tracks/{id}/cover` | idem |
| [x] | Progression d'upload affichée | `web/src/pages/UploadPage.tsx` (`XMLHttpRequest`) | — | — |
| [x] | États `UPLOADING`/`PROCESSING`/`READY`/`FAILED` | `TrackStatus`, `UploadOperationStatus` (+ `CANCELLED`) | — | `TrackVisibilityTests` |
| [x] | Nettoyage du fichier partiel en cas d'échec | `TrackService.AbortUploadAsync`, `TrackProcessingService.FailAsync` | — | `TrackLifecycleTests.UploadingANonAudioFile_…` |
| [x] | Base laissée cohérente en cas d'échec | idem (morceau marqué `Failed`, chemin temporaire effacé) | — | idem |
| [x] | Remplacement de fichier sans orphelin | `TrackProcessingService.RunPipelineAsync` (ancien fichier supprimé après bascule) | `POST /tracks/{id}/upload` | — |
| [x] | Protection contre les uploads concurrents | `TrackService.ReplaceFileAsync` (409 si upload en cours) + jeton `xmin` | — | — |
| [x] | Modification des métadonnées | `TrackService.UpdateAsync` | `PATCH /tracks/{id}` | `TrackLifecycleTests.AnotherUser_Cannot…` |
| [x] | Suppression du morceau et de ses fichiers | `TrackService.DeleteAsync` | `DELETE /tracks/{id}` | idem |
| [x] | Publication / dépublication | `Track.Publish` / `Unpublish` | `POST /tracks/{id}/publish`, `/unpublish` | `TrackVisibilityTests.PublishingBeforeProcessing…` |
| [x] | Visibilité `PUBLIC` / `UNLISTED` / `PRIVATE` | `ContentVisibility`, `Track.IsAccessibleBy` | — | `TrackLifecycleTests.UnlistedTrack_…`, `PrivateTrack_…` |
| [x] | Reprise des traitements après arrêt brutal | `Infrastructure/Jobs/MaintenanceServices.cs` → `StalledJobRecoveryService` | — | — |

---

## 4. Streaming et lecteur

| # | Exigence | Code | Endpoint | Test |
|---|---|---|---|---|
| [x] | Streaming HTTP réel | `TrackStreamService.OpenAsync` + `ApiControllerBase.StreamMedia` | `GET /tracks/{id}/stream` | `TrackLifecycleTests.UploadedTrack_…` |
| [x] | `Range: bytes=…` pris en charge | `EnableRangeProcessing = true` sur flux positionnable | idem | idem |
| [x] | `200`, `206`, `416` | idem | idem | idem (les trois cas sont assertés) |
| [x] | `Content-Range`, `Content-Length`, `Accept-Ranges`, `Content-Type` | idem | idem | idem |
| [x] | Fichier jamais chargé en RAM | `LocalFileStorage.OpenReadAsync` (`FileStream`) | idem | — |
| [x] | Lecteur audio React réel | `web/src/features/player/AudioEngine.tsx` | — | `playerStore.test.ts` |
| [x] | Lecture / pause / précédent / suivant | `playerStore.ts` | — | `playerStore.test.ts` (enchaînement) |
| [x] | Seek | `playerStore.seek` + `SeekBar` | — | `playerStore.test.ts` |
| [x] | Volume et sourdine | `playerStore.setVolume` / `toggleMute` | — | `playerStore.test.ts` (bornes) |
| [x] | Shuffle | `buildShuffleOrder` (Fisher-Yates, morceau courant en tête) | — | `playerStore.test.ts` (parcours complet sans doublon) |
| [x] | Repeat (off / all / one) | `playerStore.cycleRepeat` | — | `playerStore.test.ts` |
| [x] | File d'attente | `playerStore` + `QueuePanel` | — | `playerStore.test.ts` |
| [x] | « Lire ensuite » | `playerStore.playNext` | — | `playerStore.test.ts` |
| [x] | Mini-player permanent | `PlayerBar.tsx`, monté hors de l'`Outlet` | — | vérifié au navigateur (persiste après navigation) |
| [x] | Player plein écran | `FullScreenPlayer` dans `PlayerBar.tsx` | — | — |
| [x] | Reprise à la dernière position | `AudioEngine` + `PlaybackService` | `GET|PUT /tracks/{id}/progress` | `SearchAndSocialTests.PlaybackProgressIsStoredAndReturnedForResume` |
| [x] | Media Session API | `AudioEngine.tsx` (`play`, `pause`, `previoustrack`, `nexttrack`, `seekbackward`, `seekforward`, `seekto`) | — | — |
| [x] | Lecteur non recréé au changement de page | monté une seule fois dans `AppShell` | — | vérifié au navigateur |

---

## 5. Likes, écoutes, commentaires

| # | Exigence | Code | Endpoint | Test |
|---|---|---|---|---|
| [x] | Like / unlike | `Features/Tracks/LikeService.cs` | `POST|DELETE /tracks/{id}/like` | `TrackLifecycleTests.LikeIsIdempotent…` |
| [x] | État du like | `LikeService.GetStateAsync` | `GET /tracks/{id}/like` | idem |
| [x] | Compteur de likes | `Track.LikeCount`, incrément SQL atomique | idem | idem |
| [x] | Double like impossible (contrainte base) | clé composite `track_likes(track_id, user_id)` | — | idem |
| [x] | `showLikeCount` respecté | `TrackMapper.ToDto` | — | `TrackLifecycleTests.HiddenLikeCounter_…` |
| [x] | Compteur masqué mais visible du propriétaire | idem | — | idem |
| [x] | Écoute valide à partir de 10 s | `PlaybackService.RegisterPlayAsync`, `Track.MinimumValidPlaySeconds` | `POST /tracks/{id}/plays` | `TrackLifecycleTests.PlayIsCountedOnlyBeyondTenSeconds…` |
| [x] | Le serveur ne fait pas confiance au client | durée bornée par celle du morceau, déduplication imposée | idem | idem |
| [x] | Anti-abus sur les écoutes répétées | marqueur Redis + repli SQL, fenêtre de 5 minutes | idem | idem |
| [x] | Événements d'écoute | entité `PlayEvent` | — | idem |
| [x] | Statistiques mises à jour sans bloquer le lecteur | appels non bloquants côté `AudioEngine` | — | — |
| [x] | Création / modification / suppression de commentaire | `Features/Comments/CommentService.cs` | `POST /tracks/{id}/comments`, `PATCH|DELETE /comments/{id}` | `TrackLifecycleTests.TimestampedComment_…` |
| [x] | Pagination des commentaires | `CommentService.ListAsync` | `GET /tracks/{id}/comments` | — |
| [x] | Commentaire horodaté | `Comment.TimestampSeconds`, borné par la durée | idem | `TrackLifecycleTests.TimestampedComment_…` |
| [x] | Clic sur l'horodatage → déplacement de la lecture | `web/src/pages/TrackPage.tsx` → `seekTo` | — | — |

---

## 6. Playlists

| # | Exigence | Code | Endpoint | Test |
|---|---|---|---|---|
| [x] | Création / modification / suppression | `Features/Playlists/PlaylistService.cs` | `POST|PATCH|DELETE /playlists` | `PlaylistTests.*` |
| [x] | Visibilité publique / privée / non répertoriée | `Playlist.IsAccessibleBy` | — | `PlaylistTests.PrivatePlaylistIsInvisible…` |
| [x] | Pochette de playlist | `PlaylistService.SetCoverAsync` | `POST /playlists/{id}/cover` | — |
| [x] | Ajout / suppression de morceau | `AddTrackAsync` / `RemoveTrackAsync` | `POST|DELETE /playlists/{id}/tracks` | `PlaylistTests.ReorderPersistsPositions…` |
| [x] | Positions compactées après suppression | `RemoveTrackAsync` | idem | `PlaylistTests.RemovingATrackCompactsTheRemainingPositions` |
| [x] | Réorganisation | `Playlist.Reorder` (règle de domaine) | `PATCH /playlists/{id}/tracks/reorder` | `PlaylistTests.ReorderPersistsPositions…` + 6 tests unitaires |
| [x] | Positions persistées correctement | transaction + validation stricte | idem | idem |
| [x] | Conflits et validations | doublon → 409 ; réordonnancement incomplet → 422 | idem | idem |
| [x] | Drag & drop réellement fonctionnel | `web/src/pages/PlaylistPage.tsx` (dnd-kit, clavier compris) | — | — |
| [x] | Duplication | `PlaylistService.DuplicateAsync` | `POST /playlists/{id}/duplicate` | `PlaylistTests.PublicPlaylistCanBeDuplicated…` |
| [x] | Partage | lien direct + bouton de copie | — | — |
| [x] | Suivi de playlist | `FollowAsync` / `UnfollowAsync` | `POST|DELETE /playlists/{id}/follow` | `PlaylistTests.FollowAndFavoriteArePerUser…` |
| [x] | Favoris | `FavoriteAsync` / `UnfavoriteAsync` | `POST|DELETE /playlists/{id}/favorite` | idem |
| [x] | Playlists système « Mes likes » et « Écoutés récemment » | `GET /me/likes`, `GET /me/history` (pages dédiées, lecture en file) | — | `SearchAndSocialTests.PlaybackProgress…` |

---

## 7. Réseau social

| # | Exigence | Code | Endpoint | Test |
|---|---|---|---|---|
| [x] | Follow / unfollow | `UserService.FollowAsync` / `UnfollowAsync` | `POST|DELETE /users/{id}/follow` | `SearchAndSocialTests.FollowingAUserIsIdempotent…` |
| [x] | Auto-abonnement refusé (domaine + base) | `Follow.Create` + contrainte `ck_follows_no_self_follow` | idem | idem |
| [x] | Listes d'abonnés et d'abonnements | `ListFollowersAsync` / `ListFollowingAsync` | `GET /users/{id}/followers`, `/following`, `/me/followers`, `/me/following` | — |

---

## 8. Recherche et découverte

| # | Exigence | Code | Endpoint | Test |
|---|---|---|---|---|
| [x] | Recherche morceaux / artistes / albums / playlists / tags | `Features/Search/SearchService.cs` | `GET /search` | `SearchAndSocialTests.SearchFindsTracksByTitleAndByTag` |
| [x] | Recherche par genre | filtre `genre` | idem | — |
| [x] | Tags avec préfixe `#` | `Tag.Normalize`, détection du `#` | idem | `SearchAndSocialTests.SearchFindsTracksByTitleAndByTag` |
| [x] | Pagination, filtres, tri | `PageRequest`, `TrackFilter`, `ApplySort` | idem | `PagingAndPatternTests` |
| [x] | Recherche instantanée avec debounce | `web/src/hooks/index.ts` → `useDebounced` | — | — |
| [x] | Abstraction permettant OpenSearch plus tard | interface `ISearchService`, implémentation `PostgresSearchService` | — | — |
| [x] | Jokers `%` / `_` échappés | `Common/SqlPatterns.cs` + caractère d'échappement explicite | — | `SearchAndSocialTests.SearchWithWildcardCharacters…` |
| [x] | Page d'accueil (récents, populaires, artistes, playlists, recommandations, abonnements) | `Features/Discovery/HomeService.cs` | `GET /home` | — |
| [x] | Recommandations simples et déterministes | `Features/Discovery/RecommendationService.cs` (score documenté) | `GET /recommendations/tracks`, `/artists` | — |
| [x] | Aucun apprentissage automatique | score arithmétique explicite | — | — |
| [x] | Genres et tags | `Features/Catalog/CatalogService.cs` | `GET /genres`, `/genres/{id}/tracks`, `/tags`, `/tags/{tag}/tracks` | `InfrastructureTests.GenresAreSeededAtStartup` |

---

## 9. Statistiques artiste

| # | Exigence | Code | Endpoint | Test |
|---|---|---|---|---|
| [x] | Nombre de morceaux, écoutes, likes, abonnés | `Features/Analytics/AnalyticsService.cs` | `GET /me/analytics/overview` | `SearchAndSocialTests.ArtistAnalyticsCoverOwnedTracksOnly` |
| [x] | Évolution des écoutes | `GetPlaysSeriesAsync` | `GET /me/analytics/plays` | idem |
| [x] | Filtrage par période `day` / `week` / `month` | `AnalyticsGroupBy`, `GroupPoints` | idem | `AnalyticsAggregationTests` (5 tests) |
| [x] | Morceaux les plus écoutés | `GetTopTracksAsync` | `GET /me/analytics/top-tracks` | — |
| [x] | Playlists contenant ses morceaux | `TrackAnalyticsDto.PlaylistCount` | `GET /me/analytics/tracks` | — |
| [x] | Agrégation maîtrisée et paginée | agrégation SQL, plage bornée à 366 jours | — | `AnalyticsAggregationTests.RangeRejectsAPeriodLongerThanAYear` |

---

## 10. Signalements et modération

| # | Exigence | Code | Endpoint | Test |
|---|---|---|---|---|
| [x] | Motifs `COPYRIGHT` / `OFFENSIVE` / `SPAM` / `OTHER` | `ReportReason` | — | — |
| [x] | Signalement d'un morceau, commentaire, profil, playlist | `Features/Moderation/ReportService.cs` | `POST /reports` | `AdminTests.ReportResolutionHidesTheTrack…` |
| [x] | Historique des signalements émis | `ListMineAsync` | `GET /me/reports` | — |
| [x] | Consultation et filtrage par la modération | `ListForModerationAsync` | `GET /admin/reports` | `AdminTests.ReportResolution…` |
| [x] | Traitement : résolu / rejeté / en examen | `ResolveAsync` | `PATCH /admin/reports/{id}` | idem |
| [x] | Masquage du contenu visé | `HideTargetAsync` (morceau, commentaire, profil, playlist) | idem | idem |
| [x] | Restauration | `AdminService.RestoreTrackAsync` | `POST /admin/tracks/{id}/restore` | idem |
| [x] | Justification enregistrée | `Report.ResolutionNote` | idem | — |
| [x] | Actions importantes tracées dans `AuditLog` | `AuditLogger.RecordAsync` | `GET /admin/audit-logs` | `AdminTests.ReportResolution…` (vérifie l'entrée) |

---

## 11. Administration

| # | Exigence | Code | Endpoint | Test |
|---|---|---|---|---|
| [x] | Liste et recherche d'utilisateurs | `Features/Admin/AdminService.ListUsersAsync` | `GET /admin/users` | `AdminTests.AdminCanListUsers…` |
| [x] | Consultation d'un utilisateur | `GetUserAsync` | `GET /admin/users/{id}` | — |
| [x] | Gestion des rôles et du statut | `UpdateUserAsync` | `PATCH /admin/users/{id}` | `AdminTests.SuspendingAUserRevokesTheirActiveSessions` |
| [x] | Suppression administrative d'un compte | `DeleteUserAsync` → `AccountService.PurgeUserAsync` | `DELETE /admin/users/{id}` | — |
| [x] | Liste globale des morceaux | `ListTracksAsync` (masqués et supprimés inclus) | `GET /admin/tracks` | — |
| [x] | Masquage / restauration / suppression d'un morceau | `HideTrackAsync`, `RestoreTrackAsync`, `DeleteTrackAsync` | `POST /admin/tracks/{id}/hide`, `/restore`, `DELETE` | `AdminTests.ReportResolution…` |
| [x] | Statistiques globales | `GetStatisticsAsync` | `GET /admin/statistics` | `AdminTests.AdminCanListUsersAndReadGlobalStatistics` |
| [x] | Gestion des genres | `CreateGenreAsync`, `UpdateGenreAsync`, `DeleteGenreAsync` | `POST|PATCH|DELETE /admin/genres` | `AdminTests.DuplicateGenreNameIsRejected`, `GenreStillInUse…` |
| [x] | Journal d'audit | `ListAuditLogsAsync` | `GET /admin/audit-logs` | `AdminTests.ReportResolution…` |
| [x] | Séparation réelle des permissions | modération ≠ administration (deux policies + contrôles applicatifs) | — | `AdminTests.AdminEndpointsAreClosedToRegularUsers` |
| [x] | Un administrateur ne peut pas se retirer ses droits | `UpdateUserAsync` | — | `AdminTests.AdminCannotRevokeTheirOwnAccess` |
| [x] | Interface d'administration complète | `web/src/pages/AdminPages.tsx` (6 écrans) | — | — |

---

## 12. Export et suppression de compte

| # | Exigence | Code | Endpoint | Test |
|---|---|---|---|---|
| [x] | Demande d'export | `Features/Account/AccountService.RequestExportAsync` | `POST /me/data-export` | `AccountTests.DataExportProducesADownloadableArchive…` |
| [x] | Génération en arrière-plan | `Features/Account/UserExportGenerator.cs` | — | idem |
| [x] | États `PENDING`/`PROCESSING`/`READY`/`FAILED`/`EXPIRED` | `UserExportStatus` | `GET /me/data-exports/{id}` | idem |
| [x] | Contenu : profil, morceaux, playlists, likes, commentaires, abonnements, historique, paramètres, signalements | `UserExportGenerator.WriteArchiveAsync` (11 entrées) | — | idem (archive ZIP valide) |
| [x] | Téléchargement protégé | `DownloadExportAsync` (réservé au propriétaire) | `GET /me/data-exports/{id}/download` | idem (404 pour un tiers) |
| [x] | Durée de vie limitée des archives | `ExportLifetime` = 7 jours + `MaintenanceService` | — | `SocialRulesTests.ExportIsDownloadableOnly…` |
| [x] | Avertissement avant suppression | `web/src/pages/SettingsPage.tsx` | — | — |
| [x] | Export proposé avant suppression | idem (les deux sections se suivent) | — | — |
| [x] | Confirmation explicite exigée | `DeleteOwnAccountAsync` (case + saisie du pseudo) | `DELETE /me` | `AccountTests.AccountDeletionRequiresExplicitConfirmation` |
| [x] | Suppression des données personnelles | `PurgeUserAsync` → `Anonymize` | idem | `AccountTests.AccountDeletionRemovesPersonalData…` |
| [x] | Suppression des fichiers associés | `CollectUserFilesAsync` + suppression des dossiers | idem | idem |
| [x] | Nettoyage des références, pas d'orphelin | cascades en base + dissociation des `PlayEvent` | idem | idem |
| [x] | Fonctionnalité testée | — | — | 2 tests d'intégration dédiés |

---

## 13. Modèle de données

| # | Exigence | Vérification |
|---|---|---|
| [x] | Toutes les entités du document créées | 25 tables : `users`, `user_settings`, `refresh_tokens`, `follows`, `stored_files`, `tracks`, `track_files`, `track_metadata`, `track_covers`, `track_tags`, `track_likes`, `upload_operations`, `albums`, `genres`, `tags`, `playlists`, `playlist_items`, `playlist_follows`, `playlist_favorites`, `comments`, `play_events`, `listening_histories`, `reports`, `audit_logs`, `user_exports` |
| [x] | Identifiants UUID | aucune clé auto-incrémentée |
| [x] | Dates en UTC | `UtcDateTimeConverter` appliqué par convention à toutes les dates |
| [x] | Migration initiale propre | `Persistence/Migrations/20260814152603_InitialCreate` — appliquée sur base vierge en CI |
| [x] | Index pertinents | 78 index, couvrant l'intégralité de la liste du § 27 du modèle |
| [x] | Contraintes d'unicité | email, pseudo normalisé, slugs de genre et de tag, `(track_id, size)`, `(user_id, track_id)` d'historique |
| [x] | Contraintes SQL | 7 contraintes `CHECK` : durée, compteurs, positions, horodatages, auto-abonnement |
| [x] | Clés étrangères | cascades et `SET NULL` explicitement configurés |
| [x] | Many-to-many correctement contraints | `track_tags`, `playlist_items`, `track_likes`, `follows`, `playlist_follows`, `playlist_favorites` — toutes en clé composite |
| [x] | Seed des genres | `DatabaseSeeder` — 24 genres, opération idempotente |
| [x] | Transactions aux endroits critiques | like/unlike, réordonnancement, retrait de morceau, duplication, traitement d'un signalement, suppression de compte |
| [x] | Compensation des fichiers orphelins | suppression après cohérence de la base + `MaintenanceService` périodique |

---

## 14. Architecture et qualité

| # | Exigence | Vérification |
|---|---|---|
| [x] | Découpage API / Application / Domain / Infrastructure | quatre projets distincts |
| [x] | Domaine sans dépendance technique | `MusicPlatform.Domain.csproj` ne référence aucun paquet |
| [x] | Monolithe modulaire, pas de microservices | un seul service applicatif |
| [x] | Cas d'utilisation testables indépendamment | 21 services applicatifs enregistrés, couverts par les tests unitaires |
| [x] | Abstraction `IFileStorage` + `LocalFileStorage` | `Application/Abstractions/IFileStorage.cs`, `Infrastructure/Storage/LocalFileStorage.cs` |
| [x] | Domaine ignorant les chemins physiques | chemins logiques uniquement, via `StoragePaths` |
| [x] | Aucun chemin physique exposé | les DTO n'exposent que des URL d'API |
| [x] | Redis utilisé à bon escient | cache de recommandations, déduplication des écoutes |
| [x] | Redis jamais source de vérité | dégradation silencieuse ; suite d'intégration exécutée sans Redis |
| [x] | Problem Details avec code métier | `ExceptionHandlingMiddleware` | 
| [x] | Aucune trace d'exécution en production | message générique hors développement |
| [x] | Entités EF jamais exposées | tous les retours sont des DTO |
| [x] | DTO d'entrée et de sortie séparés | `CreateTrackRequest` / `TrackDto`, etc. |
| [x] | Validation de toutes les entrées | attributs de route, validation applicative, contraintes de base |
| [x] | Nullable reference types activés | `Directory.Build.props` |
| [x] | Avertissements traités comme des erreurs | `TreatWarningsAsErrors` |
| [x] | Pas de `.Result` ni de `.Wait()` | vérifié, aucun blocage synchrone |
| [x] | `CancellationToken` propagé | sur l'ensemble des opérations d'E/S |
| [x] | `AsNoTracking()` en lecture | appliqué à toutes les requêtes de consultation |
| [x] | Pas de requête N+1 | projections uniques (`TrackQueries.Project`, `UserQueries.Project`) |
| [x] | Collections paginées | `PageRequest` borné à 100 |
| [x] | Récursion absente | aucun appel récursif |
| [x] | `goto` absent | — |
| [x] | Boucles bornées | itérations sur collections finies ; boucles de fond pilotées par un jeton d'arrêt |
| [x] | Erreurs jamais ignorées silencieusement | tous les `catch` journalisent ou compensent, et sont commentés |

---

## 15. Infrastructure, tests et livraison

| # | Exigence | Vérification |
|---|---|---|
| [x] | `docker compose up` lance tout | `postgres`, `redis`, `api`, `web` — **vérifié : les 4 services démarrent sains** |
| [x] | Volumes persistants | `postgres-data`, `redis-data`, `storage-data` |
| [x] | Versions d'images explicites | `postgres:17-alpine`, `redis:7-alpine`, `node:24-alpine`, `nginx:1.27-alpine`, `dotnet/*:10.0` — aucun `latest` |
| [x] | Dockerfiles multi-étapes, exécution sans privilège | `src/MusicPlatform.Api/Dockerfile`, `web/Dockerfile` |
| [x] | `.env.example` avec valeurs fictives | à la racine ; `.env` ignoré par Git |
| [x] | Aucun secret dans le dépôt | vérifié |
| [x] | CI GitHub Actions | `.github/workflows/ci.yml` — 4 jobs |
| [x] | Restauration, compilation, tests, vérification du frontend, construction des images | jobs `backend`, `frontend`, `migrations`, `docker` |
| [x] | Contrôle des migrations en CI | `has-pending-model-changes` — **vérifié localement : aucune modification en attente** |
| [x] | Tests unitaires | 92, verts |
| [x] | Tests d'intégration | 43, verts, sur PostgreSQL réel (Testcontainers) |
| [x] | Tests frontend | 39, verts |
| [x] | Health checks `/health`, `/health/live`, `/health/ready` | `InfrastructureTests` (3 tests) |
| [x] | `/health/ready` vérifie les dépendances | PostgreSQL, Redis, stockage |
| [x] | Logs structurés et trace ID | Serilog + `traceId` dans chaque réponse d'erreur |
| [x] | OpenTelemetry | traces et métriques, export OTLP conditionnel |
| [x] | Swagger utilisable | `/swagger` — **vérifié en pile Docker** |
| [x] | Documentation OpenAPI des endpoints, paramètres, DTO et erreurs | commentaires XML sur tous les contrôleurs |
| [x] | `README.md` complet | présentation, architecture, installation, variables, migrations, tests, API, sécurité, déploiement VPS |

---

## 16. Périmètre volontairement exclu

Conformément au § 2 du prompt et au § 20 du cahier des charges, les éléments suivants
**ne sont pas** implémentés, et c'est intentionnel :

application React Native · application iOS · mode hors ligne · API publique · webhooks ·
notifications · paroles · apprentissage automatique avancé · transcodage audio ·
multi-qualité · import de bibliothèque externe · vérification d'email ·
récupération de mot de passe (exclus du MVP par le § 4 du cahier des charges).

L'architecture reste ouverte à l'ajout de l'application mobile : l'API est un service REST
autonome, sans état de session côté serveur, que le site web consomme exactement comme le
ferait un client React Native.

---

## 17. Réserves

Deux points méritent d'être signalés explicitement.

**[~] Lecture audio non validée dans un navigateur automatisé.** La chaîne HTTP de
streaming est vérifiée de bout en bout — `200`, `206` avec `Content-Range`, `416`,
`Accept-Ranges`, `ETag` — par un test d'intégration et par des appels directs à travers
nginx. L'interface du lecteur, la persistance de la file et l'enchaînement des morceaux
sont couverts par 39 tests frontend et vérifiés visuellement au navigateur. En revanche, la
restitution sonore elle-même n'a pas pu être observée : l'environnement Chrome automatisé
utilisé pour la vérification n'a pas de pipeline média — même un fichier WAV encodé en
`data:` URI n'y est pas décodé. Une écoute manuelle dans un navigateur ordinaire reste donc
à faire.

**[~] Tests de bout en bout dédiés.** Les parcours critiques listés au § 24 de
`architecture.md` — inscription, authentification, upload, création de playlist, lecture,
like, commentaire, export, suppression — sont tous couverts, mais au niveau de l'API par
les 43 tests d'intégration, et non par un outil de navigation type Playwright. Le couple
« tests d'intégration API + tests de composants frontend » couvre la logique ; il ne couvre
pas les régressions purement visuelles.
