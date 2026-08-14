import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link, useParams } from 'react-router-dom';
import { usersApi } from '../services/api';
import { mediaUrl } from '../services/apiClient';
import { TrackList } from '../components/TrackList';
import { Empty, ErrorMessage, FollowButton, Loading, Pagination, ReportDialog } from '../components/common';
import { formatDate, formatNumber } from '../lib/format';
import { PlaylistCard } from './HomePage';

type Tab = 'tracks' | 'playlists' | 'followers' | 'following';

/** Profil public d'un utilisateur, avec ses morceaux, playlists et abonnements. */
export function ProfilePage() {
  const { username = '' } = useParams();
  const [tab, setTab] = useState<Tab>('tracks');
  const [page, setPage] = useState(1);

  const { data: profile, isLoading, error } = useQuery({
    queryKey: ['profile', username],
    queryFn: () => usersApi.get(username),
  });

  const tracksQuery = useQuery({
    queryKey: ['profile-tracks', username, page],
    queryFn: () => usersApi.tracks(username, { page, pageSize: 25 }),
    enabled: tab === 'tracks' && !!profile && !profile.isRestricted,
  });

  const playlistsQuery = useQuery({
    queryKey: ['profile-playlists', username, page],
    queryFn: () => usersApi.playlists(username, { page, pageSize: 24 }),
    enabled: tab === 'playlists' && !!profile && !profile.isRestricted,
  });

  const followersQuery = useQuery({
    queryKey: ['profile-followers', profile?.id, page],
    queryFn: () => usersApi.followers(profile!.id, { page, pageSize: 30 }),
    enabled: tab === 'followers' && !!profile && !profile.isRestricted,
  });

  const followingQuery = useQuery({
    queryKey: ['profile-following', profile?.id, page],
    queryFn: () => usersApi.following(profile!.id, { page, pageSize: 30 }),
    enabled: tab === 'following' && !!profile && !profile.isRestricted,
  });

  if (isLoading) {
    return <Loading rows={3} />;
  }

  if (error) {
    return <ErrorMessage error={error} />;
  }

  if (!profile) {
    return null;
  }

  const avatar = mediaUrl(profile.avatarUrl);

  function switchTab(next: Tab) {
    setTab(next);
    setPage(1);
  }

  return (
    <>
      <header className="row wrap" style={{ gap: 20, marginBottom: 28 }}>
        {avatar ? (
          <img className="avatar avatar-lg" src={avatar} alt="" />
        ) : (
          <span className="avatar avatar-lg" aria-hidden="true" />
        )}

        <div className="grow" style={{ minWidth: 240 }}>
          <h1 style={{ marginBottom: 4 }}>{profile.username}</h1>
          <p className="muted small">
            Membre depuis {formatDate(profile.createdAt)}
            {profile.role !== 'User' && <span className="badge" style={{ marginLeft: 8 }}>{profile.role}</span>}
          </p>

          {profile.isRestricted ? (
            <div className="alert alert-info">Ce profil est privé. Son contenu n'est pas public.</div>
          ) : (
            <>
              {profile.bio && <p>{profile.bio}</p>}

              <div className="row wrap" style={{ gap: 20, marginBottom: 12 }}>
                <span>
                  <strong>{formatNumber(profile.trackCount)}</strong> <span className="muted small">morceaux</span>
                </span>
                <span>
                  <strong>{formatNumber(profile.followerCount)}</strong> <span className="muted small">abonnés</span>
                </span>
                <span>
                  <strong>{formatNumber(profile.followingCount)}</strong> <span className="muted small">abonnements</span>
                </span>
              </div>

              {profile.socialLinks && Object.keys(profile.socialLinks).length > 0 && (
                <div className="row wrap" style={{ gap: 8, marginBottom: 12 }}>
                  {Object.entries(profile.socialLinks).map(([label, url]) => (
                    <a key={label} href={url} className="tag-chip" target="_blank" rel="noopener noreferrer">
                      {label}
                    </a>
                  ))}
                </div>
              )}
            </>
          )}

          <div className="row" style={{ gap: 8 }}>
            <FollowButton
              userId={profile.id}
              username={profile.username}
              isFollowing={Boolean(profile.isFollowedByCurrentUser)}
            />
            <ReportDialog targetType="User" targetId={profile.id} targetLabel={profile.username} />
          </div>
        </div>
      </header>

      {!profile.isRestricted && (
        <>
          <div className="tabs" role="tablist" aria-label="Contenu du profil">
            <button type="button" role="tab" aria-selected={tab === 'tracks'} onClick={() => switchTab('tracks')}>
              Morceaux
            </button>
            <button type="button" role="tab" aria-selected={tab === 'playlists'} onClick={() => switchTab('playlists')}>
              Playlists
            </button>
            <button type="button" role="tab" aria-selected={tab === 'followers'} onClick={() => switchTab('followers')}>
              Abonnés
            </button>
            <button type="button" role="tab" aria-selected={tab === 'following'} onClick={() => switchTab('following')}>
              Abonnements
            </button>
          </div>

          {tab === 'tracks' && (
            <>
              {tracksQuery.isLoading ? (
                <Loading />
              ) : (
                <TrackList tracks={tracksQuery.data?.items ?? []} emptyLabel="Aucun morceau publié." />
              )}
              <Pagination page={page} totalPages={tracksQuery.data?.totalPages ?? 0} onChange={setPage} />
            </>
          )}

          {tab === 'playlists' && (
            <>
              {playlistsQuery.isLoading ? (
                <Loading />
              ) : playlistsQuery.data && playlistsQuery.data.items.length > 0 ? (
                <div className="grid">
                  {playlistsQuery.data.items.map((playlist) => (
                    <PlaylistCard key={playlist.id} playlist={playlist} />
                  ))}
                </div>
              ) : (
                <Empty>Aucune playlist publique.</Empty>
              )}
              <Pagination page={page} totalPages={playlistsQuery.data?.totalPages ?? 0} onChange={setPage} />
            </>
          )}

          {tab === 'followers' && (
            <UserList
              users={followersQuery.data?.items ?? []}
              loading={followersQuery.isLoading}
              emptyLabel="Aucun abonné."
            />
          )}

          {tab === 'following' && (
            <UserList
              users={followingQuery.data?.items ?? []}
              loading={followingQuery.isLoading}
              emptyLabel="Aucun abonnement."
            />
          )}
        </>
      )}
    </>
  );
}

/** Liste d'utilisateurs, réutilisée par les onglets abonnés et abonnements. */
export function UserList({
  users,
  loading,
  emptyLabel,
}: {
  users: { id: string; username: string; avatarUrl?: string | null; followerCount: number; trackCount: number }[];
  loading?: boolean;
  emptyLabel: string;
}) {
  if (loading) {
    return <Loading rows={3} />;
  }

  if (users.length === 0) {
    return <Empty>{emptyLabel}</Empty>;
  }

  return (
    <div className="stack">
      {users.map((user) => (
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
  );
}
