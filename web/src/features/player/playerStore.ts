import { create } from 'zustand';
import type { Track } from '../../types/api';

export type RepeatMode = 'off' | 'all' | 'one';

const VOLUME_KEY = 'mp.volume';
const MUTED_KEY = 'mp.muted';

/**
 * État du lecteur audio.
 *
 * Il est volontairement distinct de l'état serveur et de l'état d'authentification :
 * la file d'attente et la position de lecture survivent à chaque navigation, alors
 * que les données distantes sont mises en cache et invalidées séparément.
 *
 * Le store ne détient jamais l'élément `<audio>` : il décrit l'intention (quoi jouer,
 * en pause ou non, à quelle position), et un unique composant monté à la racine
 * applique cette intention à l'élément média.
 */
interface PlayerState {
  /** File complète, dans l'ordre d'ajout. */
  queue: Track[];
  /** Index courant dans `queue`, ou -1 si rien n'est chargé. */
  currentIndex: number;
  isPlaying: boolean;
  /** Position courante en secondes, mise à jour par l'élément audio. */
  position: number;
  /** Durée réelle du média chargé, qui prime sur la durée annoncée par l'API. */
  duration: number;
  volume: number;
  muted: boolean;
  shuffle: boolean;
  repeat: RepeatMode;
  /** Vrai lorsque le lecteur plein écran est ouvert. */
  expanded: boolean;
  /**
   * Position à appliquer à l'élément audio au prochain rendu.
   * Remise à `null` une fois consommée, ce qui évite de repositionner en boucle.
   */
  seekRequest: number | null;

  /** Ordre de lecture aléatoire pré-calculé, utilisé lorsque `shuffle` est actif. */
  shuffleOrder: number[];

  playTrack: (track: Track, queue?: Track[]) => void;
  playQueue: (tracks: Track[], startIndex?: number) => void;
  enqueue: (track: Track) => void;
  playNext: (track: Track) => void;
  removeFromQueue: (index: number) => void;
  clearQueue: () => void;

  toggle: () => void;
  pause: () => void;
  resume: () => void;
  next: () => void;
  previous: () => void;
  seek: (seconds: number) => void;
  consumeSeek: () => void;

  setPosition: (seconds: number) => void;
  setDuration: (seconds: number) => void;
  setVolume: (volume: number) => void;
  toggleMute: () => void;
  toggleShuffle: () => void;
  cycleRepeat: () => void;
  setExpanded: (expanded: boolean) => void;

  /** Appelé par l'élément audio à la fin d'un morceau. */
  handleEnded: () => void;
}

/** Lit une préférence numérique persistée, avec repli sur la valeur par défaut. */
function readNumber(key: string, fallback: number): number {
  const raw = localStorage.getItem(key);
  const parsed = raw === null ? Number.NaN : Number(raw);
  return Number.isFinite(parsed) ? parsed : fallback;
}

/**
 * Construit un ordre aléatoire des index de la file en plaçant `startIndex` en tête,
 * afin que l'activation du mode aléatoire n'interrompe pas le morceau en cours.
 * Mélange de Fisher-Yates : le nombre d'itérations est borné par la taille de la file.
 */
function buildShuffleOrder(length: number, startIndex: number): number[] {
  const order = Array.from({ length }, (_, index) => index);

  for (let i = order.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [order[i], order[j]] = [order[j], order[i]];
  }

  if (startIndex >= 0) {
    const position = order.indexOf(startIndex);
    if (position > 0) {
      [order[0], order[position]] = [order[position], order[0]];
    }
  }

  return order;
}

