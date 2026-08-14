import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useParams } from 'react-router-dom';
import {
  DndContext,
  KeyboardSensor,
  PointerSensor,
  closestCenter,
  useSensor,
  useSensors,
  type DragEndEvent,
} from '@dnd-kit/core';
import { restrictToVerticalAxis } from '@dnd-kit/modifiers';
import { SortableContext, arrayMove, sortableKeyboardCoordinates, useSortable, verticalListSortingStrategy } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { playlistsApi } from '../services/api';
import { mediaUrl } from '../services/apiClient';
import { useAuthStore } from '../features/auth/authStore';
import { usePlayerStore } from '../features/player/playerStore';
import { TrackRow } from '../components/TrackList';
import { Dialog, Empty, ErrorMessage, Loading, ReportDialog } from '../components/common';
import { HeartIcon, MusicIcon, PlayIcon, TrashIcon } from '../components/Icons';
import { formatDuration, formatNumber } from '../lib/format';
import type { ContentVisibility, PlaylistTrack } from '../types/api';

/** Ligne de playlist déplaçable au clavier comme à la souris. */
function SortableTrackRow({
  item,
  index,
  queue,
  onRemove,
}: {
  item: PlaylistTrack;
  index: number;
  queue: PlaylistTrack[];
  onRemove: () => void;
}) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({ id: item.track.id });

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
  };

  return (
    <div ref={setNodeRef} style={style} className={`sortable-item${isDragging ? ' dragging' : ''}`}>
      <TrackRow
        track={item.track}
        index={index}
        queue={queue.map((entry) => entry.track)}
        handle={
          <button
            type="button"
            className="drag-handle"
            aria-label={`Déplacer ${item.track.title}. Utilisez les flèches pour changer sa position.`}
            {...attributes}
            {...listeners}
          >
            ⠿
          </button>
        }
        actions={
          <button
            type="button"
            className="icon-btn"
            onClick={onRemove}
            aria-label={`Retirer ${item.track.title} de la playlist`}
          >
            <TrashIcon size={16} />
          </button>
        }
      />
    </div>
  );
}

