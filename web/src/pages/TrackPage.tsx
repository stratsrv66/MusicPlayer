import { useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useParams } from 'react-router-dom';
import { commentsApi, playlistsApi, tracksApi } from '../services/api';
import { mediaUrl } from '../services/apiClient';
import { useAuthStore } from '../features/auth/authStore';
import { usePlayerStore, useCurrentTrack } from '../features/player/playerStore';
import { LikeButton } from '../components/LikeButton';
import { Dialog, Empty, ErrorMessage, Loading, Pagination, ReportDialog } from '../components/common';
import { MusicIcon, PauseIcon, PlayIcon, PlusIcon, QueueIcon, TrashIcon } from '../components/Icons';
import { formatDate, formatDuration, formatNumber, formatRelative } from '../lib/format';
import type { Comment } from '../types/api';

/** Page de détail d'un morceau : lecture, métadonnées, tags et commentaires. */
export function TrackPage() {
  const { trackId = '' } = useParams();
  const queryClient = useQueryClient();
  const me = useAuthStore((state) => state.me);

  const current = useCurrentTrack();
  const isPlaying = usePlayerStore((state) => state.isPlaying);
  const playTrack = usePlayerStore((state) => state.playTrack);
  const toggle = usePlayerStore((state) => state.toggle);
  const enqueue = usePlayerStore((state) => state.enqueue);
  const playNext = usePlayerStore((state) => state.playNext);
  const seek = usePlayerStore((state) => state.seek);

  const [addToPlaylistOpen, setAddToPlaylistOpen] = useState(false);

  const { data, isLoading, error } = useQuery({
    queryKey: ['track', trackId],
    queryFn: () => tracksApi.get(trackId),
  });

  if (isLoading) {
    return <Loading rows={4} />;
  }

  if (error) {
    return <ErrorMessage error={error} />;
  }

  if (!data) {
    return null;
  }

  const { track } = data;
  const cover = mediaUrl(track.coverUrls.large);
  const isCurrent = current?.id === track.id;
  const isOwner = me?.profile.id === track.owner.id;

  /**
   * Positionne la lecture sur un instant du morceau.
   * Si le morceau n'est pas chargé, il est d'abord placé dans le lecteur.
   */
  function seekTo(seconds: number) {
    if (!isCurrent) {
      playTrack(track);
    }
    seek(seconds);
  }

  return (
    <>
      <div className="row wrap" style={{ alignItems: 'flex-start', gap: 24, marginBottom: 32 }}>
        <div style={{ width: 260, maxWidth: '100%' }}>
          <div className="cover">
            {cover ? (
              <img src={cover} alt={`Pochette de ${track.title}`} />
            ) : (
              <div className="cover-placeholder">
                <MusicIcon size={48} />
              </div>
            )}
          </div>
        </div>

        <div className="grow" style={{ minWidth: 260 }}>
          <div className="row wrap" style={{ gap: 8, marginBottom: 8 }}>
            <span className="badge">{track.visibility === 'Public' ? 'Public' : track.visibility === 'Unlisted' ? 'Non répertorié' : 'Privé'}</span>
            {track.status !== 'Ready' && <span className="badge badge-warning">{track.status}</span>}
            {data.isHidden && <span className="badge badge-danger">Masqué par la modération</span>}
          </div>

          <h1 style={{ marginBottom: 4 }}>{track.title}</h1>
          <p className="muted">
            <Link to={`/users/${track.owner.username}`}>{track.artistName}</Link>
            {' · '}
            {formatDate(track.createdAt)}
            {' · '}
            {formatDuration(track.durationSeconds)}
          </p>

          {data.failureReason && (
            <div className="alert alert-error">Le traitement a échoué : {data.failureReason}</div>
          )}

          <div className="row wrap" style={{ gap: 8, marginBottom: 16 }}>
            <button
              type="button"
              className="btn btn-primary"
              onClick={() => (isCurrent ? toggle() : playTrack(track))}
              disabled={track.status !== 'Ready'}
            >
              {isCurrent && isPlaying ? <PauseIcon size={18} /> : <PlayIcon size={18} />}
              {isCurrent && isPlaying ? 'Pause' : 'Lire'}
            </button>

            <LikeButton track={track} showCount />

            <button type="button" className="btn" onClick={() => playNext(track)} disabled={track.status !== 'Ready'}>
              <QueueIcon size={16} /> Lire ensuite
            </button>

            <button type="button" className="btn" onClick={() => enqueue(track)} disabled={track.status !== 'Ready'}>
              <PlusIcon size={16} /> Ajouter à la file
            </button>

            {me && (
              <button type="button" className="btn" onClick={() => setAddToPlaylistOpen(true)}>
                Ajouter à une playlist
              </button>
            )}

            {isOwner && (
              <Link to={`/tracks/${track.id}/edit`} className="btn">
                Modifier
              </Link>
            )}

            <ReportDialog targetType="Track" targetId={track.id} targetLabel={track.title} />
          </div>

          <dl className="row wrap" style={{ gap: 24, marginBottom: 16 }}>
            {track.playCount !== null && track.playCount !== undefined && (
              <div>
                <dt className="small muted">Écoutes</dt>
                <dd style={{ margin: 0, fontWeight: 600 }}>{formatNumber(track.playCount)}</dd>
              </div>
            )}
            {track.likeCount !== null && track.likeCount !== undefined && (
              <div>
                <dt className="small muted">Likes</dt>
                <dd style={{ margin: 0, fontWeight: 600 }}>{formatNumber(track.likeCount)}</dd>
              </div>
            )}
            <div>
              <dt className="small muted">Commentaires</dt>
              <dd style={{ margin: 0, fontWeight: 600 }}>{formatNumber(data.commentCount)}</dd>
            </div>
            {track.genre && (
              <div>
                <dt className="small muted">Genre</dt>
                <dd style={{ margin: 0, fontWeight: 600 }}>{track.genre.name}</dd>
              </div>
            )}
            {data.year && (
              <div>
                <dt className="small muted">Année</dt>
                <dd style={{ margin: 0, fontWeight: 600 }}>{data.year}</dd>
              </div>
            )}
          </dl>

          {data.description && <p style={{ whiteSpace: 'pre-wrap' }}>{data.description}</p>}

          {track.tags.length > 0 && (
            <div className="row wrap" style={{ gap: 6 }}>
              {track.tags.map((tag) => (
                <Link key={tag} to={`/tags/${tag}`} className="tag-chip">
                  #{tag}
                </Link>
              ))}
            </div>
          )}
        </div>
      </div>

      <CommentSection trackId={track.id} onSeek={seekTo} />

      {addToPlaylistOpen && (
        <AddToPlaylistDialog
          trackId={track.id}
          onClose={() => setAddToPlaylistOpen(false)}
          onAdded={() => queryClient.invalidateQueries({ queryKey: ['my-playlists'] })}
        />
      )}
    </>
  );
}

