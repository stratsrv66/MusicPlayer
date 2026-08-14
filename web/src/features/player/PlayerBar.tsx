import { useState } from 'react';
import { Link } from 'react-router-dom';
import { mediaUrl } from '../../services/apiClient';
import { formatDuration } from '../../lib/format';
import {
  ChevronDownIcon,
  MuteIcon,
  NextIcon,
  PauseIcon,
  PlayIcon,
  PrevIcon,
  QueueIcon,
  RepeatIcon,
  RepeatOneIcon,
  ShuffleIcon,
  TrashIcon,
  VolumeIcon,
} from '../../components/Icons';
import { LikeButton } from '../../components/LikeButton';
import { usePlayerStore, useCurrentTrack } from './playerStore';

/** Barre de progression, réutilisée par le mini-lecteur et le plein écran. */
function SeekBar() {
  const position = usePlayerStore((state) => state.position);
  const duration = usePlayerStore((state) => state.duration);
  const seek = usePlayerStore((state) => state.seek);

  const max = duration > 0 ? Math.floor(duration) : 0;

  return (
    <div className="progress-row">
      <span aria-hidden="true">{formatDuration(position)}</span>
      <input
        type="range"
        className="seek"
        min={0}
        max={max}
        step={1}
        value={Math.min(Math.floor(position), max)}
        onChange={(event) => seek(Number(event.target.value))}
        aria-label="Position de lecture"
        aria-valuetext={`${formatDuration(position)} sur ${formatDuration(duration)}`}
        disabled={max === 0}
      />
      <span aria-hidden="true">{formatDuration(duration)}</span>
    </div>
  );
}

/** Boutons de transport : aléatoire, précédent, lecture, suivant, répétition. */
function TransportControls({ size = 20 }: { size?: number }) {
  const isPlaying = usePlayerStore((state) => state.isPlaying);
  const shuffle = usePlayerStore((state) => state.shuffle);
  const repeat = usePlayerStore((state) => state.repeat);
  const hasTrack = usePlayerStore((state) => state.currentIndex >= 0);

  const toggle = usePlayerStore((state) => state.toggle);
  const next = usePlayerStore((state) => state.next);
  const previous = usePlayerStore((state) => state.previous);
  const toggleShuffle = usePlayerStore((state) => state.toggleShuffle);
  const cycleRepeat = usePlayerStore((state) => state.cycleRepeat);

  const repeatLabel =
    repeat === 'off' ? 'Répétition désactivée' : repeat === 'all' ? 'Répéter la file' : 'Répéter le morceau';

  return (
    <div className="player-buttons">
      <button
        type="button"
        className="icon-btn"
        onClick={toggleShuffle}
        aria-pressed={shuffle}
        aria-label="Lecture aléatoire"
        title="Lecture aléatoire"
      >
        <ShuffleIcon size={size - 2} />
      </button>

      <button type="button" className="icon-btn" onClick={previous} disabled={!hasTrack} aria-label="Morceau précédent">
        <PrevIcon size={size} />
      </button>

      <button
        type="button"
        className="play-button"
        onClick={toggle}
        disabled={!hasTrack}
        aria-label={isPlaying ? 'Mettre en pause' : 'Lire'}
      >
        {isPlaying ? <PauseIcon size={size} /> : <PlayIcon size={size} />}
      </button>

      <button type="button" className="icon-btn" onClick={next} disabled={!hasTrack} aria-label="Morceau suivant">
        <NextIcon size={size} />
      </button>

      <button
        type="button"
        className="icon-btn"
        onClick={cycleRepeat}
        aria-pressed={repeat !== 'off'}
        aria-label={repeatLabel}
        title={repeatLabel}
      >
        {repeat === 'one' ? <RepeatOneIcon size={size - 2} /> : <RepeatIcon size={size - 2} />}
      </button>
    </div>
  );
}

/** Réglage du volume et coupure du son. */
function VolumeControl() {
  const volume = usePlayerStore((state) => state.volume);
  const muted = usePlayerStore((state) => state.muted);
  const setVolume = usePlayerStore((state) => state.setVolume);
  const toggleMute = usePlayerStore((state) => state.toggleMute);

  return (
    <div className="row" style={{ gap: 4 }}>
      <button
        type="button"
        className="icon-btn"
        onClick={toggleMute}
        aria-pressed={muted}
        aria-label={muted ? 'Rétablir le son' : 'Couper le son'}
      >
        {muted || volume === 0 ? <MuteIcon size={18} /> : <VolumeIcon size={18} />}
      </button>
      <input
        type="range"
        className="seek volume"
        min={0}
        max={1}
        step={0.01}
        value={muted ? 0 : volume}
        onChange={(event) => setVolume(Number(event.target.value))}
        aria-label="Volume"
        aria-valuetext={`${Math.round((muted ? 0 : volume) * 100)} %`}
      />
    </div>
  );
}

