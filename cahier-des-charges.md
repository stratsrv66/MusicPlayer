# Cahier des charges --- Plateforme musicale

## 1. Présentation

**Nom de travail :** à définir\
**Type :** projet portfolio full-stack multi-utilisateurs\
**Positionnement :** plateforme musicale inspirée de SoundCloud,
destinée principalement à des artistes souhaitant publier leurs propres
morceaux.

L'application sera disponible sous deux formes :

-   site web responsive, pensé **mobile-first** ;
-   application mobile Android réalisée avec **React Native**.

L'objectif est de construire un produit crédible et production-ready
permettant de démontrer des compétences en conception logicielle,
développement backend .NET, frontend, mobile, sécurité, stockage de
fichiers, traitement audio, tests, Docker, CI/CD et déploiement.

## 2. Objectifs

### Objectifs fonctionnels

-   permettre à un utilisateur de créer un compte avec email et mot de
    passe ;
-   permettre aux artistes d'importer leurs morceaux ;
-   gérer les métadonnées et pochettes ;
-   publier des morceaux avec une visibilité configurable ;
-   créer et gérer des playlists ;
-   suivre des utilisateurs/artistes ;
-   aimer des morceaux ;
-   commenter des morceaux ;
-   écouter de la musique avec un lecteur complet ;
-   mémoriser la progression d'écoute ;
-   conserver l'historique d'écoute ;
-   rechercher morceaux, artistes, albums, playlists, utilisateurs et
    tags ;
-   proposer une page d'accueil personnalisée ;
-   proposer des recommandations simples ;
-   fournir des statistiques aux propriétaires des morceaux ;
-   permettre l'export des données avant suppression du compte ;
-   permettre de signaler des contenus ;
-   fournir une administration complète.

### Objectifs techniques

-   API REST ASP.NET Core ;
-   architecture maintenable et simple à expliquer ;
-   PostgreSQL ;
-   Redis ;
-   stockage local des fichiers en développement et production initiale
    ;
-   Docker et Docker Compose ;
-   CI/CD GitHub Actions ;
-   déploiement sur VPS ;
-   tests unitaires et d'intégration ;
-   observabilité ;
-   sécurité ;
-   documentation OpenAPI/Swagger.

## 3. Utilisateurs et rôles

### USER

Utilisateur standard pouvant :

-   gérer son profil ;
-   rendre son profil public ou privé ;
-   uploader ses propres morceaux ;
-   créer des playlists ;
-   suivre des utilisateurs ;
-   liker des morceaux ;
-   commenter ;
-   consulter son historique ;
-   exporter ses données ;
-   supprimer son compte.

### ARTIST

Le rôle artiste sera conservé comme rôle métier distinct si nécessaire,
mais le produit doit rester simple : un utilisateur peut publier ses
propres morceaux sans devoir créer un compte séparé.

### MODERATOR

Peut :

-   consulter les signalements ;
-   masquer un contenu ;
-   traiter les signalements ;
-   appliquer les actions de modération prévues.

### ADMIN

Peut en plus :

-   gérer les utilisateurs ;
-   gérer les rôles ;
-   gérer les genres et tags ;
-   consulter les statistiques globales ;
-   consulter les journaux d'audit ;
-   gérer les contenus signalés ;
-   administrer la plateforme.

## 4. Comptes et profils

L'inscription se fait avec :

-   email ;
-   mot de passe.

Pas de vérification email et pas de récupération de mot de passe dans le
MVP.

Le profil peut contenir :

-   pseudo ;
-   email ;
-   avatar ;
-   bio ;
-   liens sociaux ;
-   visibilité du profil ;
-   préférences liées aux statistiques.

Un profil peut être public ou privé.

La suppression du compte doit afficher une proposition d'export des
données avant confirmation.

## 5. Export et suppression

Avant suppression définitive, l'utilisateur doit pouvoir demander un
export de ses données.

L'export peut contenir au minimum :

-   informations du profil ;
-   morceaux ;
-   playlists ;
-   likes ;
-   commentaires ;
-   abonnements ;
-   historique d'écoute ;
-   paramètres utilisateur.

Après confirmation :

1.  proposer l'export ;
2.  demander confirmation ;
3.  supprimer les données personnelles ;
4.  supprimer les fichiers appartenant à l'utilisateur selon les règles
    métier ;
