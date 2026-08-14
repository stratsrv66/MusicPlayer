import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { mediaUrl } from '../services/apiClient';
import { usePlayerStore, useCurrentTrack } from '../features/player/playerStore';
import { formatDuration, formatNumber } from '../lib/format';
import type { Track } from '../types/api';
import { LikeButton } from './LikeButton';
import { PauseIcon, PlayIcon } from './Icons';

interface TrackRowProps {
  track: Track;
  index: number;
  queue: Track[];
  /** Actions supplémentaires affichées en fin de ligne. */
  actions?: ReactNode;
  /** Poignée de glisser-déposer, fournie par la playlist éditable. */
  handle?: ReactNode;
}

/** Ligne d'un morceau dans une liste. */
export function TrackRow({ track, index, queue, actions, handle }: TrackRowProps) {
  const current = useCurrentTrack();
  const isPlaying = usePlayerStore((state) => state.isPlaying);
  const playQueue = usePlayerStore((state) => state.playQueue);
  const toggle = usePlayerStore((state) => state.toggle);

  const isCurrent = current?.id === track.id;
  const isActive = isCurrent && isPlaying;
  const thumb = mediaUrl(track.coverUrls.small);

  function handlePlay() {
    if (isCurrent) {
      toggle();
      return;
    }
    playQueue(queue, queue.findIndex((item) => item.id === track.id));
  }

  return (
    <div className={`track-row${isCurrent ? ' is-current' : ''}`}>
      {handle ?? <span className="index">{index + 1}</span>}

      <button
        type="button"
        className="icon-btn"
        onClick={handlePlay}
        aria-label={isActive ? `Mettre en pause ${track.title}` : `Lire ${track.title}`}
      >
        {isActive ? <PauseIcon size={18} /> : <PlayIcon size={18} />}
      </button>

      <div className="row grow" style={{ minWidth: 0 }}>
        {thumb && <img className="thumb" src={thumb} alt="" loading="lazy" />}
        <div className="meta grow">
          <Link to={`/tracks/${track.id}`} className="truncate" style={{ fontWeight: 550, display: 'block' }}>
            {track.title}
          </Link>
          <Link to={`/users/${track.owner.username}`} className="truncate small muted" style={{ display: 'block' }}>
            {track.artistName}
          </Link>
        </div>
      </div>

      <span className="duration small">
        {track.playCount !== null && track.playCount !== undefined && (
          <span className="muted" style={{ marginRight: 12 }}>{formatNumber(track.playCount)}</span>
        )}
        {formatDuration(track.durationSeconds)}
      </span>

      <span className="row">
        <LikeButton track={track} />
        {actions}
      </span>
    </div>
  );
}

/** Liste verticale de morceaux. */
export function TrackList({ tracks, emptyLabel = 'Aucun morceau.' }: { tracks: Track[]; emptyLabel?: string }) {
  if (tracks.length === 0) {
    return <p className="empty">{emptyLabel}</p>;
  }

  return (
    <div className="track-list">
      {tracks.map((track, index) => (
        <TrackRow key={track.id} track={track} index={index} queue={tracks} />
      ))}
    </div>
  );
}
