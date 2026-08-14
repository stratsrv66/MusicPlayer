import { Link } from 'react-router-dom';
import { mediaUrl } from '../services/apiClient';
import { usePlayerStore, useCurrentTrack } from '../features/player/playerStore';
import { formatDuration, formatNumber } from '../lib/format';
import type { Track } from '../types/api';
import { MusicIcon, PauseIcon, PlayIcon } from './Icons';

interface TrackCardProps {
  track: Track;
  /** File dans laquelle le morceau s'inscrit, pour permettre l'enchaînement. */
  queue?: Track[];
}

/** Vignette d'un morceau, avec bouton de lecture superposé sur la pochette. */
export function TrackCard({ track, queue }: TrackCardProps) {
  const current = useCurrentTrack();
  const isPlaying = usePlayerStore((state) => state.isPlaying);
  const playTrack = usePlayerStore((state) => state.playTrack);
  const toggle = usePlayerStore((state) => state.toggle);

  const isCurrent = current?.id === track.id;
  const isActive = isCurrent && isPlaying;
  const cover = mediaUrl(track.coverUrls.medium);

  function handlePlay() {
    if (isCurrent) {
      toggle();
      return;
    }
    playTrack(track, queue);
  }

  return (
    <article className="track-card">
      <div className="cover">
        {cover ? (
          <img src={cover} alt="" loading="lazy" onError={(event) => event.currentTarget.remove()} />
        ) : (
          <div className="cover-placeholder">
            <MusicIcon size={32} />
          </div>
        )}
        <button
          type="button"
          className="play-overlay"
          onClick={handlePlay}
          aria-label={isActive ? `Mettre en pause ${track.title}` : `Lire ${track.title}`}
        >
          {isActive ? <PauseIcon size={20} /> : <PlayIcon size={20} />}
        </button>
      </div>

      <Link to={`/tracks/${track.id}`} className="truncate" style={{ fontWeight: 600, display: 'block' }}>
        {track.title}
      </Link>
      <Link to={`/users/${track.owner.username}`} className="truncate small muted" style={{ display: 'block' }}>
        {track.artistName}
      </Link>

      <p className="small muted" style={{ marginTop: 4, marginBottom: 0 }}>
        {formatDuration(track.durationSeconds)}
        {track.playCount !== null && track.playCount !== undefined && ` · ${formatNumber(track.playCount)} écoutes`}
      </p>
    </article>
  );
}

/** Grille responsive de vignettes. */
export function TrackGrid({ tracks }: { tracks: Track[] }) {
  if (tracks.length === 0) {
    return <p className="empty">Aucun morceau à afficher.</p>;
  }

  return (
    <div className="grid">
      {tracks.map((track) => (
        <TrackCard key={track.id} track={track} queue={tracks} />
      ))}
    </div>
  );
}