5.  conserver uniquement les données strictement nécessaires à
    l'intégrité/audit si cela est prévu.

## 6. Gestion des morceaux

Formats acceptés : les formats audio courants définis lors de
l'implémentation.

Contraintes :

-   taille maximale : **20 Mo** ;
-   pas de limite de durée ;
-   qualité conservée à la qualité maximale du fichier fourni ;
-   aucun transcodage obligatoire ;
-   fichier original non conservé après traitement si le pipeline génère
    un fichier de diffusion distinct.

Lors de l'upload :

1.  validation du fichier ;
2.  extraction des métadonnées ;
3.  extraction éventuelle de la pochette embarquée ;
4.  génération des tailles de pochettes ;
5.  stockage ;
6.  création du morceau ;
7.  publication selon la visibilité choisie.

Métadonnées :

-   titre ;
-   artiste ;
-   album ;
-   genre ;
-   année ;
-   durée ;
-   pochette ;
-   tags ;
-   autres métadonnées utiles.

L'utilisateur peut modifier les métadonnées après upload.

Une pochette séparée peut être fournie.

## 7. Upload

L'interface doit afficher une progression.

Le système doit empêcher qu'un ancien fichier reste associé au morceau
lorsque l'utilisateur remplace un fichier.

En cas d'échec ou d'annulation, l'upload peut être repris depuis le
début. Le fichier partiellement uploadé doit être nettoyé.

Le système doit également gérer correctement les doubles soumissions et
les uploads concurrents.

## 8. Visibilité des morceaux

Chaque morceau dispose d'une visibilité configurable.

Valeurs proposées :

-   `PUBLIC` : visible par tous ;
-   `UNLISTED` : accessible par lien mais non référencé dans les
    recherches publiques ;
-   `PRIVATE` : accessible uniquement au propriétaire et aux personnes
    explicitement autorisées si cette capacité est ajoutée
    ultérieurement.

La visibilité devra être conçue de manière extensible.

## 9. Likes et vues

Un propriétaire peut choisir si les compteurs sont visibles publiquement
:

-   visibilité des likes ;
-   visibilité des vues/écoutes.

Les compteurs peuvent rester disponibles au propriétaire dans son
dashboard même lorsqu'ils sont masqués publiquement.

Une écoute est comptabilisée après **10 secondes** de lecture.

Le système doit éviter de compter plusieurs fois abusivement la même
écoute à partir d'une même session courte.

## 10. Lecteur audio

Fonctionnalités :

-   lecture ;
-   pause ;
-   précédent ;
-   suivant ;
-   seek ;
-   volume ;
-   shuffle ;
-   repeat ;
-   file d'attente ;
-   ajouter à la lecture suivante ;
-   mini-player permanent ;
-   player plein écran sur mobile ;
-   reprise à la dernière position connue ;
-   historique d'écoute.

Le player doit exploiter les capacités du navigateur/mobile pour la
lecture en arrière-plan.

L'application mobile Android doit supporter :

-   écran verrouillé ;
-   contrôles multimédias système ;
-   casque/Bluetooth ;
-   commandes précédent/suivant/play/pause.

La mise en œuvre exacte reposera sur les APIs Media Session et les
capacités natives de React Native.

## 11. Playlists

Un utilisateur peut :

-   créer une playlist ;
-   modifier son nom ;
-   modifier sa description ;
-   changer sa pochette ;
-   rendre la playlist publique, privée ou non répertoriée ;
-   ajouter/supprimer des morceaux ;
-   réordonner les morceaux ;
-   dupliquer une playlist ;
-   partager une playlist ;
-   suivre une playlist ;
-   ajouter une playlist aux favoris.

Playlists système prévues :

-   Mes likes ;
-   Écoutés récemment.

## 12. Réseau social

Fonctionnalités :

-   follow/unfollow ;
-   likes ;
-   commentaires ;
-   profils publics ;
-   profils privés ;
-   affichage des morceaux publics ;
-   affichage des playlists publiques.

Les commentaires peuvent être associés à un timestamp du morceau.

Exemple :

`01:34 — commentaire`

## 13. Recherche

La recherche porte sur :

-   morceaux ;
-   artistes/utilisateurs ;
-   albums ;
-   playlists ;
-   genres ;
-   tags.