/** Fil de commentaires, avec saisie du timestamp courant. */
function CommentSection({ trackId, onSeek }: { trackId: string; onSeek: (seconds: number) => void }) {
  const me = useAuthStore((state) => state.me);
  const position = usePlayerStore((state) => state.position);
  const current = useCurrentTrack();
  const queryClient = useQueryClient();

  const [page, setPage] = useState(1);
  const [content, setContent] = useState('');
  const [attachTimestamp, setAttachTimestamp] = useState(true);

  const { data, isLoading, error } = useQuery({
    queryKey: ['comments', trackId, page],
    queryFn: () => tracksApi.comments(trackId, { page, pageSize: 20 }),
  });

  const createMutation = useMutation({
    mutationFn: (timestamp: number | null) => tracksApi.addComment(trackId, content, timestamp),
    onSuccess: () => {
      setContent('');
      queryClient.invalidateQueries({ queryKey: ['comments', trackId] });
      queryClient.invalidateQueries({ queryKey: ['track', trackId] });
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (commentId: string) => commentsApi.remove(commentId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['comments', trackId] });
      queryClient.invalidateQueries({ queryKey: ['track', trackId] });
    },
  });

  // Le timestamp n'a de sens que si le morceau affiché est celui en cours de lecture.
  const canAttachTimestamp = current?.id === trackId && position > 0;

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    if (!content.trim()) {
      return;
    }
    createMutation.mutate(canAttachTimestamp && attachTimestamp ? Math.floor(position) : null);
  }

  return (
    <section className="section">
      <h2>Commentaires</h2>

      {me ? (
        <form onSubmit={handleSubmit} className="card" style={{ marginBottom: 16 }}>
          <div className="field">
            <label htmlFor="comment-content">Votre commentaire</label>
            <textarea
              id="comment-content"
              value={content}
              maxLength={2000}
              onChange={(event) => setContent(event.target.value)}
              placeholder="Partagez votre avis…"
            />
          </div>

          {canAttachTimestamp && (
            <div className="field checkbox">
              <input
                id="attach-timestamp"
                type="checkbox"
                checked={attachTimestamp}
                onChange={(event) => setAttachTimestamp(event.target.checked)}
              />
              <label htmlFor="attach-timestamp">Associer à la position {formatDuration(position)}</label>
            </div>
          )}

          <ErrorMessage error={createMutation.error} />

          <button type="submit" className="btn btn-primary" disabled={createMutation.isPending || !content.trim()}>
            Publier
          </button>
        </form>
      ) : (
        <p className="muted small">
          <Link to="/login">Connectez-vous</Link> pour commenter ce morceau.
        </p>
      )}

      <ErrorMessage error={error} />

      {isLoading ? (
        <Loading rows={2} />
      ) : data && data.items.length > 0 ? (
        <>
          <div>
            {data.items.map((comment) => (
              <CommentItem
                key={comment.id}
                comment={comment}
                onSeek={onSeek}
                onDelete={() => deleteMutation.mutate(comment.id)}
              />
            ))}
          </div>
          <Pagination page={page} totalPages={data.totalPages} onChange={setPage} />
        </>
      ) : (
        <Empty>Aucun commentaire pour le moment.</Empty>
      )}
    </section>
  );
}

