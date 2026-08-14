import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { meApi } from '../services/api';
import { ErrorMessage, Empty, Loading } from '../components/common';
import { formatDate, formatNumber } from '../lib/format';
import type { AnalyticsGroupBy, PlaysPoint } from '../types/api';

/** Tuile de statistique. */
function Stat({ label, value }: { label: string; value: string | number }) {
  return (
    <div className="stat">
      <div className="value">{typeof value === 'number' ? formatNumber(value) : value}</div>
      <div className="label">{label}</div>
    </div>
  );
}

/**
 * Histogramme des écoutes.
 *
 * Il est dessiné en CSS plutôt qu'avec une bibliothèque de graphiques : la série est
 * simple et cela évite une dépendance supplémentaire. Un tableau équivalent est fourni
 * aux lecteurs d'écran.
 */
function PlaysChart({ points }: { points: PlaysPoint[] }) {
  if (points.length === 0) {
    return <Empty>Aucune écoute sur la période.</Empty>;
  }

  const max = Math.max(...points.map((point) => point.plays), 1);

  return (
    <figure style={{ margin: 0 }}>
      <div className="chart" aria-hidden="true">
        {points.map((point) => (
          <div
            key={point.date}
            className="bar"
            style={{ height: `${(point.plays / max) * 100}%` }}
            title={`${point.date} : ${point.plays} écoutes`}
          />
        ))}
      </div>

      <figcaption className="small muted" style={{ marginTop: 8 }}>
        Du {formatDate(points[0].date)} au {formatDate(points[points.length - 1].date)} — maximum {max} écoutes.
      </figcaption>

      <table className="sr-only">
        <caption>Écoutes par période</caption>
        <thead>
          <tr>
            <th scope="col">Date</th>
            <th scope="col">Écoutes</th>
            <th scope="col">Auditeurs uniques</th>
          </tr>
        </thead>
        <tbody>
          {points.map((point) => (
            <tr key={point.date}>
              <td>{point.date}</td>
              <td>{point.plays}</td>
              <td>{point.uniqueListeners}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </figure>
  );
}

/** Tableau de bord de l'artiste. */
export function AnalyticsPage() {
  const [groupBy, setGroupBy] = useState<AnalyticsGroupBy>('Day');

  const overview = useQuery({ queryKey: ['analytics-overview'], queryFn: meApi.analyticsOverview });
  const series = useQuery({
    queryKey: ['analytics-plays', groupBy],
    queryFn: () => meApi.analyticsPlays(undefined, undefined, groupBy),
  });
  const perTrack = useQuery({
    queryKey: ['analytics-tracks'],
    queryFn: () => meApi.analyticsTracks({ pageSize: 50 }),
  });

  return (
    <>
      <h1>Statistiques</h1>

      <ErrorMessage error={overview.error ?? series.error ?? perTrack.error} />

      {overview.isLoading ? (
        <Loading rows={2} />
      ) : overview.data ? (
        <section className="section">
          <div className="stat-grid">
            <Stat label="Morceaux" value={overview.data.trackCount} />
            <Stat label="Morceaux publics" value={overview.data.publicTrackCount} />
            <Stat label="Écoutes totales" value={overview.data.totalPlays} />
            <Stat label="Écoutes (30 j)" value={overview.data.playsLast30Days} />
            <Stat label="Likes" value={overview.data.totalLikes} />
            <Stat label="Abonnés" value={overview.data.followerCount} />
            <Stat label="Commentaires reçus" value={overview.data.commentCount} />
          </div>
        </section>
      ) : null}

      <section className="section">
        <div className="section-header">
          <h2>Évolution des écoutes</h2>
          <div className="field" style={{ marginBottom: 0 }}>
            <label htmlFor="analytics-groupby" className="sr-only">
              Granularité
            </label>
            <select
              id="analytics-groupby"
              value={groupBy}
              onChange={(event) => setGroupBy(event.target.value as AnalyticsGroupBy)}
            >
              <option value="Day">Par jour</option>
              <option value="Week">Par semaine</option>
              <option value="Month">Par mois</option>
            </select>
          </div>
        </div>

        {series.isLoading ? <Loading rows={1} /> : <PlaysChart points={series.data?.points ?? []} />}
      </section>

      <section className="section">
        <h2>Détail par morceau</h2>

        {perTrack.isLoading ? (
          <Loading rows={3} />
        ) : perTrack.data && perTrack.data.items.length > 0 ? (
          <div className="table-wrapper">
            <table>
              <caption className="sr-only">Statistiques détaillées de chaque morceau</caption>
              <thead>
                <tr>
                  <th scope="col">Morceau</th>
                  <th scope="col">Visibilité</th>
                  <th scope="col">Écoutes</th>
                  <th scope="col">Likes</th>
                  <th scope="col">Commentaires</th>
                  <th scope="col">Playlists</th>
                  <th scope="col">Publié le</th>
                </tr>
              </thead>
              <tbody>
                {perTrack.data.items.map((row) => (
                  <tr key={row.trackId}>
                    <td>
                      <Link to={`/tracks/${row.trackId}`}>{row.title}</Link>
                    </td>
                    <td>{row.visibility}</td>
                    <td>{formatNumber(row.playCount)}</td>
                    <td>{formatNumber(row.likeCount)}</td>
                    <td>{formatNumber(row.commentCount)}</td>
                    <td>{formatNumber(row.playlistCount)}</td>
                    <td>{formatDate(row.createdAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <Empty>Importez un morceau pour commencer à collecter des statistiques.</Empty>
        )}
      </section>
    </>
  );
}
