import { useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, Navigate } from 'react-router-dom';
import { meApi, playlistsApi } from '../services/api';
import { useAuthStore } from '../features/auth/authStore';
import { usePlayerStore } from '../features/player/playerStore';
import { TrackList } from '../components/TrackList';
import { Dialog, Empty, ErrorMessage, Loading, Pagination } from '../components/common';
import { PlayIcon, PlusIcon } from '../components/Icons';
import { formatDuration, formatRelative } from '../lib/format';
import { PlaylistCard } from './HomePage';
import { UserList } from './ProfilePage';
import type { ContentVisibility } from '../types/api';

/** Morceaux importés par l'utilisateur, y compris privés et en traitement. */
export function MyTracksPage() {
  const [page, setPage] = useState(1);
  const { data, isLoading, error } = useQuery({
    queryKey: ['my-tracks', page],
    queryFn: () => meApi.tracks({ page, pageSize: 25 }),
  });

  return (
    <>
      <div className="row-between" style={{ marginBottom: 16 }}>
        <h1 style={{ margin: 0 }}>Mes morceaux</h1>
        <Link to="/upload" className="btn btn-primary">
          <PlusIcon size={16} /> Importer
        </Link>
      </div>

      <ErrorMessage error={error} />

      {isLoading ? (
        <Loading />
      ) : (
        <>
          <TrackList tracks={data?.items ?? []} emptyLabel="Vous n'avez pas encore importé de morceau." />
          <Pagination page={page} totalPages={data?.totalPages ?? 0} onChange={setPage} />
        </>
      )}
    </>
  );
}

/** Playlists de l'utilisateur, avec création. */
export function MyPlaylistsPage() {
  const [page, setPage] = useState(1);
  const [createOpen, setCreateOpen] = useState(false);

  const { data, isLoading, error } = useQuery({
    queryKey: ['my-playlists', page],
    queryFn: () => meApi.playlists({ page, pageSize: 24 }),
  });

  return (
    <>
      <div className="row-between" style={{ marginBottom: 16 }}>
        <h1 style={{ margin: 0 }}>Mes playlists</h1>
        <button type="button" className="btn btn-primary" onClick={() => setCreateOpen(true)}>
          <PlusIcon size={16} /> Nouvelle playlist
        </button>
      </div>

      <ErrorMessage error={error} />

      {isLoading ? (
        <Loading />
      ) : data && data.items.length > 0 ? (
        <>
          <div className="grid">
            {data.items.map((playlist) => (
              <PlaylistCard key={playlist.id} playlist={playlist} />
            ))}
          </div>
          <Pagination page={page} totalPages={data.totalPages} onChange={setPage} />
        </>
      ) : (
        <Empty>Vous n'avez pas encore de playlist.</Empty>
      )}

      {createOpen && <CreatePlaylistDialog onClose={() => setCreateOpen(false)} />}
    </>
  );
}

/** Formulaire de création d'une playlist. */
function CreatePlaylistDialog({ onClose }: { onClose: () => void }) {
  const queryClient = useQueryClient();
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [visibility, setVisibility] = useState<ContentVisibility>('Private');

  const mutation = useMutation({
    mutationFn: () => playlistsApi.create({ name, description, visibility }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['my-playlists'] });
      onClose();
    },
  });

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    mutation.mutate();
  }

  return (
    <Dialog title="Nouvelle playlist" onClose={onClose}>
      <form onSubmit={handleSubmit}>
        <ErrorMessage error={mutation.error} />

        <div className="field">
          <label htmlFor="new-playlist-name">Nom</label>
          <input
            id="new-playlist-name"
            value={name}
            required
            maxLength={120}
            onChange={(event) => setName(event.target.value)}
            autoFocus
          />
        </div>

        <div className="field">
          <label htmlFor="new-playlist-description">Description</label>
          <textarea
            id="new-playlist-description"
            value={description}
            maxLength={2000}
            onChange={(event) => setDescription(event.target.value)}
          />
        </div>

        <div className="field">
          <label htmlFor="new-playlist-visibility">Visibilité</label>
          <select
            id="new-playlist-visibility"
            value={visibility}
            onChange={(event) => setVisibility(event.target.value as ContentVisibility)}
          >
            <option value="Private">Privée</option>
            <option value="Unlisted">Non répertoriée</option>
            <option value="Public">Publique</option>
          </select>
        </div>

        <button type="submit" className="btn btn-primary" disabled={mutation.isPending || !name.trim()}>
          Créer
        </button>
      </form>
    </Dialog>
  );
}