export const usePlayerStore = create<PlayerState>((set, get) => ({
  queue: [],
  currentIndex: -1,
  isPlaying: false,
  position: 0,
  duration: 0,
  volume: readNumber(VOLUME_KEY, 1),
  muted: localStorage.getItem(MUTED_KEY) === 'true',
  shuffle: false,
  repeat: 'off',
  expanded: false,
  seekRequest: null,
  shuffleOrder: [],

  playTrack: (track, queue) => {
    const nextQueue = queue && queue.length > 0 ? queue : [track];
    const index = Math.max(0, nextQueue.findIndex((item) => item.id === track.id));

    set({
      queue: nextQueue,
      currentIndex: index,
      isPlaying: true,
      position: 0,
      duration: track.durationSeconds,
      seekRequest: 0,
      shuffleOrder: get().shuffle ? buildShuffleOrder(nextQueue.length, index) : [],
    });
  },

  playQueue: (tracks, startIndex = 0) => {
    if (tracks.length === 0) {
      return;
    }

    const index = Math.min(Math.max(startIndex, 0), tracks.length - 1);
    set({
      queue: tracks,
      currentIndex: index,
      isPlaying: true,
      position: 0,
      duration: tracks[index].durationSeconds,
      seekRequest: 0,
      shuffleOrder: get().shuffle ? buildShuffleOrder(tracks.length, index) : [],
    });
  },

  enqueue: (track) => {
    const { queue, currentIndex } = get();
    if (queue.some((item) => item.id === track.id)) {
      return;
    }

    const nextQueue = [...queue, track];
    set({
      queue: nextQueue,
      currentIndex: currentIndex < 0 ? 0 : currentIndex,
      shuffleOrder: get().shuffle ? buildShuffleOrder(nextQueue.length, Math.max(currentIndex, 0)) : [],
    });
  },

  playNext: (track) => {
    const { queue, currentIndex } = get();
    const filtered = queue.filter((item) => item.id !== track.id);
    const insertAt = Math.min(currentIndex + 1, filtered.length);
    const nextQueue = [...filtered.slice(0, insertAt), track, ...filtered.slice(insertAt)];

    set({
      queue: nextQueue,
      currentIndex: currentIndex < 0 ? 0 : currentIndex,
      shuffleOrder: get().shuffle ? buildShuffleOrder(nextQueue.length, Math.max(currentIndex, 0)) : [],
    });
  },

  removeFromQueue: (index) => {
    const { queue, currentIndex } = get();
    if (index < 0 || index >= queue.length) {
      return;
    }

    const nextQueue = queue.filter((_, i) => i !== index);
    let nextIndex = currentIndex;

    if (index < currentIndex) {
      nextIndex = currentIndex - 1;
    } else if (index === currentIndex) {
      nextIndex = nextQueue.length === 0 ? -1 : Math.min(currentIndex, nextQueue.length - 1);
    }

    set({
      queue: nextQueue,
      currentIndex: nextIndex,
      isPlaying: nextQueue.length > 0 && get().isPlaying,
      shuffleOrder: get().shuffle ? buildShuffleOrder(nextQueue.length, Math.max(nextIndex, 0)) : [],
    });
  },

  clearQueue: () => set({ queue: [], currentIndex: -1, isPlaying: false, position: 0, duration: 0, shuffleOrder: [] }),

  toggle: () => {
    const { currentIndex, isPlaying } = get();
    if (currentIndex < 0) {
      return;
    }
    set({ isPlaying: !isPlaying });
  },

  pause: () => set({ isPlaying: false }),

  resume: () => {
    if (get().currentIndex >= 0) {
      set({ isPlaying: true });
    }
  },

  next: () => {
    const target = resolveNextIndex(get(), true);
    if (target === null) {
      set({ isPlaying: false });
      return;
    }

    set({
      currentIndex: target,
      position: 0,
      duration: get().queue[target].durationSeconds,
      isPlaying: true,
      seekRequest: 0,
    });
  },

  previous: () => {
    const { position, currentIndex, queue, shuffle, shuffleOrder } = get();

    // Convention usuelle : au-delà de trois secondes, « précédent » revient au début.
    if (position > 3) {
      set({ seekRequest: 0, position: 0 });
      return;
    }

    if (currentIndex <= 0 && !shuffle) {
      set({ seekRequest: 0, position: 0 });
      return;
    }

    let target: number;
    if (shuffle && shuffleOrder.length === queue.length) {
      const rank = shuffleOrder.indexOf(currentIndex);
      target = rank > 0 ? shuffleOrder[rank - 1] : shuffleOrder[shuffleOrder.length - 1];
    } else {
      target = currentIndex - 1;
    }

    set({
      currentIndex: target,
      position: 0,
      duration: queue[target].durationSeconds,
      isPlaying: true,
      seekRequest: 0,
    });
  },

  seek: (seconds) => {
    const clamped = Math.max(0, seconds);
    set({ position: clamped, seekRequest: clamped });
  },

  consumeSeek: () => set({ seekRequest: null }),

  setPosition: (seconds) => set({ position: seconds }),

  setDuration: (seconds) => {
    if (Number.isFinite(seconds) && seconds > 0) {
      set({ duration: seconds });
    }
  },

  setVolume: (volume) => {
    const clamped = Math.min(1, Math.max(0, volume));
    localStorage.setItem(VOLUME_KEY, String(clamped));
    set({ volume: clamped, muted: clamped === 0 });
  },

  toggleMute: () => {
    const muted = !get().muted;
    localStorage.setItem(MUTED_KEY, String(muted));
    set({ muted });
  },

  toggleShuffle: () => {
    const shuffle = !get().shuffle;
    const { queue, currentIndex } = get();
    set({ shuffle, shuffleOrder: shuffle ? buildShuffleOrder(queue.length, currentIndex) : [] });
  },

  cycleRepeat: () => {
    const order: RepeatMode[] = ['off', 'all', 'one'];
    const current = order.indexOf(get().repeat);
    set({ repeat: order[(current + 1) % order.length] });
  },

  setExpanded: (expanded) => set({ expanded }),

  handleEnded: () => {
    const state = get();

    if (state.repeat === 'one') {
      set({ position: 0, seekRequest: 0, isPlaying: true });
      return;
    }

    const target = resolveNextIndex(state, false);
    if (target === null) {
      set({ isPlaying: false, position: 0 });
      return;
    }

    set({
      currentIndex: target,
      position: 0,
      duration: state.queue[target].durationSeconds,
      isPlaying: true,
      seekRequest: 0,
    });
  },
}));

/**
 * Détermine l'index du morceau suivant.
 *
 * `manual` distingue l'appui sur « suivant » — qui boucle toujours en fin de file —
 * de la fin naturelle d'un morceau, qui ne boucle que si la répétition est active.
 * Retourne `null` lorsqu'il n'y a plus rien à jouer.
 */
function resolveNextIndex(state: PlayerState, manual: boolean): number | null {
  const { queue, currentIndex, shuffle, shuffleOrder, repeat } = state;

  if (queue.length === 0 || currentIndex < 0) {
    return null;
  }

  if (shuffle && shuffleOrder.length === queue.length) {
    const rank = shuffleOrder.indexOf(currentIndex);
    if (rank >= 0 && rank < shuffleOrder.length - 1) {
      return shuffleOrder[rank + 1];
    }
    return repeat === 'all' || manual ? shuffleOrder[0] : null;
  }

  if (currentIndex < queue.length - 1) {
    return currentIndex + 1;
  }

  return repeat === 'all' || manual ? 0 : null;
}

/** Sélecteur : morceau en cours de lecture, ou `null`. */
export function useCurrentTrack(): Track | null {
  return usePlayerStore((state) => (state.currentIndex >= 0 ? (state.queue[state.currentIndex] ?? null) : null));
}