/** Page de détail d'une playlist, éditable par son propriétaire. */
export function PlaylistPage() {
  const { playlistId = '' } = useParams();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const me = useAuthStore((state) => state.me);
  const playQueue = usePlayerStore((state) => state.playQueue);

  const [items, setItems] = useState<PlaylistTrack[]>([]);
  const [editOpen, setEditOpen] = useState(false);

  const { data, isLoading, error } = useQuery({
    queryKey: ['playlist', playlistId],
    queryFn: () => playlistsApi.get(playlistId),
  });

  // La liste locale sert de source pendant le glisser-déposer, pour un rendu immédiat.
  useEffect(() => {
    if (data) {
      setItems(data.tracks);
    }
  }, [data]);

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 5 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );

  const reorderMutation = useMutation({
    mutationFn: (ordered: PlaylistTrack[]) =>
      playlistsApi.reorder(
        playlistId,
        ordered.map((entry, position) => ({ trackId: entry.track.id, position })),
      ),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['playlist', playlistId] }),
    onError: () => queryClient.invalidateQueries({ queryKey: ['playlist', playlistId] }),
  });

  const removeMutation = useMutation({
    mutationFn: (trackId: string) => playlistsApi.removeTrack(playlistId, trackId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['playlist', playlistId] }),
  });

  const favoriteMutation = useMutation({
    mutationFn: (favorited: boolean) =>
      favorited ? playlistsApi.unfavorite(playlistId) : playlistsApi.favorite(playlistId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['playlist', playlistId] }),
  });

  const followMutation = useMutation({
    mutationFn: (following: boolean) =>
      following ? playlistsApi.unfollow(playlistId) : playlistsApi.follow(playlistId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['playlist', playlistId] }),
  });

  const duplicateMutation = useMutation({
    mutationFn: () => playlistsApi.duplicate(playlistId),
    onSuccess: (copy) => {
      queryClient.invalidateQueries({ queryKey: ['my-playlists'] });
      navigate(`/playlists/${copy.id}`);
    },
  });

  function handleDragEnd(event: DragEndEvent) {
    const { active, over } = event;
    if (!over || active.id === over.id) {
      return;
    }

    const from = items.findIndex((entry) => entry.track.id === active.id);
    const to = items.findIndex((entry) => entry.track.id === over.id);
    if (from < 0 || to < 0) {
      return;
    }

    const reordered = arrayMove(items, from, to);
    setItems(reordered);
    reorderMutation.mutate(reordered);
  }

  if (isLoading) {
    return <Loading rows={4} />;
  }

  if (error) {
    return <ErrorMessage error={error} />;
  }

  if (!data) {
    return null;
  }

  const { playlist, canEdit } = data;
  const cover = mediaUrl(playlist.coverUrl);
  const isOwner = me?.profile.id === playlist.owner.id;

  return (
    <>
      <div className="row wrap" style={{ alignItems: 'flex-start', gap: 24, marginBottom: 32 }}>
        <div style={{ width: 220, maxWidth: '100%' }}>
          <div className="cover">
            {cover ? (
              <img src={cover} alt={`Pochette de ${playlist.name}`} />
            ) : (
              <div className="cover-placeholder">
                <MusicIcon size={44} />
              </div>
            )}
          </div>
        </div>

        <div className="grow" style={{ minWidth: 260 }}>
          <span className="badge">
            {playlist.visibility === 'Public' ? 'Publique' : playlist.visibility === 'Unlisted' ? 'Non répertoriée' : 'Privée'}
          </span>

          <h1 style={{ marginTop: 8, marginBottom: 4 }}>{playlist.name}</h1>
          <p className="muted">
            <Link to={`/users/${playlist.owner.username}`}>{playlist.owner.username}</Link>
            {' · '}
            {playlist.trackCount} morceaux
            {' · '}
            {formatDuration(playlist.totalDurationSeconds)}
            {' · '}
            {formatNumber(playlist.followerCount)} abonnés
          </p>

          {playlist.description && <p>{playlist.description}</p>}

          <div className="row wrap" style={{ gap: 8 }}>
            <button
              type="button"
              className="btn btn-primary"
              disabled={items.length === 0}
              onClick={() => playQueue(items.map((entry) => entry.track), 0)}
            >
              <PlayIcon size={18} /> Tout lire
            </button>

            {me && !isOwner && (
              <>
                <button
                  type="button"
                  className="btn"
                  onClick={() => favoriteMutation.mutate(Boolean(playlist.isFavoritedByCurrentUser))}
                  aria-pressed={Boolean(playlist.isFavoritedByCurrentUser)}
                >
                  <HeartIcon size={16} filled={Boolean(playlist.isFavoritedByCurrentUser)} />
                  {playlist.isFavoritedByCurrentUser ? 'Retirer des favoris' : 'Ajouter aux favoris'}
                </button>

                <button
                  type="button"
                  className="btn"
                  onClick={() => followMutation.mutate(Boolean(playlist.isFollowedByCurrentUser))}
                >
                  {playlist.isFollowedByCurrentUser ? 'Ne plus suivre' : 'Suivre'}
                </button>
              </>
            )}

            {me && (
              <button type="button" className="btn" onClick={() => duplicateMutation.mutate()}>
                Dupliquer
              </button>
            )}

            <button
              type="button"
              className="btn"
              onClick={() => {
                void navigator.clipboard?.writeText(window.location.href);
              }}
            >
              Partager
            </button>

            {canEdit && (
              <button type="button" className="btn" onClick={() => setEditOpen(true)}>
                Modifier
              </button>
            )}

            <ReportDialog targetType="Playlist" targetId={playlist.id} targetLabel={playlist.name} />
          </div>
        </div>
      </div>

      <ErrorMessage error={reorderMutation.error} />

      {items.length === 0 ? (
        <Empty>Cette playlist est vide.</Empty>
      ) : canEdit ? (
        <>
          <p className="small muted">
            Glissez les morceaux pour les réordonner, ou utilisez la poignée au clavier.
          </p>
          <DndContext
            sensors={sensors}
            collisionDetection={closestCenter}
            modifiers={[restrictToVerticalAxis]}
            onDragEnd={handleDragEnd}
          >
            <SortableContext items={items.map((entry) => entry.track.id)} strategy={verticalListSortingStrategy}>
              <div className="track-list">
                {items.map((item, index) => (
                  <SortableTrackRow
                    key={item.track.id}
                    item={item}
                    index={index}
                    queue={items}
                    onRemove={() => removeMutation.mutate(item.track.id)}
                  />
                ))}
              </div>
            </SortableContext>
          </DndContext>
        </>
      ) : (
        <div className="track-list">
          {items.map((item, index) => (
            <TrackRow
              key={item.track.id}
              track={item.track}
              index={index}
              queue={items.map((entry) => entry.track)}
            />
          ))}
        </div>
      )}

      {editOpen && <EditPlaylistDialog playlistId={playlistId} onClose={() => setEditOpen(false)} />}
    </>
  );
}

