import { useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { importsApi } from '../services/api';
import { ErrorMessage, Loading } from '../components/common';
import { formatDuration } from '../lib/format';
import type { ContentVisibility, ExternalPlaylist, PlaylistImportItemStatus, PlaylistPreview } from '../types/api';

/** Intervalle d'interrogation de la progression, en millisecondes. */
const POLL_INTERVAL_MS = 2000;

/** Libellé et style du badge associé à l'état d'un morceau. */
const ITEM_STATUS: Record<PlaylistImportItemStatus, { label: string; className: string }> = {
  Pending: { label: 'En attente', className: 'badge' },
  Running: { label: 'En cours', className: 'badge badge-accent' },
  Imported: { label: 'Importé', className: 'badge badge-success' },
  Duplicate: { label: 'Déjà présent', className: 'badge badge-accent' },
  Failed: { label: 'Échec', className: 'badge badge-danger' },
  Cancelled: { label: 'Annulé', className: 'badge' },
};

/**
 * Import d'une playlist YouTube.
 *
 * L'écran suit trois temps : choix de la playlist, aperçu du contenu avant de s'engager,
 * puis suivi de la progression. Chaque morceau est téléchargé par le serveur exactement
 * comme un morceau importé depuis un lien YouTube isolé.
 */
export function ImportPlaylistPage() {
  const [url, setUrl] = useState('');
  const [preview, setPreview] = useState<PlaylistPreview | null>(null);
  const [activeImportId, setActiveImportId] = useState<string | null>(null);

  const previewMutation = useMutation({
    mutationFn: () => importsApi.preview(url.trim()),
    onSuccess: setPreview,
  });

  /** Repart de l'écran de sélection en oubliant l'aperçu courant. */
  function reset() {
    setPreview(null);
    setActiveImportId(null);
    previewMutation.reset();
  }

  if (activeImportId) {
    return <ImportProgress importId={activeImportId} onClose={reset} />;
  }

  return (
    <>
      <h1>Importer une playlist YouTube</h1>

      {preview ? (
        <PreviewPanel preview={preview} url={url.trim()} onCancel={reset} onStarted={setActiveImportId} />
      ) : (
        <>
          <form
            className="card"
            style={{ maxWidth: 720 }}
            onSubmit={(event: FormEvent) => {
              event.preventDefault();
              previewMutation.mutate();
            }}
          >
            <ErrorMessage error={previewMutation.error} />

            <div className="field">
              <label htmlFor="import-url">Lien de la playlist</label>
              <input
                id="import-url"
                type="text"
                required
                value={url}
                onChange={(event) => setUrl(event.target.value)}
                placeholder="https://www.youtube.com/playlist?list=…"
                aria-describedby="import-url-help"
              />
              <span id="import-url-help" className="small muted">
                Collez le lien d'une playlist publique. Son contenu vous sera présenté avant tout import.
              </span>
            </div>

            <button
              type="submit"
              className="btn btn-primary"
              disabled={previewMutation.isPending || url.trim().length === 0}
            >
              {previewMutation.isPending ? 'Analyse…' : 'Analyser la playlist'}
            </button>
          </form>

          <ChannelBrowser
            onSelect={(playlist) => {
              setUrl(playlist.url);
              previewMutation.reset();
            }}
          />

          <RecentImports onOpen={setActiveImportId} />
        </>
      )}
    </>
  );
}

/** Parcourt les playlists publiques d'une chaîne pour éviter d'avoir à coller un lien. */
function ChannelBrowser({ onSelect }: { onSelect: (playlist: ExternalPlaylist) => void }) {
  const [profileId, setProfileId] = useState('');

  const mutation = useMutation({
    mutationFn: () => importsApi.profilePlaylists(profileId.trim()),
  });

  return (
    <form
      className="card"
      style={{ maxWidth: 720, marginTop: 24 }}
      onSubmit={(event: FormEvent) => {
        event.preventDefault();
        mutation.mutate();
      }}
    >
      <h2>Parcourir une chaîne</h2>
      <ErrorMessage error={mutation.error} />

      <div className="field">
        <label htmlFor="import-channel">Chaîne YouTube</label>
        <div className="row">
          <input
            id="import-channel"
            className="grow"
            value={profileId}
            onChange={(event) => setProfileId(event.target.value)}
            placeholder="@nomdelachaine"
          />
          <button type="submit" className="btn" disabled={mutation.isPending || profileId.trim().length === 0}>
            {mutation.isPending ? 'Recherche…' : 'Lister'}
          </button>
        </div>
      </div>

      {mutation.data && mutation.data.length === 0 && (
        <p className="small muted">Aucune playlist publique trouvée pour cette chaîne.</p>
      )}

      {mutation.data && mutation.data.length > 0 && (
        <ul className="stack" style={{ listStyle: 'none', padding: 0 }}>
          {mutation.data.map((playlist) => (
            <li key={playlist.id} className="row-between">
              <span className="truncate">
                {playlist.name} <span className="muted small">— {playlist.trackCount} morceaux</span>
              </span>
              <button type="button" className="btn btn-sm" onClick={() => onSelect(playlist)}>
                Choisir
              </button>
            </li>
          ))}
        </ul>
      )}
    </form>
  );
}

/** Présente le contenu de la playlist et les options avant de lancer l'import. */
function PreviewPanel({
  preview,
  url,
  onCancel,
  onStarted,
}: {
  preview: PlaylistPreview;
  url: string;
  onCancel: () => void;
  onStarted: (importId: string) => void;
}) {
  const queryClient = useQueryClient();

  // « Non répertorié » est le défaut : un morceau privé n'est pas diffusable dans le
  // navigateur, l'élément <audio> ne pouvant pas présenter de jeton d'authentification.
  const [visibility, setVisibility] = useState<ContentVisibility>('Unlisted');
  const [createPlaylist, setCreatePlaylist] = useState(true);

  const startMutation = useMutation({
    mutationFn: () => importsApi.start({ url, visibility, createPlaylist }),
    onSuccess: (created) => {
      queryClient.invalidateQueries({ queryKey: ['playlist-imports'] });
      onStarted(created.id);
    },
  });

  return (
    <div className="card" style={{ maxWidth: 900 }}>
      <ErrorMessage error={startMutation.error} />

      <div className="row-between wrap">
        <div>
          <h2 style={{ margin: 0 }}>{preview.playlist.name}</h2>
          <p className="small muted" style={{ margin: 0 }}>
            {preview.playlist.owner ? `${preview.playlist.owner} · ` : ''}
            {preview.tracks.length} morceaux
          </p>
        </div>
        <button type="button" className="btn" onClick={onCancel}>
          Changer de playlist
        </button>
      </div>

      <div className="row wrap">
        <div className="field grow">
          <label htmlFor="import-visibility">Visibilité des morceaux importés</label>
          <select
            id="import-visibility"
            value={visibility}
            onChange={(event) => setVisibility(event.target.value as ContentVisibility)}
            aria-describedby="import-visibility-help"
          >
            <option value="Unlisted">Non répertorié (accessible par lien)</option>
            <option value="Public">Public</option>
            <option value="Private">Privé (non lisible dans le navigateur)</option>
          </select>
          <span id="import-visibility-help" className="small muted">
            Un morceau privé n'est pas diffusable depuis le navigateur : le lecteur audio ne peut pas
            présenter de jeton d'authentification au serveur. « Non répertorié » le rend écoutable sans
            l'exposer dans les recherches ni sur la page d'accueil.
          </span>
        </div>
      </div>

      <div className="checkbox">
        <input
          id="import-create-playlist"
          type="checkbox"
          checked={createPlaylist}
          onChange={(event) => setCreatePlaylist(event.target.checked)}
        />
        <label htmlFor="import-create-playlist">Créer la playlist correspondante dans ma bibliothèque</label>
      </div>

      <p className="small muted">
        Les morceaux déjà présents dans votre bibliothèque seront rattachés sans être retéléchargés.
        Assurez-vous de disposer des droits nécessaires sur les contenus que vous importez.
      </p>

      <div className="table-wrapper" style={{ maxHeight: 360, overflowY: 'auto' }}>
        <table>
          <thead>
            <tr>
              <th scope="col">#</th>
              <th scope="col">Titre</th>
              <th scope="col">Chaîne</th>
              <th scope="col">Durée</th>
            </tr>
          </thead>
          <tbody>
            {preview.tracks.map((track, index) => (
              <tr key={`${track.sourceId}-${index}`}>
                <td className="muted">{index + 1}</td>
                <td>{track.title}</td>
                <td className="muted">{track.artistName}</td>
                <td className="muted">{track.durationSeconds > 0 ? formatDuration(track.durationSeconds) : '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="row" style={{ marginTop: 16 }}>
        <button
          type="button"
          className="btn btn-primary"
          disabled={startMutation.isPending}
          onClick={() => startMutation.mutate()}
        >
          {startMutation.isPending ? 'Démarrage…' : `Importer ${preview.tracks.length} morceaux`}
        </button>
        <button type="button" className="btn" onClick={onCancel} disabled={startMutation.isPending}>
          Annuler
        </button>
      </div>
    </div>
  );
}

/**
 * Suit la progression d'un import en cours.
 * L'interrogation s'arrête dès que l'import atteint un état terminal.
 */
function ImportProgress({ importId, onClose }: { importId: string; onClose: () => void }) {
  const queryClient = useQueryClient();

  const { data, error, isLoading } = useQuery({
    queryKey: ['playlist-import', importId],
    queryFn: () => importsApi.get(importId),
    refetchInterval: (query) => {
      const status = query.state.data?.import.status;
      return status === 'Pending' || status === 'Running' ? POLL_INTERVAL_MS : false;
    },
  });

  const cancelMutation = useMutation({
    mutationFn: () => importsApi.cancel(importId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['playlist-import', importId] }),
  });

  const retryMutation = useMutation({
    mutationFn: () => importsApi.retry(importId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['playlist-import', importId] }),
  });

  if (error) {
    return <ErrorMessage error={error} />;
  }

  if (isLoading || !data) {
    return <Loading rows={4} />;
  }

  const { import: current, items } = data;
  const { progress } = current;
  const percent = progress.total === 0 ? 0 : Math.round((progress.processed / progress.total) * 100);
  const isRunning = current.status === 'Pending' || current.status === 'Running';
  const retryable = progress.failed + progress.cancelled;

  return (
    <>
      <div className="row-between wrap">
        <h1 style={{ marginBottom: 0 }}>{current.name}</h1>
        <button type="button" className="btn" onClick={onClose}>
          Nouvel import
        </button>
      </div>

      <div className="card">
        <ErrorMessage error={cancelMutation.error ?? retryMutation.error} />

        <div className="row-between">
          <strong>
            {progress.processed} / {progress.total}
          </strong>
          <span className="muted small">{statusLabel(current.status)}</span>
        </div>

        <div
          className="progress-bar"
          role="progressbar"
          aria-valuenow={percent}
          aria-valuemin={0}
          aria-valuemax={100}
          style={{ marginTop: 8 }}
        >
          <div style={{ width: `${percent}%` }} />
        </div>

        <div className="row wrap" style={{ marginTop: 12 }}>
          <span className="badge badge-success">{progress.imported} importés</span>
          <span className="badge badge-accent">{progress.duplicate} déjà présents</span>
          <span className="badge badge-danger">{progress.failed} en échec</span>
          {progress.cancelled > 0 && <span className="badge">{progress.cancelled} annulés</span>}
        </div>

        {current.failureReason && (
          <div className="alert alert-error" role="alert">
            {current.failureReason}
          </div>
        )}

        <div className="row wrap" style={{ marginTop: 16 }}>
          {isRunning && (
            <button
              type="button"
              className="btn btn-danger"
              disabled={cancelMutation.isPending}
              onClick={() => cancelMutation.mutate()}
            >
              Annuler l'import
            </button>
          )}

          {!isRunning && retryable > 0 && (
            <button
              type="button"
              className="btn"
              disabled={retryMutation.isPending}
              onClick={() => retryMutation.mutate()}
            >
              Relancer les {retryable} morceaux non importés
            </button>
          )}

          {current.playlistId && (
            <Link className="btn" to={`/playlists/${current.playlistId}`}>
              Ouvrir la playlist
            </Link>
          )}
        </div>
      </div>

      <div className="card table-wrapper">
        <table>
          <thead>
            <tr>
              <th scope="col">#</th>
              <th scope="col">Titre</th>
              <th scope="col">Chaîne</th>
              <th scope="col">Durée</th>
              <th scope="col">État</th>
              <th scope="col">Détail</th>
            </tr>
          </thead>
          <tbody>
            {items.map((item) => (
              <tr key={item.id}>
                <td className="muted">{item.position + 1}</td>
                <td>{item.trackId ? <Link to={`/tracks/${item.trackId}`}>{item.title}</Link> : item.title}</td>
                <td className="muted">{item.artistName}</td>
                <td className="muted">
                  {item.durationSeconds > 0 ? formatDuration(item.durationSeconds) : '—'}
                </td>
                <td>
                  <span className={ITEM_STATUS[item.status].className}>{ITEM_STATUS[item.status].label}</span>
                </td>
                <td className="muted small">{item.failureReason ?? ''}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}

/** Rappelle les imports précédents et permet d'en rouvrir le détail. */
function RecentImports({ onOpen }: { onOpen: (importId: string) => void }) {
  const { data } = useQuery({ queryKey: ['playlist-imports'], queryFn: importsApi.list });

  if (!data || data.length === 0) {
    return null;
  }

  return (
    <div className="card" style={{ maxWidth: 720, marginTop: 24 }}>
      <h2>Imports récents</h2>
      <ul className="stack" style={{ listStyle: 'none', padding: 0 }}>
        {data.map((entry) => (
          <li key={entry.id} className="row-between">
            <span className="truncate">
              {entry.name}{' '}
              <span className="muted small">
                — {entry.progress.processed}/{entry.progress.total} · {statusLabel(entry.status)}
              </span>
            </span>
            <button type="button" className="btn btn-sm" onClick={() => onOpen(entry.id)}>
              Voir
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}

/** Libellé français de l'état global d'un import. */
function statusLabel(status: string): string {
  switch (status) {
    case 'Pending':
      return 'En attente';
    case 'Running':
      return 'En cours';
    case 'Completed':
      return 'Terminé';
    case 'Failed':
      return 'Échec';
    case 'Cancelled':
      return 'Annulé';
    default:
      return status;
  }
}
