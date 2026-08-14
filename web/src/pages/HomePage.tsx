import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { discoveryApi } from '../services/api';
import { mediaUrl } from '../services/apiClient';
import { useAuthStore } from '../features/auth/authStore';
import { TrackGrid } from '../components/TrackCard';
import { ErrorMessage, Loading } from '../components/common';
import { formatNumber } from '../lib/format';
import type { Playlist, Track, UserSummary } from '../types/api';

/** Section de la page d'accueil, masquée lorsqu'elle est vide. */
function Section({ title, children, empty }: { title: string; children: React.ReactNode; empty: boolean }) {
  if (empty) {
    return null;
  }

  return (
    <section className="section">
      <div className="section-header">
        <h2>{title}</h2>
      </div>
      {children}
    </section>
  );
}

/** Vignette d'artiste. */
function ArtistCard({ artist }: { artist: UserSummary }) {
  const avatar = mediaUrl(artist.avatarUrl);

  return (
    <Link to={`/users/${artist.username}`} style={{ textAlign: 'center' }}>
      <div className="cover" style={{ borderRadius: '50%' }}>
        {avatar ? <img src={avatar} alt="" loading="lazy" /> : <div className="cover-placeholder">♪</div>}
      </div>
      <span className="truncate" style={{ display: 'block', fontWeight: 600 }}>
        {artist.username}
      </span>
      <span className="small muted">{formatNumber(artist.followerCount)} abonnés</span>
    </Link>
  );
}

/** Vignette de playlist. */
export function PlaylistCard({ playlist }: { playlist: Playlist }) {
  const cover = mediaUrl(playlist.coverUrl);

  return (
    <Link to={`/playlists/${playlist.id}`}>
      <div className="cover">
        {cover ? <img src={cover} alt="" loading="lazy" /> : <div className="cover-placeholder">≡</div>}
      </div>
      <span className="truncate" style={{ display: 'block', fontWeight: 600 }}>
        {playlist.name}
      </span>
      <span className="small muted">
        {playlist.trackCount} morceaux · {playlist.owner.username}
      </span>
    </Link>
  );
}

/** Page d'accueil : nouveautés, populaires, recommandations et abonnements. */
export function HomePage() {
  const me = useAuthStore((state) => state.me);
  const { data, isLoading, error } = useQuery({ queryKey: ['home'], queryFn: discoveryApi.home });

  if (isLoading) {
    return <Loading rows={5} />;
  }

  if (error) {
    return <ErrorMessage error={error} />;
  }

  if (!data) {
    return null;
  }

  const allTracks: Track[] = [...data.recentTracks, ...data.popularTracks];

  return (
    <>
      <h1>{me ? `Bonjour ${me.profile.username}` : 'Découvrir'}</h1>

      <Section title="Recommandé pour vous" empty={data.recommendations.length === 0}>
        <TrackGrid tracks={data.recommendations} />
      </Section>

      <Section title="Des artistes que vous suivez" empty={data.fromFollowedArtists.length === 0}>
        <TrackGrid tracks={data.fromFollowedArtists} />
      </Section>

      <Section title="Nouveautés" empty={data.recentTracks.length === 0}>
        <TrackGrid tracks={data.recentTracks} />
      </Section>

      <Section title="Les plus écoutés" empty={data.popularTracks.length === 0}>
        <TrackGrid tracks={data.popularTracks} />
      </Section>

      <Section title="Artistes en vue" empty={data.popularArtists.length === 0}>
        <div className="grid">
          {data.popularArtists.map((artist) => (
            <ArtistCard key={artist.id} artist={artist} />
          ))}
        </div>
      </Section>

      <Section title="Playlists populaires" empty={data.popularPlaylists.length === 0}>
        <div className="grid">
          {data.popularPlaylists.map((playlist) => (
            <PlaylistCard key={playlist.id} playlist={playlist} />
          ))}
        </div>
      </Section>

      {allTracks.length === 0 && (
        <p className="empty">
          Le catalogue est encore vide. {me ? <Link to="/upload">Importez votre premier morceau.</Link> : null}
        </p>
      )}
    </>
  );
}