/** Formulaire d'édition d'une playlist, en boîte de dialogue. */
function EditPlaylistDialog({ playlistId, onClose }: { playlistId: string; onClose: () => void }) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { data } = useQuery({ queryKey: ['playlist', playlistId], queryFn: () => playlistsApi.get(playlistId) });

  const [name, setName] = useState(data?.playlist.name ?? '');
  const [description, setDescription] = useState(data?.playlist.description ?? '');
  const [visibility, setVisibility] = useState<ContentVisibility>(data?.playlist.visibility ?? 'Private');

  const updateMutation = useMutation({
    mutationFn: () => playlistsApi.update(playlistId, { name, description, visibility }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['playlist', playlistId] });
      onClose();
    },
  });

  const coverMutation = useMutation({
    mutationFn: (file: File) => playlistsApi.setCover(playlistId, file),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['playlist', playlistId] }),
  });

  const deleteMutation = useMutation({
    mutationFn: () => playlistsApi.remove(playlistId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['my-playlists'] });
      navigate('/me/playlists');
    },
  });

  return (
    <Dialog title="Modifier la playlist" onClose={onClose}>
      <ErrorMessage error={updateMutation.error ?? deleteMutation.error} />

      <div className="field">
        <label htmlFor="playlist-name">Nom</label>
        <input id="playlist-name" value={name} maxLength={120} onChange={(event) => setName(event.target.value)} />
      </div>

      <div className="field">
        <label htmlFor="playlist-description">Description</label>
        <textarea
          id="playlist-description"
          value={description}
          maxLength={2000}
          onChange={(event) => setDescription(event.target.value)}
        />
      </div>

      <div className="field">
        <label htmlFor="playlist-visibility">Visibilité</label>
        <select
          id="playlist-visibility"
          value={visibility}
          onChange={(event) => setVisibility(event.target.value as ContentVisibility)}
        >
          <option value="Public">Publique</option>
          <option value="Unlisted">Non répertoriée</option>
          <option value="Private">Privée</option>
        </select>
      </div>

      <div className="field">
        <label htmlFor="playlist-cover">Pochette</label>
        <input
          id="playlist-cover"
          type="file"
          accept="image/*"
          onChange={(event) => {
            const file = event.target.files?.[0];
            if (file) {
              coverMutation.mutate(file);
            }
          }}
        />
      </div>

      <div className="row-between">
        <button
          type="button"
          className="btn btn-danger"
          onClick={() => {
            if (window.confirm('Supprimer définitivement cette playlist ?')) {
              deleteMutation.mutate();
            }
          }}
        >
          Supprimer
        </button>

        <button type="button" className="btn btn-primary" onClick={() => updateMutation.mutate()} disabled={!name.trim()}>
          Enregistrer
        </button>
      </div>
    </Dialog>
  );
}
