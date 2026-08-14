import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, NavLink, Outlet } from 'react-router-dom';
import { adminApi, discoveryApi } from '../services/api';
import { Dialog, Empty, ErrorMessage, Loading, Pagination } from '../components/common';
import { useDebounced } from '../hooks';
import { formatBytes, formatDateTime, formatNumber } from '../lib/format';
import type { ReportStatus, UserRole, UserStatus } from '../types/api';

/** Coquille de la console d'administration, avec sa navigation par onglets. */
export function AdminLayout() {
  return (
    <>
      <h1>Administration</h1>

      <nav className="tabs" aria-label="Sections d'administration">
        <NavLink to="/admin" end>
          Statistiques
        </NavLink>
        <NavLink to="/admin/reports">Signalements</NavLink>
        <NavLink to="/admin/tracks">Morceaux</NavLink>
        <NavLink to="/admin/users">Utilisateurs</NavLink>
        <NavLink to="/admin/genres">Genres</NavLink>
        <NavLink to="/admin/audit-logs">Journal d'audit</NavLink>
      </nav>

      <Outlet />
    </>
  );
}

/** Statistiques globales de la plateforme. */
export function AdminStatisticsPage() {
  const { data, isLoading, error } = useQuery({ queryKey: ['admin-stats'], queryFn: adminApi.statistics });

  if (isLoading) {
    return <Loading rows={3} />;
  }

  if (error) {
    return <ErrorMessage error={error} />;
  }

  if (!data) {
    return null;
  }

  const max = Math.max(...data.playsLast30Days.map((point) => point.plays), 1);

  return (
    <>
      <div className="stat-grid" style={{ marginBottom: 32 }}>
        <div className="stat"><div className="value">{formatNumber(data.totalUsers)}</div><div className="label">Utilisateurs</div></div>
        <div className="stat"><div className="value">{formatNumber(data.activeUsers)}</div><div className="label">Actifs</div></div>
        <div className="stat"><div className="value">{formatNumber(data.suspendedUsers)}</div><div className="label">Suspendus</div></div>
        <div className="stat"><div className="value">{formatNumber(data.totalTracks)}</div><div className="label">Morceaux</div></div>
        <div className="stat"><div className="value">{formatNumber(data.publicTracks)}</div><div className="label">Publics</div></div>
        <div className="stat"><div className="value">{formatNumber(data.hiddenTracks)}</div><div className="label">Masqués</div></div>
        <div className="stat"><div className="value">{formatNumber(data.totalPlaylists)}</div><div className="label">Playlists</div></div>
        <div className="stat"><div className="value">{formatNumber(data.totalComments)}</div><div className="label">Commentaires</div></div>
        <div className="stat"><div className="value">{formatNumber(data.totalPlays)}</div><div className="label">Écoutes</div></div>
        <div className="stat"><div className="value">{formatNumber(data.totalLikes)}</div><div className="label">Likes</div></div>
        <div className="stat"><div className="value">{formatNumber(data.pendingReports)}</div><div className="label">Signalements en attente</div></div>
        <div className="stat"><div className="value">{formatBytes(data.storageBytesUsed)}</div><div className="label">Stockage audio</div></div>
      </div>

      <section className="section">
        <h2>Écoutes sur 30 jours</h2>
        {data.playsLast30Days.length === 0 ? (
          <Empty>Aucune écoute enregistrée.</Empty>
        ) : (
          <div className="chart" aria-hidden="true">
            {data.playsLast30Days.map((point) => (
              <div
                key={point.date}
                className="bar"
                style={{ height: `${(point.plays / max) * 100}%` }}
                title={`${point.date} : ${point.plays}`}
              />
            ))}
          </div>
        )}
      </section>
    </>
  );
}

