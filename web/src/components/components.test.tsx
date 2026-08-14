import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ReactNode } from 'react';
import { LikeButton } from './LikeButton';
import { TrackList } from './TrackList';
import { Pagination } from './common';
import { formatBytes, formatDuration, formatNumber } from '../lib/format';
import { usePlayerStore } from '../features/player/playerStore';
import { useAuthStore } from '../features/auth/authStore';
import { tracksApi } from '../services/api';
import type { Track } from '../types/api';

/** Construit un morceau de test. */
function makeTrack(overrides: Partial<Track> = {}): Track {
  return {
    id: 'track-1',
    title: 'Nuit blanche',
    artistName: 'Alice',
    durationSeconds: 215,
    visibility: 'Public',
    status: 'Ready',
    owner: { id: 'user-1', username: 'alice' },
    tags: [],
    coverUrls: { small: '/s', medium: '/m', large: '/l' },
    streamUrl: '/api/v1/tracks/track-1/stream',
    createdAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

/** Enveloppe les composants dans les providers nécessaires. */
function wrap(ui: ReactNode) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>{ui}</MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('formatage', () => {
  it('affiche les durées en minutes et secondes', () => {
    expect(formatDuration(0)).toBe('0:00');
    expect(formatDuration(9)).toBe('0:09');
    expect(formatDuration(215)).toBe('3:35');
    expect(formatDuration(3725)).toBe('1:02:05');
  });

  it('protège contre une durée invalide', () => {
    expect(formatDuration(Number.NaN)).toBe('0:00');
    expect(formatDuration(-10)).toBe('0:00');
  });

  it('distingue un compteur masqué d’un compteur à zéro', () => {
    expect(formatNumber(0)).toBe('0');
    expect(formatNumber(null)).toBe('—');
    expect(formatNumber(undefined)).toBe('—');
  });

  it('formate les tailles de fichier', () => {
    expect(formatBytes(512)).toBe('512 o');
    expect(formatBytes(1024)).toBe('1.0 Ko');
    expect(formatBytes(20 * 1024 * 1024)).toBe('20.0 Mo');
  });
});

describe('LikeButton', () => {
  beforeEach(() => {
    useAuthStore.setState({ me: null, loading: false });
    vi.restoreAllMocks();
  });

  it("expose l'état du like aux technologies d'assistance", () => {
    wrap(<LikeButton track={makeTrack({ isLikedByCurrentUser: true })} />);

    const button = screen.getByRole('button', { name: /Retirer le like/i });
    expect(button).toHaveAttribute('aria-pressed', 'true');
  });

  it("affiche le compteur lorsqu'il est demandé et disponible", () => {
    wrap(<LikeButton track={makeTrack({ likeCount: 42 })} showCount />);

    expect(screen.getByText('42')).toBeInTheDocument();
  });

  it("n'affiche aucun compteur lorsque le propriétaire l'a masqué", () => {
    wrap(<LikeButton track={makeTrack({ likeCount: null })} showCount />);

    expect(screen.queryByText('42')).not.toBeInTheDocument();
  });

  it("n'appelle pas l'API lorsque le visiteur n'est pas connecté", async () => {
    const like = vi.spyOn(tracksApi, 'like');
    wrap(<LikeButton track={makeTrack()} />);

    await userEvent.click(screen.getByRole('button', { name: /Aimer/i }));

    expect(like).not.toHaveBeenCalled();
  });

  it("envoie le like lorsque l'utilisateur est connecté", async () => {
    useAuthStore.setState({
      me: {
        profile: {
          id: 'user-2',
          username: 'bob',
          profileVisibility: 'Public',
          role: 'User',
          createdAt: '2026-01-01T00:00:00Z',
          trackCount: 0,
          playlistCount: 0,
          followerCount: 0,
          followingCount: 0,
          isRestricted: false,
        },
        email: 'bob@example.com',
        settings: { showLikeCount: true, showPlayCount: true },
        status: 'Active',
      },
      loading: false,
    });

    const like = vi.spyOn(tracksApi, 'like').mockResolvedValue({ liked: true, likeCount: 1 });
    wrap(<LikeButton track={makeTrack()} />);

    await userEvent.click(screen.getByRole('button', { name: /Aimer/i }));

    await waitFor(() => expect(like).toHaveBeenCalledWith('track-1'));
  });
});

describe('TrackList', () => {
  beforeEach(() => {
    usePlayerStore.setState({ queue: [], currentIndex: -1, isPlaying: false, shuffleOrder: [] });
    useAuthStore.setState({ me: null, loading: false });
  });

  it('affiche un message quand la liste est vide', () => {
    wrap(<TrackList tracks={[]} emptyLabel="Rien à écouter." />);

    expect(screen.getByText('Rien à écouter.')).toBeInTheDocument();
  });

  it('affiche le titre, l’artiste et la durée de chaque morceau', () => {
    wrap(<TrackList tracks={[makeTrack()]} />);

    expect(screen.getByText('Nuit blanche')).toBeInTheDocument();
    expect(screen.getByText('Alice')).toBeInTheDocument();
    expect(screen.getByText('3:35')).toBeInTheDocument();
  });

  it('charge la file complète dans le lecteur au clic sur lecture', async () => {
    const first = makeTrack({ id: 't1', title: 'Premier' });
    const second = makeTrack({ id: 't2', title: 'Second' });

    wrap(<TrackList tracks={[first, second]} />);

    await userEvent.click(screen.getByRole('button', { name: /Lire Second/i }));

    const state = usePlayerStore.getState();
    expect(state.queue.map((t) => t.id)).toEqual(['t1', 't2']);
    expect(state.currentIndex).toBe(1);
    expect(state.isPlaying).toBe(true);
  });
});

describe('Pagination', () => {
  it("ne s'affiche pas pour une page unique", () => {
    const { container } = wrap(<Pagination page={1} totalPages={1} onChange={() => {}} />);

    expect(container).toBeEmptyDOMElement();
  });

  it('désactive les bornes de navigation', () => {
    wrap(<Pagination page={1} totalPages={3} onChange={() => {}} />);

    expect(screen.getByRole('button', { name: 'Précédent' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Suivant' })).toBeEnabled();
  });

  it('signale la page demandée', async () => {
    const onChange = vi.fn();
    wrap(<Pagination page={2} totalPages={5} onChange={onChange} />);

    await userEvent.click(screen.getByRole('button', { name: 'Suivant' }));

    expect(onChange).toHaveBeenCalledWith(3);
  });
});
