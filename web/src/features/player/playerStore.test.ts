import { beforeEach, describe, expect, it } from 'vitest';
import { usePlayerStore } from './playerStore';
import type { Track } from '../../types/api';

/** Construit un morceau minimal pour alimenter la file de lecture. */
function track(id: string, durationSeconds = 180): Track {
  return {
    id,
    title: `Titre ${id}`,
    artistName: 'Artiste',
    durationSeconds,
    visibility: 'Public',
    status: 'Ready',
    owner: { id: 'owner', username: 'artiste' },
    tags: [],
    coverUrls: { small: '/s', medium: '/m', large: '/l' },
    streamUrl: `/api/v1/tracks/${id}/stream`,
    createdAt: new Date().toISOString(),
  };
}

const [a, b, c] = [track('a'), track('b'), track('c')];

/** Remet le store dans son état initial entre deux tests. */
function reset() {
  usePlayerStore.setState({
    queue: [],
    currentIndex: -1,
    isPlaying: false,
    position: 0,
    duration: 0,
    shuffle: false,
    repeat: 'off',
    expanded: false,
    seekRequest: null,
    shuffleOrder: [],
  });
}

describe("file d'attente", () => {
  beforeEach(reset);

  it('démarre la lecture du morceau demandé au sein de sa file', () => {
    usePlayerStore.getState().playTrack(b, [a, b, c]);

    const state = usePlayerStore.getState();
    expect(state.currentIndex).toBe(1);
    expect(state.isPlaying).toBe(true);
    expect(state.queue).toHaveLength(3);
  });

  it("ajoute un morceau à la fin de la file sans interrompre la lecture", () => {
    usePlayerStore.getState().playTrack(a, [a]);
    usePlayerStore.getState().enqueue(b);

    const state = usePlayerStore.getState();
    expect(state.queue.map((t) => t.id)).toEqual(['a', 'b']);
    expect(state.currentIndex).toBe(0);
  });

  it("n'ajoute pas deux fois le même morceau à la file", () => {
    usePlayerStore.getState().playTrack(a, [a]);
    usePlayerStore.getState().enqueue(a);

    expect(usePlayerStore.getState().queue).toHaveLength(1);
  });

  it('insère « lire ensuite » juste après le morceau courant', () => {
    usePlayerStore.getState().playQueue([a, b], 0);
    usePlayerStore.getState().playNext(c);

    expect(usePlayerStore.getState().queue.map((t) => t.id)).toEqual(['a', 'c', 'b']);
  });

  it("retire un morceau et corrige l'index courant", () => {
    usePlayerStore.getState().playQueue([a, b, c], 2);
    usePlayerStore.getState().removeFromQueue(0);

    const state = usePlayerStore.getState();
    expect(state.queue.map((t) => t.id)).toEqual(['b', 'c']);
    expect(state.queue[state.currentIndex].id).toBe('c');
  });

  it('arrête la lecture lorsque la file est vidée', () => {
    usePlayerStore.getState().playQueue([a, b], 0);
    usePlayerStore.getState().clearQueue();

    const state = usePlayerStore.getState();
    expect(state.queue).toHaveLength(0);
    expect(state.currentIndex).toBe(-1);
    expect(state.isPlaying).toBe(false);
  });
});

describe('enchaînement des morceaux', () => {
  beforeEach(reset);

  it('passe au morceau suivant', () => {
    usePlayerStore.getState().playQueue([a, b, c], 0);
    usePlayerStore.getState().next();

    expect(usePlayerStore.getState().currentIndex).toBe(1);
  });

  it("boucle sur le premier morceau lorsque l'utilisateur demande explicitement le suivant", () => {
    usePlayerStore.getState().playQueue([a, b], 1);
    usePlayerStore.getState().next();

    expect(usePlayerStore.getState().currentIndex).toBe(0);
  });

  it("s'arrête en fin de file sans répétition", () => {
    usePlayerStore.getState().playQueue([a, b], 1);
    usePlayerStore.getState().handleEnded();

    const state = usePlayerStore.getState();
    expect(state.isPlaying).toBe(false);
    expect(state.currentIndex).toBe(1);
  });

  it('reboucle en fin de file lorsque la répétition est active', () => {
    usePlayerStore.getState().playQueue([a, b], 1);
    usePlayerStore.getState().cycleRepeat(); // off -> all
    usePlayerStore.getState().handleEnded();

    const state = usePlayerStore.getState();
    expect(state.currentIndex).toBe(0);
    expect(state.isPlaying).toBe(true);
  });

  it('rejoue le même morceau en répétition unitaire', () => {
    usePlayerStore.getState().playQueue([a, b], 0);
    usePlayerStore.getState().cycleRepeat(); // all
    usePlayerStore.getState().cycleRepeat(); // one
    usePlayerStore.getState().handleEnded();

    const state = usePlayerStore.getState();
    expect(state.currentIndex).toBe(0);
    expect(state.seekRequest).toBe(0);
  });

  it('parcourt les trois modes de répétition', () => {
    expect(usePlayerStore.getState().repeat).toBe('off');
    usePlayerStore.getState().cycleRepeat();
    expect(usePlayerStore.getState().repeat).toBe('all');
    usePlayerStore.getState().cycleRepeat();
    expect(usePlayerStore.getState().repeat).toBe('one');
    usePlayerStore.getState().cycleRepeat();
    expect(usePlayerStore.getState().repeat).toBe('off');
  });

  it('revient au début du morceau si « précédent » est pressé après trois secondes', () => {
    usePlayerStore.getState().playQueue([a, b], 1);
    usePlayerStore.getState().setPosition(30);
    usePlayerStore.getState().previous();

    const state = usePlayerStore.getState();
    expect(state.currentIndex).toBe(1);
    expect(state.seekRequest).toBe(0);
  });

  it('passe au morceau précédent en début de lecture', () => {
    usePlayerStore.getState().playQueue([a, b], 1);
    usePlayerStore.getState().setPosition(1);
    usePlayerStore.getState().previous();

    expect(usePlayerStore.getState().currentIndex).toBe(0);
  });
});

