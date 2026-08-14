import { useEffect } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter, Navigate, Outlet, Route, Routes } from 'react-router-dom';
import { setSessionExpiredHandler } from '../services/apiClient';
import { ApiError } from '../services/apiClient';
import { useAuthStore, useCanModerate } from '../features/auth/authStore';
import { Loading } from '../components/common';
import { AppShell } from './AppShell';
import { LoginPage, RegisterPage } from '../pages/AuthPages';
import { HomePage } from '../pages/HomePage';
import { SearchPage, TagPage } from '../pages/SearchPage';
import { TrackPage } from '../pages/TrackPage';
import { EditTrackPage, UploadPage } from '../pages/UploadPage';
import { PlaylistPage } from '../pages/PlaylistPage';
import { ProfilePage } from '../pages/ProfilePage';
import {
  MyFollowersPage,
  MyFollowingPage,
  MyHistoryPage,
  MyLikesPage,
  MyPlaylistsPage,
  MyProfilePage,
  MyTracksPage,
} from '../pages/LibraryPages';
import { AnalyticsPage } from '../pages/AnalyticsPage';
import { SettingsPage } from '../pages/SettingsPage';
import {
  AdminAuditLogsPage,
  AdminGenresPage,
  AdminLayout,
  AdminReportsPage,
  AdminStatisticsPage,
  AdminTracksPage,
  AdminUsersPage,
} from '../pages/AdminPages';

/**
 * Client de cache des données serveur.
 *
 * Les erreurs d'autorisation ne sont jamais retentées : une nouvelle tentative
 * échouerait de la même manière et retarderait l'affichage du message à l'utilisateur.
 */
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      refetchOnWindowFocus: false,
      retry: (failureCount, error) => {
        if (error instanceof ApiError && error.status >= 400 && error.status < 500) {
          return false;
        }
        return failureCount < 2;
      },
    },
  },
});

/** Restreint une route aux utilisateurs authentifiés. */
function RequireAuth() {
  const me = useAuthStore((state) => state.me);
  const loading = useAuthStore((state) => state.loading);

  if (loading) {
    return <Loading rows={3} />;
  }

  return me ? <Outlet /> : <Navigate to="/login" replace />;
}

/** Restreint une route aux modérateurs et administrateurs. */
function RequireModerator() {
  const loading = useAuthStore((state) => state.loading);
  const canModerate = useCanModerate();

  if (loading) {
    return <Loading rows={3} />;
  }

  return canModerate ? <Outlet /> : <Navigate to="/" replace />;
}

/** Page affichée pour une URL inconnue. */
function NotFoundPage() {
  return (
    <div className="empty">
      <h1>Page introuvable</h1>
      <p>Le contenu demandé n'existe pas ou n'est plus accessible.</p>
      <a className="btn btn-primary" href="/">
        Retour à l'accueil
      </a>
    </div>
  );
}

/** Racine de l'application : providers, restauration de session et routage. */
export function App() {
  const restore = useAuthStore((state) => state.restore);
  const clear = useAuthStore((state) => state.clear);

  // La session est restaurée une seule fois au démarrage.
  useEffect(() => {
    void restore();
    setSessionExpiredHandler(clear);
  }, [restore, clear]);

  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <Routes>
          <Route element={<AppShell />}>
            <Route index element={<HomePage />} />
            <Route path="login" element={<LoginPage />} />
            <Route path="register" element={<RegisterPage />} />
            <Route path="search" element={<SearchPage />} />
            <Route path="tags/:tag" element={<TagPage />} />
            <Route path="tracks/:trackId" element={<TrackPage />} />
            <Route path="playlists/:playlistId" element={<PlaylistPage />} />
            <Route path="users/:username" element={<ProfilePage />} />

            <Route element={<RequireAuth />}>
              <Route path="upload" element={<UploadPage />} />
              <Route path="tracks/:trackId/edit" element={<EditTrackPage />} />
              <Route path="me" element={<MyProfilePage />} />
              <Route path="me/tracks" element={<MyTracksPage />} />
              <Route path="me/playlists" element={<MyPlaylistsPage />} />
              <Route path="me/likes" element={<MyLikesPage />} />
              <Route path="me/history" element={<MyHistoryPage />} />
              <Route path="me/followers" element={<MyFollowersPage />} />
              <Route path="me/following" element={<MyFollowingPage />} />
              <Route path="me/analytics" element={<AnalyticsPage />} />
              <Route path="me/settings" element={<SettingsPage />} />
            </Route>

            <Route element={<RequireModerator />}>
              <Route path="admin" element={<AdminLayout />}>
                <Route index element={<AdminStatisticsPage />} />
                <Route path="reports" element={<AdminReportsPage />} />
                <Route path="tracks" element={<AdminTracksPage />} />
                <Route path="users" element={<AdminUsersPage />} />
                <Route path="genres" element={<AdminGenresPage />} />
                <Route path="audit-logs" element={<AdminAuditLogsPage />} />
              </Route>
            </Route>

            <Route path="*" element={<NotFoundPage />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </QueryClientProvider>
  );
}