/** File de modération des signalements. */
export function AdminReportsPage() {
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<ReportStatus | ''>('Pending');
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<string | null>(null);

  const { data, isLoading, error } = useQuery({
    queryKey: ['admin-reports', status, page],
    queryFn: () => adminApi.reports({ status: status || undefined, page, pageSize: 25 }),
  });

  return (
    <>
      <div className="field" style={{ maxWidth: 240 }}>
        <label htmlFor="report-status">Filtrer par statut</label>
        <select
          id="report-status"
          value={status}
          onChange={(event) => {
            setStatus(event.target.value as ReportStatus | '');
            setPage(1);
          }}
        >
          <option value="">Tous</option>
          <option value="Pending">En attente</option>
          <option value="Reviewing">En cours d'examen</option>
          <option value="Resolved">Résolus</option>
          <option value="Rejected">Rejetés</option>
        </select>
      </div>

      <ErrorMessage error={error} />

      {isLoading ? (
        <Loading rows={3} />
      ) : data && data.items.length > 0 ? (
        <>
          <div className="table-wrapper">
            <table>
              <caption className="sr-only">Signalements</caption>
              <thead>
                <tr>
                  <th scope="col">Date</th>
                  <th scope="col">Cible</th>
                  <th scope="col">Motif</th>
                  <th scope="col">Auteur</th>
                  <th scope="col">Statut</th>
                  <th scope="col">Action</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((report) => (
                  <tr key={report.id}>
                    <td>{formatDateTime(report.createdAt)}</td>
                    <td>
                      <span className="badge">{report.targetType}</span>{' '}
                      {report.targetType === 'Track' ? (
                        <Link to={`/tracks/${report.targetId}`}>{report.targetLabel ?? report.targetId}</Link>
                      ) : (
                        (report.targetLabel ?? report.targetId)
                      )}
                    </td>
                    <td>{report.reason}</td>
                    <td>{report.reporter?.username ?? '—'}</td>
                    <td>
                      <span className={`badge${report.status === 'Pending' ? ' badge-warning' : ''}`}>{report.status}</span>
                    </td>
                    <td>
                      <button type="button" className="btn btn-sm" onClick={() => setSelected(report.id)}>
                        Traiter
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pagination page={page} totalPages={data.totalPages} onChange={setPage} />
        </>
      ) : (
        <Empty>Aucun signalement pour ce filtre.</Empty>
      )}

      {selected && (
        <ResolveReportDialog
          reportId={selected}
          onClose={() => setSelected(null)}
          onResolved={() => {
            queryClient.invalidateQueries({ queryKey: ['admin-reports'] });
            queryClient.invalidateQueries({ queryKey: ['admin-stats'] });
            setSelected(null);
          }}
        />
      )}
    </>
  );
}

/** Formulaire de traitement d'un signalement. */
function ResolveReportDialog({
  reportId,
  onClose,
  onResolved,
}: {
  reportId: string;
  onClose: () => void;
  onResolved: () => void;
}) {
  const { data } = useQuery({ queryKey: ['admin-report', reportId], queryFn: () => adminApi.report(reportId) });
  const [status, setStatus] = useState<ReportStatus>('Resolved');
  const [note, setNote] = useState('');
  const [hideTarget, setHideTarget] = useState(false);

  const mutation = useMutation({
    mutationFn: () => adminApi.resolveReport(reportId, { status, resolutionNote: note, hideTarget }),
    onSuccess: onResolved,
  });

  return (
    <Dialog title="Traiter le signalement" onClose={onClose}>
      <ErrorMessage error={mutation.error} />

      {data && (
        <dl className="small" style={{ marginBottom: 16 }}>
          <dt className="muted">Cible</dt>
          <dd>
            {data.targetType} — {data.targetLabel ?? data.targetId}
          </dd>
          <dt className="muted">Motif</dt>
          <dd>{data.reason}</dd>
          {data.description && (
            <>
              <dt className="muted">Description</dt>
              <dd style={{ whiteSpace: 'pre-wrap' }}>{data.description}</dd>
            </>
          )}
        </dl>
      )}

      <div className="field">
        <label htmlFor="resolve-status">Nouveau statut</label>
        <select id="resolve-status" value={status} onChange={(event) => setStatus(event.target.value as ReportStatus)}>
          <option value="Reviewing">En cours d'examen</option>
          <option value="Resolved">Résolu</option>
          <option value="Rejected">Rejeté</option>
        </select>
      </div>

      <div className="field">
        <label htmlFor="resolve-note">Justification</label>
        <textarea id="resolve-note" value={note} maxLength={2000} onChange={(event) => setNote(event.target.value)} />
      </div>

      <div className="field checkbox">
        <input
          id="resolve-hide"
          type="checkbox"
          checked={hideTarget}
          onChange={(event) => setHideTarget(event.target.checked)}
        />
        <label htmlFor="resolve-hide">Masquer également le contenu signalé</label>
      </div>

      <button type="button" className="btn btn-primary" onClick={() => mutation.mutate()} disabled={mutation.isPending}>
        Enregistrer la décision
      </button>
    </Dialog>
  );
}

/** Gestion globale des morceaux. */
export function AdminTracksPage() {
  const queryClient = useQueryClient();
  const [search, setSearch] = useState('');
  const debounced = useDebounced(search);
  const [page, setPage] = useState(1);

  const { data, isLoading, error } = useQuery({
    queryKey: ['admin-tracks', debounced, page],
    queryFn: () => adminApi.tracks({ q: debounced || undefined, page, pageSize: 25 }),
  });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['admin-tracks'] });

  const hideMutation = useMutation({ mutationFn: adminApi.hideTrack, onSuccess: invalidate });
  const restoreMutation = useMutation({ mutationFn: adminApi.restoreTrack, onSuccess: invalidate });
  const deleteMutation = useMutation({ mutationFn: adminApi.deleteTrack, onSuccess: invalidate });

  return (
    <>
      <div className="field" style={{ maxWidth: 320 }}>
        <label htmlFor="admin-track-search">Rechercher</label>
        <input
          id="admin-track-search"
          type="search"
          value={search}
          onChange={(event) => {
            setSearch(event.target.value);
            setPage(1);
          }}
          placeholder="Titre, artiste ou pseudo"
        />
      </div>

      <ErrorMessage error={error ?? hideMutation.error ?? restoreMutation.error ?? deleteMutation.error} />

      {isLoading ? (
        <Loading rows={3} />
      ) : data && data.items.length > 0 ? (
        <>
          <div className="table-wrapper">
            <table>
              <caption className="sr-only">Morceaux de la plateforme</caption>
              <thead>
                <tr>
                  <th scope="col">Titre</th>
                  <th scope="col">Propriétaire</th>
                  <th scope="col">Visibilité</th>
                  <th scope="col">État</th>
                  <th scope="col">Écoutes</th>
                  <th scope="col">Likes</th>
                  <th scope="col">Actions</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((track) => (
                  <tr key={track.id}>
                    <td>
                      <Link to={`/tracks/${track.id}`}>{track.title}</Link>
                      {track.isHidden && <span className="badge badge-danger" style={{ marginLeft: 8 }}>Masqué</span>}
                    </td>
                    <td>
                      <Link to={`/users/${track.owner.username}`}>{track.owner.username}</Link>
                    </td>
                    <td>{track.visibility}</td>
                    <td>{track.status}</td>
                    <td>{formatNumber(track.playCount)}</td>
                    <td>{formatNumber(track.likeCount)}</td>
                    <td>
                      <div className="row" style={{ gap: 4 }}>
                        {track.isHidden ? (
                          <button type="button" className="btn btn-sm" onClick={() => restoreMutation.mutate(track.id)}>
                            Restaurer
                          </button>
                        ) : (
                          <button type="button" className="btn btn-sm" onClick={() => hideMutation.mutate(track.id)}>
                            Masquer
                          </button>
                        )}
                        <button
                          type="button"
                          className="btn btn-sm btn-danger"
                          onClick={() => {
                            if (window.confirm(`Supprimer définitivement « ${track.title} » ?`)) {
                              deleteMutation.mutate(track.id);
                            }
                          }}
                        >
                          Supprimer
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pagination page={page} totalPages={data.totalPages} onChange={setPage} />
        </>
      ) : (
        <Empty>Aucun morceau trouvé.</Empty>
      )}
    </>
  );
}

/** Gestion des comptes utilisateurs. */
export function AdminUsersPage() {
  const queryClient = useQueryClient();
  const [search, setSearch] = useState('');
  const debounced = useDebounced(search);
  const [page, setPage] = useState(1);

  const { data, isLoading, error } = useQuery({
    queryKey: ['admin-users', debounced, page],
    queryFn: () => adminApi.users({ q: debounced || undefined, page, pageSize: 25 }),
  });

  const updateMutation = useMutation({
    mutationFn: ({ userId, body }: { userId: string; body: { role?: string; status?: string } }) =>
      adminApi.updateUser(userId, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin-users'] }),
  });

  const deleteMutation = useMutation({
    mutationFn: adminApi.deleteUser,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin-users'] }),
  });

  return (
    <>
      <div className="field" style={{ maxWidth: 320 }}>
        <label htmlFor="admin-user-search">Rechercher</label>
        <input
          id="admin-user-search"
          type="search"
          value={search}
          onChange={(event) => {
            setSearch(event.target.value);
            setPage(1);
          }}
          placeholder="Pseudo ou email"
        />
      </div>

      <ErrorMessage error={error ?? updateMutation.error ?? deleteMutation.error} />

      {isLoading ? (
        <Loading rows={3} />
      ) : data && data.items.length > 0 ? (
        <>
          <div className="table-wrapper">
            <table>
              <caption className="sr-only">Comptes utilisateurs</caption>
              <thead>
                <tr>
                  <th scope="col">Pseudo</th>
                  <th scope="col">Email</th>
                  <th scope="col">Rôle</th>
                  <th scope="col">Statut</th>
                  <th scope="col">Morceaux</th>
                  <th scope="col">Abonnés</th>
                  <th scope="col">Inscrit le</th>
                  <th scope="col">Actions</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((user) => (
                  <tr key={user.id}>
                    <td>
                      <Link to={`/users/${user.username}`}>{user.username}</Link>
                    </td>
                    <td>{user.email}</td>
                    <td>
                      <select
                        aria-label={`Rôle de ${user.username}`}
                        value={user.role}
                        onChange={(event) =>
                          updateMutation.mutate({ userId: user.id, body: { role: event.target.value as UserRole } })
                        }
                      >
                        <option value="User">User</option>
                        <option value="Artist">Artist</option>
                        <option value="Moderator">Moderator</option>
                        <option value="Admin">Admin</option>
                      </select>
                    </td>
                    <td>
                      <select
                        aria-label={`Statut de ${user.username}`}
                        value={user.status}
                        onChange={(event) =>
                          updateMutation.mutate({ userId: user.id, body: { status: event.target.value as UserStatus } })
                        }
                      >
                        <option value="Active">Active</option>
                        <option value="Suspended">Suspended</option>
                      </select>
                    </td>
                    <td>{formatNumber(user.trackCount)}</td>
                    <td>{formatNumber(user.followerCount)}</td>
                    <td>{formatDateTime(user.createdAt)}</td>
                    <td>
                      <button
                        type="button"
                        className="btn btn-sm btn-danger"
                        disabled={Boolean(user.deletedAt)}
                        onClick={() => {
                          if (window.confirm(`Supprimer définitivement le compte « ${user.username} » ?`)) {
                            deleteMutation.mutate(user.id);
                          }
                        }}
                      >
                        Supprimer
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pagination page={page} totalPages={data.totalPages} onChange={setPage} />
        </>
      ) : (
        <Empty>Aucun utilisateur trouvé.</Empty>
      )}
    </>
  );
}

/** Gestion du référentiel de genres. */
export function AdminGenresPage() {
  const queryClient = useQueryClient();
  const [name, setName] = useState('');

  const { data, isLoading, error } = useQuery({ queryKey: ['admin-genres'], queryFn: discoveryApi.genres });

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ['admin-genres'] });
    queryClient.invalidateQueries({ queryKey: ['genres'] });
  };

  const createMutation = useMutation({
    mutationFn: () => adminApi.createGenre(name),
    onSuccess: () => {
      setName('');
      invalidate();
    },
  });

  const deleteMutation = useMutation({ mutationFn: adminApi.deleteGenre, onSuccess: invalidate });

  const renameMutation = useMutation({
    mutationFn: ({ id, value }: { id: string; value: string }) => adminApi.updateGenre(id, value),
    onSuccess: invalidate,
  });

  return (
    <>
      <form
        className="row"
        style={{ marginBottom: 24, maxWidth: 420 }}
        onSubmit={(event) => {
          event.preventDefault();
          createMutation.mutate();
        }}
      >
        <div className="field grow" style={{ marginBottom: 0 }}>
          <label htmlFor="new-genre">Nouveau genre</label>
          <input id="new-genre" value={name} minLength={2} maxLength={60} onChange={(event) => setName(event.target.value)} />
        </div>
        <button type="submit" className="btn btn-primary" disabled={!name.trim() || createMutation.isPending}>
          Ajouter
        </button>
      </form>

      <ErrorMessage error={error ?? createMutation.error ?? deleteMutation.error ?? renameMutation.error} />

      {isLoading ? (
        <Loading rows={3} />
      ) : (
        <div className="table-wrapper">
          <table>
            <caption className="sr-only">Genres musicaux</caption>
            <thead>
              <tr>
                <th scope="col">Nom</th>
                <th scope="col">Slug</th>
                <th scope="col">Morceaux publics</th>
                <th scope="col">Actions</th>
              </tr>
            </thead>
            <tbody>
              {data?.map((genre) => (
                <tr key={genre.id}>
                  <td>{genre.name}</td>
                  <td className="muted">{genre.slug}</td>
                  <td>{formatNumber(genre.trackCount ?? 0)}</td>
                  <td>
                    <div className="row" style={{ gap: 4 }}>
                      <button
                        type="button"
                        className="btn btn-sm"
                        onClick={() => {
                          const value = window.prompt('Nouveau nom du genre', genre.name);
                          if (value?.trim()) {
                            renameMutation.mutate({ id: genre.id, value: value.trim() });
                          }
                        }}
                      >
                        Renommer
                      </button>
                      <button
                        type="button"
                        className="btn btn-sm btn-danger"
                        disabled={(genre.trackCount ?? 0) > 0}
                        title={(genre.trackCount ?? 0) > 0 ? 'Ce genre est encore utilisé' : undefined}
                        onClick={() => deleteMutation.mutate(genre.id)}
                      >
                        Supprimer
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  );
}

/** Journal des actions d'administration. */
export function AdminAuditLogsPage() {
  const [action, setAction] = useState('');
  const debounced = useDebounced(action);
  const [page, setPage] = useState(1);

  const { data, isLoading, error } = useQuery({
    queryKey: ['admin-audit', debounced, page],
    queryFn: () => adminApi.auditLogs({ action: debounced || undefined, page, pageSize: 30 }),
  });

  return (
    <>
      <div className="field" style={{ maxWidth: 320 }}>
        <label htmlFor="audit-filter">Filtrer par action</label>
        <input
          id="audit-filter"
          type="search"
          value={action}
          onChange={(event) => {
            setAction(event.target.value);
            setPage(1);
          }}
          placeholder="TRACK_HIDDEN, USER_UPDATED…"
        />
      </div>

      <ErrorMessage error={error} />

      {isLoading ? (
        <Loading rows={3} />
      ) : data && data.items.length > 0 ? (
        <>
          <div className="table-wrapper">
            <table>
              <caption className="sr-only">Journal d'audit</caption>
              <thead>
                <tr>
                  <th scope="col">Date</th>
                  <th scope="col">Acteur</th>
                  <th scope="col">Action</th>
                  <th scope="col">Cible</th>
                  <th scope="col">Détails</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((log) => (
                  <tr key={log.id}>
                    <td>{formatDateTime(log.createdAt)}</td>
                    <td>{log.actor?.username ?? <span className="muted">système</span>}</td>
                    <td>
                      <span className="badge">{log.action}</span>
                    </td>
                    <td className="muted small">
                      {log.targetType} {log.targetId?.slice(0, 8)}
                    </td>
                    <td className="muted small" style={{ maxWidth: 320, whiteSpace: 'normal' }}>
                      {log.metadata}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pagination page={page} totalPages={data.totalPages} onChange={setPage} />
        </>
      ) : (
        <Empty>Aucune entrée dans le journal.</Empty>
      )}
    </>
  );
}