describe('lecture aléatoire', () => {
  beforeEach(reset);

  it("conserve le morceau courant en tête de l'ordre aléatoire", () => {
    usePlayerStore.getState().playQueue([a, b, c], 1);
    usePlayerStore.getState().toggleShuffle();

    const state = usePlayerStore.getState();
    expect(state.shuffle).toBe(true);
    expect(state.shuffleOrder).toHaveLength(3);
    expect(state.shuffleOrder[0]).toBe(1);
    expect([...state.shuffleOrder].sort()).toEqual([0, 1, 2]);
  });

  it("parcourt toute la file sans répéter de morceau", () => {
    usePlayerStore.getState().playQueue([a, b, c], 0);
    usePlayerStore.getState().toggleShuffle();

    const visited = [usePlayerStore.getState().currentIndex];
    usePlayerStore.getState().next();
    visited.push(usePlayerStore.getState().currentIndex);
    usePlayerStore.getState().next();
    visited.push(usePlayerStore.getState().currentIndex);

    expect([...visited].sort()).toEqual([0, 1, 2]);
  });

  it("abandonne l'ordre aléatoire lorsque le mode est désactivé", () => {
    usePlayerStore.getState().playQueue([a, b, c], 0);
    usePlayerStore.getState().toggleShuffle();
    usePlayerStore.getState().toggleShuffle();

    const state = usePlayerStore.getState();
    expect(state.shuffle).toBe(false);
    expect(state.shuffleOrder).toHaveLength(0);
  });
});

describe('contrôles de lecture', () => {
  beforeEach(reset);

  it("ne démarre pas la lecture lorsque la file est vide", () => {
    usePlayerStore.getState().toggle();

    expect(usePlayerStore.getState().isPlaying).toBe(false);
  });

  it('bascule entre lecture et pause', () => {
    usePlayerStore.getState().playQueue([a], 0);
    usePlayerStore.getState().toggle();
    expect(usePlayerStore.getState().isPlaying).toBe(false);

    usePlayerStore.getState().toggle();
    expect(usePlayerStore.getState().isPlaying).toBe(true);
  });

  it('refuse une position de lecture négative', () => {
    usePlayerStore.getState().playQueue([a], 0);
    usePlayerStore.getState().seek(-20);

    expect(usePlayerStore.getState().position).toBe(0);
  });

  it('consomme la demande de repositionnement une seule fois', () => {
    usePlayerStore.getState().playQueue([a], 0);
    usePlayerStore.getState().seek(42);
    expect(usePlayerStore.getState().seekRequest).toBe(42);

    usePlayerStore.getState().consumeSeek();
    expect(usePlayerStore.getState().seekRequest).toBeNull();
  });

  it('borne le volume entre zéro et un', () => {
    usePlayerStore.getState().setVolume(5);
    expect(usePlayerStore.getState().volume).toBe(1);

    usePlayerStore.getState().setVolume(-3);
    expect(usePlayerStore.getState().volume).toBe(0);
  });

  it('coupe automatiquement le son à volume nul', () => {
    usePlayerStore.getState().setVolume(0);

    expect(usePlayerStore.getState().muted).toBe(true);
  });

  it('ignore une durée invalide rapportée par le média', () => {
    usePlayerStore.getState().playQueue([a], 0);
    const before = usePlayerStore.getState().duration;

    usePlayerStore.getState().setDuration(Number.NaN);
    expect(usePlayerStore.getState().duration).toBe(before);

    usePlayerStore.getState().setDuration(240);
    expect(usePlayerStore.getState().duration).toBe(240);
  });
});
