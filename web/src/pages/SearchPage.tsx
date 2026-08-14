import { useEffect, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { discoveryApi } from '../services/api';
import { mediaUrl } from '../services/apiClient';
import { TrackList } from '../components/TrackList';
import { Empty, ErrorMessage, Loading, Pagination } from '../components/common';
import { useDebounced } from '../hooks';
import { formatNumber } from '../lib/format';
import { PlaylistCard } from './HomePage';
import type { SearchType } from '../types/api';

const TYPES: { value: SearchType; label: string }[] = [
  { value: 'All', label: 'Tout' },
  { value: 'Track', label: 'Morceaux' },
  { value: 'User', label: 'Artistes' },
  { value: 'Playlist', label: 'Playlists' },
  { value: 'Album', label: 'Albums' },
  { value: 'Tag', label: 'Tags' },
];

const SORTS = [
  { value: '', label: 'Pertinence' },
  { value: 'recent', label: 'Plus récents' },
  { value: 'popular', label: 'Plus écoutés' },
  { value: 'likes', label: 'Plus aimés' },
  { value: 'duration', label: 'Durée' },
];

/**
 * Recherche instantanée.
 *
 * La saisie est débattue avant d'atteindre l'API afin de ne pas déclencher une requête
 * par frappe ; les filtres sont reflétés dans l'URL pour rendre la recherche partageable.
 */
export function SearchPage() {
  const [params, setParams] = useSearchParams();
  const [term, setTerm] = useState(params.get('q') ?? '');
  const debouncedTerm = useDebounced(term, 300);

  const type = (params.get('type') as SearchType | null) ?? 'All';
  const genre = params.get('genre') ?? '';
  const sort = params.get('sort') ?? '';
  const minDuration = params.get('minDuration') ?? '';
  const maxDuration = params.get('maxDuration') ?? '';

  const { data: genres } = useQuery({ queryKey: ['genres'], queryFn: discoveryApi.genres, staleTime: 300000 });

  // Le terme débattu est propagé dans l'URL : l'historique reste cohérent.
  useEffect(() => {
    setParams(
      (current) => {
        const next = new URLSearchParams(current);
        if (debouncedTerm) {
          next.set('q', debouncedTerm);
        } else {
          next.delete('q');
        }
        return next;
      },
      { replace: true },
    );
  }, [debouncedTerm, setParams]);

  const { data, isFetching, error } = useQuery({
    queryKey: ['search', debouncedTerm, type, genre, sort, minDuration, maxDuration],
    queryFn: () =>
      discoveryApi.search({
        q: debouncedTerm || undefined,
        type,
        genre: genre || undefined,
        sort: sort || undefined,
        minDuration: minDuration ? Number(minDuration) : undefined,
        maxDuration: maxDuration ? Number(maxDuration) : undefined,
        pageSize: 24,
      }),
    enabled: debouncedTerm.length > 0 || type !== 'All' || genre !== '',
  });

  /** Met à jour un filtre dans l'URL. */
  function updateParam(key: string, value: string) {
    setParams((current) => {
      const next = new URLSearchParams(current);
      if (value) {
        next.set(key, value);
      } else {
        next.delete(key);
      }
      return next;
    });
  }

  return (
    <>
      <h1>Recherche</h1>

      <div className="field">
        <label htmlFor="search-input">Rechercher</label>
        <input
          id="search-input"
          type="search"
          value={term}
          onChange={(event) => setTerm(event.target.value)}
          placeholder="Titre, artiste, playlist ou #tag"
          autoFocus
        />
      </div>

      <div className="tabs" role="tablist" aria-label="Type de résultat">
        {TYPES.map((item) => (
          <button
            key={item.value}
            type="button"
            role="tab"
            aria-selected={type === item.value}
            onClick={() => updateParam('type', item.value === 'All' ? '' : item.value)}
          >
            {item.label}
          </button>
        ))}
      </div>

      <div className="row wrap" style={{ marginBottom: 24 }}>
        <div className="field" style={{ marginBottom: 0, minWidth: 160 }}>
          <label htmlFor="filter-genre">Genre</label>
          <select id="filter-genre" value={genre} onChange={(event) => updateParam('genre', event.target.value)}>
            <option value="">Tous</option>
            {genres?.map((item) => (
              <option key={item.id} value={item.slug}>
                {item.name}
              </option>
            ))}
          </select>
        </div>

        <div className="field" style={{ marginBottom: 0, minWidth: 150 }}>
          <label htmlFor="filter-sort">Trier par</label>
          <select id="filter-sort" value={sort} onChange={(event) => updateParam('sort', event.target.value)}>
            {SORTS.map((item) => (
              <option key={item.value} value={item.value}>
                {item.label}
              </option>
            ))}
          </select>
        </div>

        <div className="field" style={{ marginBottom: 0, width: 130 }}>
          <label htmlFor="filter-min">Durée min. (s)</label>
          <input
            id="filter-min"
            type="number"
            min={0}
            value={minDuration}
            onChange={(event) => updateParam('minDuration', event.target.value)}
          />
        </div>

        <div className="field" style={{ marginBottom: 0, width: 130 }}>
          <label htmlFor="filter-max">Durée max. (s)</label>
          <input
            id="filter-max"
            type="number"
            min={0}
            value={maxDuration}
            onChange={(event) => updateParam('maxDuration', event.target.value)}
          />
        </div>
      </div>

      <ErrorMessage error={error} />

      {isFetching && <Loading rows={3} />}

      {data && !isFetching && (
        <div aria-live="polite">
          {data.tracks && data.tracks.items.length > 0 && (
            <section className="section">
              <h2>Morceaux ({formatNumber(data.tracks.totalItems)})</h2>
              <TrackList tracks={data.tracks.items} />
            </section>
          )}

          {data.users && data.users.items.length > 0 && (
            <section className="section">
              <h2>Artistes ({formatNumber(data.users.totalItems)})</h2>
              <div className="stack">
                {data.users.items.map((user) => (
                  <Link key={user.id} to={`/users/${user.username}`} className="row card">
                    {user.avatarUrl ? (
                      <img className="avatar" src={mediaUrl(user.avatarUrl)} alt="" />
                    ) : (
                      <span className="avatar" aria-hidden="true" />
                    )}
                    <span className="grow">
                      <strong>{user.username}</strong>
                      <span className="small muted" style={{ display: 'block' }}>
                        {formatNumber(user.followerCount)} abonnés · {formatNumber(user.trackCount)} morceaux
                      </span>
                    </span>
                  </Link>
                ))}
              </div>
            </section>
          )}

          {data.playlists && data.playlists.items.length > 0 && (
            <section className="section">
              <h2>Playlists ({formatNumber(data.playlists.totalItems)})</h2>
              <div className="grid">
                {data.playlists.items.map((playlist) => (
                  <PlaylistCard key={playlist.id} playlist={playlist} />
                ))}
              </div>
            </section>
          )}

          {data.albums && data.albums.items.length > 0 && (
            <section className="section">
              <h2>Albums ({formatNumber(data.albums.totalItems)})</h2>
              <ul>
                {data.albums.items.map((album) => (
                  <li key={album.id}>
                    {album.name} — <span className="muted">{album.artistName}</span>
                  </li>
                ))}
              </ul>
            </section>
          )}

          {data.tags && data.tags.items.length > 0 && (
            <section className="section">
              <h2>Tags ({formatNumber(data.tags.totalItems)})</h2>
              <div className="row wrap">
                {data.tags.items.map((tag) => (
                  <Link key={tag.id} to={`/tags/${tag.slug}`} className="tag-chip">
                    #{tag.slug} ({tag.trackCount})
                  </Link>
                ))}
              </div>
            </section>
          )}

          {isEmpty(data) && <Empty>Aucun résultat pour cette recherche.</Empty>}
        </div>
      )}
    </>
  );
}

/** Vrai lorsqu'aucune section de résultats n'est renseignée. */
function isEmpty(result: {
  tracks?: { items: unknown[] } | null;
  users?: { items: unknown[] } | null;
  playlists?: { items: unknown[] } | null;
  albums?: { items: unknown[] } | null;
  tags?: { items: unknown[] } | null;
}): boolean {
  return (
    (result.tracks?.items.length ?? 0) === 0 &&
    (result.users?.items.length ?? 0) === 0 &&
    (result.playlists?.items.length ?? 0) === 0 &&
    (result.albums?.items.length ?? 0) === 0 &&
    (result.tags?.items.length ?? 0) === 0
  );
}

/** Liste paginée des morceaux portant un tag donné. */
export function TagPage() {
  const { tag = '' } = useParams();
  const [page, setPage] = useState(1);

  const { data, isLoading, error } = useQuery({
    queryKey: ['tag-tracks', tag, page],
    queryFn: () => discoveryApi.tagTracks(tag, { page, pageSize: 30 }),
  });

  return (
    <>
      <h1>#{tag}</h1>
      <ErrorMessage error={error} />
      {isLoading ? (
        <Loading />
      ) : (
        <>
          <TrackList tracks={data?.items ?? []} emptyLabel="Aucun morceau ne porte ce tag." />
          <Pagination page={page} totalPages={data?.totalPages ?? 0} onChange={setPage} />
        </>
      )}
    </>
  );
}
