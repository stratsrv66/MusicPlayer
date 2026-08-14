import { useEffect, useRef } from 'react';
import { mediaUrl } from '../../services/apiClient';
import { tracksApi } from '../../services/api';
import { useAuthStore } from '../auth/authStore';
import { usePlayerStore, useCurrentTrack } from './playerStore';

/**
 * Durée d'écoute, en secondes, à partir de laquelle le serveur est notifié.
 * La règle métier définitive reste appliquée côté backend.
 */
const PLAY_REPORT_THRESHOLD_SECONDS = 10;

/** Intervalle minimal entre deux sauvegardes de position, en millisecondes. */
const PROGRESS_SAVE_INTERVAL_MS = 15000;

/** Identifiant de session de lecture, utilisé pour dédupliquer les écoutes anonymes. */
const SESSION_ID = typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
  ? crypto.randomUUID()
  : Math.random().toString(36).substring(2) + Date.now().toString(36);

/**
 * Pilote l'unique élément `<audio>` de l'application.
 *
 * Le composant est monté une seule fois à la racine et n'affiche rien : il survit donc
 * à toute navigation, ce qui permet à la lecture de continuer d'une page à l'autre.
 * Il synchronise l'élément média avec l'intention décrite par le store, publie l'état
 * vers la Media Session et notifie le serveur des écoutes valides.
 */