Les tags sont écrits avec le préfixe `#`.

Exemples :

`#rock`, `#electro`, `#indie`.

Recherche instantanée et filtres :

-   type ;
-   genre ;
-   tag ;
-   artiste ;
-   durée ;
-   date ;
-   popularité.

Le projet prévoit l'utilisation d'un moteur de recherche dédié si la
volumétrie le justifie. Pour le MVP, une première implémentation
PostgreSQL est acceptable, avec une abstraction permettant une évolution
vers Elasticsearch/OpenSearch.

## 14. Accueil et découverte

L'accueil peut présenter :

-   morceaux récents ;
-   morceaux populaires ;
-   artistes populaires ;
-   playlists populaires ;
-   recommandations ;
-   contenus provenant des artistes suivis.

Le système de recommandation du MVP reste simple.

Exemples :

-   artistes suivis ;
-   genres écoutés ;
-   morceaux similaires par tags/genres ;
-   popularité récente.

Aucun système de machine learning n'est requis.

## 15. Statistiques artiste

Dashboard permettant de consulter :

-   nombre d'écoutes ;
-   nombre de likes ;
-   évolution des écoutes ;
-   morceaux les plus écoutés ;
-   playlists contenant ses morceaux ;
-   abonnés ;
-   historique des performances.

Les statistiques doivent pouvoir être filtrées par période.

## 16. Modération

Les utilisateurs peuvent signaler un contenu.

Motifs minimum :

-   copyright ;
-   contenu offensant ;
-   spam ;
-   autre.

Les modérateurs/admins peuvent :

-   consulter les signalements ;
-   changer leur statut ;
-   masquer un morceau/commentaire/profil ;
-   restaurer un contenu ;
-   enregistrer une justification.

Les actions administratives importantes doivent être tracées dans un
audit log.

## 17. Sécurité

Le système doit inclure :

-   authentification sécurisée ;
-   autorisation par rôle/policy ;
-   validation des entrées ;
-   validation stricte des uploads ;
-   limitation des accès aux fichiers ;
-   protection contre les accès directs à des ressources privées ;
-   rate limiting ;
-   prévention des abus sur les endpoints sensibles ;
-   gestion sécurisée des secrets ;
-   logs sans données sensibles ;
-   contrôle des permissions ;
-   protection contre les attaques classiques d'API.

Les limites métier explicites demandées par le projet restent minimales
; les protections techniques sont néanmoins obligatoires.

## 18. Architecture cible

Stack principale :

-   C# ;
-   ASP.NET Core ;
-   Entity Framework Core ;
-   PostgreSQL ;
-   Redis ;
-   React pour le web ;
-   React Native pour Android ;
-   Docker ;
-   Docker Compose ;
-   GitHub Actions ;
-   VPS.

Le choix des bibliothèques complémentaires doit privilégier la
simplicité et la lisibilité.

## 19. Environnements

Deux environnements :

-   `development` ;
-   `production`.

Le développement doit pouvoir être lancé localement avec Docker Compose.

## 20. MVP

Le MVP comprend toutes les fonctionnalités fonctionnelles définies dans
ce document.

Les fonctionnalités explicitement exclues ne doivent pas être ajoutées
artificiellement au périmètre :

-   transcodage automatique ;
-   multi-qualité ;
-   mode offline ;
-   lyrics ;
-   API publique ;
-   webhooks ;
-   notifications ;
-   machine learning avancé ;
-   import externe de bibliothèque.

## 21. Critères de qualité

Le projet doit être :

-   compréhensible par un développeur externe ;
-   documenté ;
-   testable ;
-   déployable ;
-   sécurisé ;
-   observable ;
-   maintenable ;
-   suffisamment simple pour être expliqué en entretien.

## 22. Valeur portfolio

Le projet doit permettre de démontrer :

-   conception d'une API REST ;
-   architecture backend ;
-   gestion de l'authentification ;
-   gestion des rôles ;
-   PostgreSQL et modélisation relationnelle ;
-   gestion de fichiers ;
-   streaming audio ;
-   application web responsive ;
-   application mobile ;
-   cache Redis ;
-   recherche ;
-   statistiques ;
-   sécurité ;
-   tests ;
-   Docker ;
-   CI/CD ;
-   déploiement VPS ;
-   observabilité ;
-   gestion de produit et arbitrage technique.