/** Morceaux aimés par l'utilisateur. */
export function MyLikesPage() {
  const [page, setPage] = useState(1);
  const playQueue = usePlayerStore((state) => state.playQueue);

  const { data, isLoading, error } = useQuery({
    queryKey: ['likes', page],
    queryFn: () => meApi.likes({ page, pageSize: 25 }),
  });

  return (
    <>
      <div className="row-between" style={{ marginBottom: 16 }}>
        <h1 style={{ margin: 0 }}>Mes likes</h1>
        <button
          type="button"
          className="btn btn-primary"
          disabled={!data || data.items.length === 0}
          onClick={() => playQueue(data!.items, 0)}
        >
          <PlayIcon size={18} /> Tout lire
        </button>
      </div>

      <ErrorMessage error={error} />

      {isLoading ? (
        <Loading />
      ) : (
        <>
          <TrackList tracks={data?.items ?? []} emptyLabel="Vous n'avez encore aimé aucun morceau." />
          <Pagination page={page} totalPages={data?.totalPages ?? 0} onChange={setPage} />
        </>
      )}
    </>
  );
}

/** Historique d'écoute avec reprise à la dernière position. */
export function MyHistoryPage() {
  const [page, setPage] = useState(1);
  const queryClient = useQueryClient();
  const playTrack = usePlayerStore((state) => state.playTrack);
  const seek = usePlayerStore((state) => state.seek);

  const { data, isLoading, error } = useQuery({
    queryKey: ['history', page],
    queryFn: () => meApi.history({ page, pageSize: 25 }),
  });

  const clearMutation = useMutation({
    mutationFn: () => meApi.clearHistory(),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['history'] }),
  });

  return (
    <>
      <div className="row-between" style={{ marginBottom: 16 }}>
        <h1 style={{ margin: 0 }}>Écoutés récemment</h1>
        <button
          type="button"
          className="btn"
          disabled={!data || data.items.length === 0 || clearMutation.isPending}
          onClick={() => {
            if (window.confirm("Effacer tout l'historique d'écoute ?")) {
              clearMutation.mutate();
            }
          }}
        >
          Effacer l'historique
        </button>
      </div>

      <ErrorMessage error={error} />

      {isLoading ? (
        <Loading />
      ) : data && data.items.length > 0 ? (
        <>
          <div className="track-list">
            {data.items.map((entry) => (
              <div key={entry.track.id} className="track-row" style={{ gridTemplateColumns: '44px 1fr auto auto' }}>
                <button
                  type="button"
                  className="icon-btn"
                  onClick={() => {
                    playTrack(
                      entry.track,
                      data.items.map((item) => item.track),
                    );
                    if (entry.lastPositionSeconds > 0) {
                      seek(entry.lastPositionSeconds);
                    }
                  }}
                  aria-label={`Reprendre ${entry.track.title} à ${formatDuration(entry.lastPositionSeconds)}`}
                >
                  <PlayIcon size={18} />
                </button>

                <div className="meta grow" style={{ minWidth: 0 }}>
                  <Link to={`/tracks/${entry.track.id}`} className="truncate" style={{ display: 'block', fontWeight: 550 }}>
                    {entry.track.title}
                  </Link>
                  <span className="truncate small muted" style={{ display: 'block' }}>
                    {entry.track.artistName} · {formatRelative(entry.lastPlayedAt)}
                  </span>
                </div>

                <span className="small muted">
                  {formatDuration(entry.lastPositionSeconds)} / {formatDuration(entry.track.durationSeconds)}
                </span>
              </div>
            ))}
          </div>
          <Pagination page={page} totalPages={data.totalPages} onChange={setPage} />
        </>
      ) : (
        <Empty>Votre historique est vide.</Empty>
      )}
    </>
  );
}

/** Abonnés de l'utilisateur connecté. */
export function MyFollowersPage() {
  const [page, setPage] = useState(1);
  const { data, isLoading } = useQuery({
    queryKey: ['my-followers', page],
    queryFn: () => meApi.followers({ page, pageSize: 30 }),
  });

  return (
    <>
      <h1>Mes abonnés</h1>
      <UserList users={data?.items ?? []} loading={isLoading} emptyLabel="Personne ne vous suit encore." />
      <Pagination page={page} totalPages={data?.totalPages ?? 0} onChange={setPage} />
    </>
  );
}

/** Abonnements de l'utilisateur connecté. */
export function MyFollowingPage() {
  const [page, setPage] = useState(1);
  const { data, isLoading } = useQuery({
    queryKey: ['following', page],
    queryFn: () => meApi.following({ page, pageSize: 30 }),
  });

  return (
    <>
      <h1>Mes abonnements</h1>
      <UserList users={data?.items ?? []} loading={isLoading} emptyLabel="Vous ne suivez encore personne." />
      <Pagination page={page} totalPages={data?.totalPages ?? 0} onChange={setPage} />
    </>
  );
}

/**
 * Renvoie vers le profil public de l'utilisateur connecté.
 * Le profil personnel et le profil public partagent ainsi une seule implémentation.
 */
export function MyProfilePage() {
  const me = useAuthStore((state) => state.me);

  if (!me) {
    return <Loading rows={2} />;
  }

  return <Navigate to={`/users/${me.profile.username}`} replace />;
}
