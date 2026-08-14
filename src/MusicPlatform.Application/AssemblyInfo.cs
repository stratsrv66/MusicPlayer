using System.Runtime.CompilerServices;

// Les helpers marqués `internal` (agrégation des statistiques, application des filtres)
// sont testés directement plutôt qu'exposés publiquement sans nécessité.
[assembly: InternalsVisibleTo("MusicPlatform.UnitTests")]
[assembly: InternalsVisibleTo("MusicPlatform.IntegrationTests")]