export function AudioEngine() {
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const track = useCurrentTrack();
  const isPlaying = usePlayerStore((state) => state.isPlaying);
  const volume = usePlayerStore((state) => state.volume);
  const muted = usePlayerStore((state) => state.muted);
  const seekRequest = usePlayerStore((state) => state.seekRequest);
  const isAuthenticated = useAuthStore((state) => state.me !== null);

  /** Secondes réellement écoutées sur le morceau courant. */
  const listenedRef = useRef(0);
  /** Vrai une fois l'écoute déclarée au serveur, pour ne la déclarer qu'une fois. */
  const reportedRef = useRef(false);
  /** Horodatage de la dernière sauvegarde de position. */
  const lastSavedRef = useRef(0);
  const trackIdRef = useRef<string | null>(null);

  // Charge la source lorsque le morceau change et restaure la position connue.
  useEffect(() => {
    const audio = audioRef.current;
    if (!audio || !track) {
      return;
    }

    if (trackIdRef.current === track.id) {
      return;
    }

    trackIdRef.current = track.id;
    listenedRef.current = 0;
    reportedRef.current = false;
    lastSavedRef.current = 0;

    audio.src = mediaUrl(track.streamUrl) ?? '';
    audio.load();

    if (!isAuthenticated) {
      return;
    }

    let cancelled = false;
    tracksApi
      .progress(track.id)
      .then((progress) => {
        // On ne reprend pas une lecture quasiment terminée, ni un morceau déjà changé.
        const nearlyFinished = progress.positionSeconds >= track.durationSeconds - 5;
        if (!cancelled && progress.positionSeconds > 5 && !nearlyFinished) {
          usePlayerStore.getState().seek(progress.positionSeconds);
        }
      })
      .catch(() => {
        // L'absence de position enregistrée n'est pas une erreur : la lecture démarre à zéro.
      });

    return () => {
      cancelled = true;
    };
  }, [track, isAuthenticated]);

  // Applique l'intention lecture/pause à l'élément média.
  useEffect(() => {
    const audio = audioRef.current;
    if (!audio || !track) {
      return;
    }

    if (isPlaying) {
      audio.play().catch(() => {
        // Le navigateur peut refuser une lecture non déclenchée par l'utilisateur.
        usePlayerStore.getState().pause();
      });
    } else {
      audio.pause();
    }
  }, [isPlaying, track]);

  // Applique le volume et la sourdine.
  useEffect(() => {
    const audio = audioRef.current;
    if (audio) {
      audio.volume = volume;
      audio.muted = muted;
    }
  }, [volume, muted]);

  // Applique une demande de repositionnement puis la consomme.
  useEffect(() => {
    const audio = audioRef.current;
    if (audio && seekRequest !== null) {
      audio.currentTime = seekRequest;
      usePlayerStore.getState().consumeSeek();
    }
  }, [seekRequest]);

  // Publie l'état vers les contrôles média du système (écran verrouillé, casque).
  useEffect(() => {
    if (!('mediaSession' in navigator) || !track) {
      return;
    }

    const cover = mediaUrl(track.coverUrls.medium);
    navigator.mediaSession.metadata = new MediaMetadata({
      title: track.title,
      artist: track.artistName,
      album: track.genre?.name ?? '',
      artwork: cover ? [{ src: cover, sizes: '300x300', type: 'image/webp' }] : [],
    });

    const store = usePlayerStore.getState();
    const handlers: [MediaSessionAction, MediaSessionActionHandler][] = [
      ['play', () => store.resume()],
      ['pause', () => store.pause()],
      ['previoustrack', () => store.previous()],
      ['nexttrack', () => store.next()],
      ['seekbackward', () => store.seek(Math.max(0, usePlayerStore.getState().position - 10))],
      ['seekforward', () => store.seek(usePlayerStore.getState().position + 10)],
      ['seekto', (details) => store.seek(details.seekTime ?? 0)],
    ];

    for (const [action, handler] of handlers) {
      try {
        navigator.mediaSession.setActionHandler(action, handler);
      } catch {
        // Toutes les actions ne sont pas prises en charge par tous les navigateurs.
      }
    }

    return () => {
      for (const [action] of handlers) {
        try {
          navigator.mediaSession.setActionHandler(action, null);
        } catch {
          // Rien à faire : l'action n'était pas gérée.
        }
      }
    };
  }, [track]);

  useEffect(() => {
    if ('mediaSession' in navigator) {
      navigator.mediaSession.playbackState = isPlaying ? 'playing' : 'paused';
    }
  }, [isPlaying]);

  /** Met à jour la position, comptabilise l'écoute et sauvegarde la progression. */
  function handleTimeUpdate(event: React.SyntheticEvent<HTMLAudioElement>) {
    const audio = event.currentTarget;
    const store = usePlayerStore.getState();
    const previous = store.position;

    store.setPosition(audio.currentTime);

    // On n'additionne que les avancées naturelles, afin qu'un seek ne gonfle pas le compteur.
    const delta = audio.currentTime - previous;
    if (delta > 0 && delta < 2) {
      listenedRef.current += delta;
    }

    if (!track) {
      return;
    }

    if (!reportedRef.current && listenedRef.current >= PLAY_REPORT_THRESHOLD_SECONDS) {
      reportedRef.current = true;
      tracksApi
        .registerPlay(track.id, {
          sessionId: SESSION_ID,
          positionSeconds: Math.floor(audio.currentTime),
          durationSeconds: Math.floor(listenedRef.current),
          source: 'PLAYER',
        })
        .catch(() => {
          // Une écoute non enregistrée ne doit jamais interrompre la lecture.
        });
    }

    const now = Date.now();
    if (isAuthenticated && now - lastSavedRef.current > PROGRESS_SAVE_INTERVAL_MS) {
      lastSavedRef.current = now;
      tracksApi.saveProgress(track.id, Math.floor(audio.currentTime)).catch(() => {
        // La reprise de lecture est un confort : son échec est silencieux.
      });
    }
  }

  /** Sauvegarde la position à la pause et en fin de morceau. */
  function saveProgressNow() {
    const audio = audioRef.current;
    if (audio && track && isAuthenticated) {
      tracksApi.saveProgress(track.id, Math.floor(audio.currentTime)).catch(() => {
        // Idem : échec silencieux.
      });
    }
  }

  return (
    <audio
      ref={audioRef}
      preload="metadata"
      onTimeUpdate={handleTimeUpdate}
      onLoadedMetadata={(event) => usePlayerStore.getState().setDuration(event.currentTarget.duration)}
      onEnded={() => {
        saveProgressNow();
        usePlayerStore.getState().handleEnded();
      }}
      onPause={saveProgressNow}
      onError={() => usePlayerStore.getState().pause()}
    />
  );
}
