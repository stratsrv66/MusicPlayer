// Les suites d'intégration configurent l'hôte par variables d'environnement, qui sont
// globales au processus. Le parallélisme est donc désactivé pour garantir des résultats
// déterministes ; la durée totale reste de l'ordre de quelques dizaines de secondes.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