/** Un commentaire, avec lien de positionnement si un timestamp est présent. */
function CommentItem({
  comment,
  onSeek,
  onDelete,
}: {
  comment: Comment;
  onSeek: (seconds: number) => void;
  onDelete: () => void;
}) {
  const avatar = mediaUrl(comment.author.avatarUrl);

  return (
    <article className="comment">
      {avatar ? <img className="avatar" src={avatar} alt="" /> : <span className="avatar" aria-hidden="true" />}

      <div className="grow" style={{ minWidth: 0 }}>
        <div className="row" style={{ gap: 8 }}>
          <Link to={`/users/${comment.author.username}`} style={{ fontWeight: 600 }}>
            {comment.author.username}
          </Link>

          {comment.timestampSeconds !== null && comment.timestampSeconds !== undefined && (
            <button
              type="button"
              className="timestamp-link"
              onClick={() => onSeek(comment.timestampSeconds!)}
              aria-label={`Écouter à ${formatDuration(comment.timestampSeconds)}`}
            >
              {formatDuration(comment.timestampSeconds)}
            </button>
          )}

          <span className="small muted">{formatRelative(comment.createdAt)}</span>

          <span className="grow" />

          <ReportDialog targetType="Comment" targetId={comment.id} targetLabel="ce commentaire" />

          {comment.canDelete && (
            <button type="button" className="icon-btn" onClick={onDelete} aria-label="Supprimer le commentaire">
              <TrashIcon size={16} />
            </button>
          )}
        </div>

        <p style={{ margin: '4px 0 0', whiteSpace: 'pre-wrap' }}>{comment.content}</p>
      </div>
    </article>
  );
}

/** Sélection d'une playlist de l'utilisateur pour y ajouter le morceau. */
function AddToPlaylistDialog({
  trackId,
  onClose,
  onAdded,
}: {
  trackId: string;
  onClose: () => void;
  onAdded: () => void;
}) {
  const { data, isLoading } = useQuery({
    queryKey: ['my-playlists'],
    queryFn: () => playlistsApi.list({ pageSize: 100 }),
  });

  const me = useAuthStore((state) => state.me);

  const mutation = useMutation({
    mutationFn: (playlistId: string) => playlistsApi.addTrack(playlistId, trackId),
    onSuccess: () => {
      onAdded();
      onClose();
    },
  });

  const owned = data?.items.filter((playlist) => playlist.owner.id === me?.profile.id) ?? [];

  return (
    <Dialog title="Ajouter à une playlist" onClose={onClose}>
      <ErrorMessage error={mutation.error} />

      {isLoading ? (
        <Loading rows={2} />
      ) : owned.length === 0 ? (
        <p className="muted">
          Vous n'avez pas encore de playlist. <Link to="/me/playlists">En créer une.</Link>
        </p>
      ) : (
        <div className="stack">
          {owned.map((playlist) => (
            <button
              key={playlist.id}
              type="button"
              className="btn"
              style={{ justifyContent: 'space-between' }}
              onClick={() => mutation.mutate(playlist.id)}
              disabled={mutation.isPending}
            >
              <span className="truncate">{playlist.name}</span>
              <span className="small muted">{playlist.trackCount} morceaux</span>
            </button>
          ))}
        </div>
      )}
    </Dialog>
  );
}