/** Panneau latéral listant la file d'attente. */
function QueuePanel({ onClose }: { onClose: () => void }) {
  const queue = usePlayerStore((state) => state.queue);
  const currentIndex = usePlayerStore((state) => state.currentIndex);
  const playQueue = usePlayerStore((state) => state.playQueue);
  const removeFromQueue = usePlayerStore((state) => state.removeFromQueue);
  const clearQueue = usePlayerStore((state) => state.clearQueue);

  return (
    <aside className="queue-panel" aria-label="File d'attente">
      <div className="row-between" style={{ marginBottom: 16 }}>
        <h2 style={{ margin: 0 }}>File d'attente</h2>
        <div className="row" style={{ gap: 4 }}>
          <button type="button" className="btn btn-sm" onClick={clearQueue} disabled={queue.length === 0}>
            Vider
          </button>
          <button type="button" className="icon-btn" onClick={onClose} aria-label="Fermer la file d'attente">
            <ChevronDownIcon size={18} />
          </button>
        </div>
      </div>

      {queue.length === 0 ? (
        <p className="muted small">La file est vide.</p>
      ) : (
        <ol style={{ listStyle: 'none', margin: 0, padding: 0 }}>
          {queue.map((track, index) => (
            <li
              key={`${track.id}-${index}`}
              className="row-between"
              style={{ padding: '8px 0', borderBottom: '1px solid var(--border)' }}
            >
              <button
                type="button"
                className="btn-ghost grow truncate"
                style={{
                  textAlign: 'left',
                  border: 'none',
                  background: 'none',
                  cursor: 'pointer',
                  color: index === currentIndex ? 'var(--accent)' : 'inherit',
                  font: 'inherit',
                  padding: 0,
                }}
                onClick={() => playQueue(queue, index)}
              >
                <span className="truncate" style={{ display: 'block', fontWeight: 550 }}>
                  {track.title}
                </span>
                <span className="truncate small muted" style={{ display: 'block' }}>
                  {track.artistName}
                </span>
              </button>
              <button
                type="button"
                className="icon-btn"
                onClick={() => removeFromQueue(index)}
                aria-label={`Retirer ${track.title} de la file`}
              >
                <TrashIcon size={16} />
              </button>
            </li>
          ))}
        </ol>
      )}
    </aside>
  );
}

/** Lecteur plein écran, adapté à l'usage mobile. */
function FullScreenPlayer() {
  const track = useCurrentTrack();
  const setExpanded = usePlayerStore((state) => state.setExpanded);

  if (!track) {
    return null;
  }

  const cover = mediaUrl(track.coverUrls.large);

  return (
    <section className="full-player" aria-label="Lecteur plein écran">
      <div className="row-between" style={{ marginBottom: 24 }}>
        <button type="button" className="icon-btn" onClick={() => setExpanded(false)} aria-label="Réduire le lecteur">
          <ChevronDownIcon size={22} />
        </button>
        <span className="small muted">Lecture en cours</span>
        <LikeButton track={track} />
      </div>

      {cover ? <img className="artwork" src={cover} alt={`Pochette de ${track.title}`} /> : <div className="artwork" />}

      <div style={{ textAlign: 'center', marginBottom: 24 }}>
        <h2 style={{ marginBottom: 4 }}>
          <Link to={`/tracks/${track.id}`} onClick={() => setExpanded(false)}>
            {track.title}
          </Link>
        </h2>
        <p className="muted" style={{ margin: 0 }}>
          <Link to={`/users/${track.owner.username}`} onClick={() => setExpanded(false)}>
            {track.artistName}
          </Link>
        </p>
      </div>

      <div className="stack" style={{ alignItems: 'center', gap: 16 }}>
        <SeekBar />
        <TransportControls size={26} />
        <VolumeControl />
      </div>
    </section>
  );
}

/**
 * Lecteur persistant de l'application.
 *
 * Il est rendu une seule fois dans la coquille applicative, en dehors du routeur,
 * de sorte que ni la lecture ni son état ne sont réinitialisés lors des navigations.
 */
export function PlayerBar() {
  const track = useCurrentTrack();
  const expanded = usePlayerStore((state) => state.expanded);
  const setExpanded = usePlayerStore((state) => state.setExpanded);
  const [queueOpen, setQueueOpen] = useState(false);

  if (!track) {
    return null;
  }

  const cover = mediaUrl(track.coverUrls.small);

  return (
    <>
      {expanded && <FullScreenPlayer />}
      {queueOpen && <QueuePanel onClose={() => setQueueOpen(false)} />}

      <div className="mini-player" role="region" aria-label="Lecteur audio">
        <div className="now-playing">
          <button
            type="button"
            className="btn-ghost"
            style={{ border: 'none', background: 'none', padding: 0, cursor: 'pointer' }}
            onClick={() => setExpanded(true)}
            aria-label="Ouvrir le lecteur plein écran"
          >
            {cover ? <img src={cover} alt="" /> : <span className="thumb" />}
          </button>
          <div className="grow" style={{ minWidth: 0 }}>
            <Link to={`/tracks/${track.id}`} className="truncate" style={{ display: 'block', fontWeight: 550 }}>
              {track.title}
            </Link>
            <Link to={`/users/${track.owner.username}`} className="truncate small muted" style={{ display: 'block' }}>
              {track.artistName}
            </Link>
          </div>
          <LikeButton track={track} />
        </div>

        <div className="player-controls">
          <TransportControls />
          <SeekBar />
        </div>

        <div className="player-extras">
          <VolumeControl />
          <button
            type="button"
            className="icon-btn"
            onClick={() => setQueueOpen((open) => !open)}
            aria-pressed={queueOpen}
            aria-label="Afficher la file d'attente"
          >
            <QueueIcon size={18} />
          </button>
        </div>
      </div>
    </>
  );
}
