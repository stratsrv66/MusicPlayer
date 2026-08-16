import { query, request } from './apiClient';
import type {
  AdminStatistics,
  AdminTrack,
  AdminUser,
  AnalyticsGroupBy,
  AnalyticsOverview,
  AuditLog,
  AuthResponse,
  Comment,
  Genre,
  ExternalPlaylist,
  HistoryEntry,
  Home,
  ImportYoutubeRequest,
  LikeState,
  Me,
  Paged,
  PlaybackProgress,
  Playlist,
  PlaylistDetails,
  PlaylistImport,
  PlaylistImportDetails,
  PlaylistPreview,
  PlaysSeries,
  RegisterPlayResult,
  Report,
  ReportStatus,
  SearchResult,
  SearchType,
  StartPlaylistImportRequest,
  Tag,
  Track,
  TrackAnalytics,
  TrackDetails,
  UploadAccepted,
  UserExport,
  UserProfile,
  UserSettings,
  UserSummary,
} from '../types/api';

export interface PageParams {
  page?: number;
  pageSize?: number;
}

export interface TrackListParams extends PageParams {
  q?: string;
  genre?: string;
  tag?: string;
  artist?: string;
  minDuration?: number;
  maxDuration?: number;
  from?: string;
  to?: string;
  sort?: string;
}

/** Authentification et gestion de session. */
export const authApi = {
  register: (email: string, username: string, password: string) =>
    request<AuthResponse>('/auth/register', { method: 'POST', body: { email, username, password }, anonymous: true }),

  login: (email: string, password: string) =>
    request<AuthResponse>('/auth/login', { method: 'POST', body: { email, password }, anonymous: true }),

  logout: (refreshToken: string) =>
    request<void>('/auth/logout', { method: 'POST', body: { refreshToken } }),
};

/** Compte de l'utilisateur connecté. */
export const meApi = {
  get: () => request<Me>('/me'),

  update: (body: Partial<{ username: string; bio: string; socialLinks: Record<string, string>; profileVisibility: string }>) =>
    request<Me>('/me', { method: 'PATCH', body }),

  setAvatar: (file: File) => {
    const form = new FormData();
    form.append('file', file);
    return request<Me>('/me/avatar', { method: 'POST', formData: form });
  },

  removeAvatar: () => request<Me>('/me/avatar', { method: 'DELETE' }),

  getSettings: () => request<UserSettings>('/me/settings'),

  updateSettings: (body: Partial<UserSettings>) =>
    request<UserSettings>('/me/settings', { method: 'PATCH', body }),

  tracks: (params: PageParams = {}) => request<Paged<Track>>(`/me/tracks${query({ ...params })}`),

  playlists: (params: PageParams = {}) => request<Paged<Playlist>>(`/me/playlists${query({ ...params })}`),

  favorites: (params: PageParams = {}) => request<Paged<Playlist>>(`/me/favorites${query({ ...params })}`),

  likes: (params: PageParams = {}) => request<Paged<Track>>(`/me/likes${query({ ...params })}`),

  history: (params: PageParams = {}) => request<Paged<HistoryEntry>>(`/me/history${query({ ...params })}`),

  clearHistory: () => request<void>('/me/history', { method: 'DELETE' }),

  followers: (params: PageParams = {}) => request<Paged<UserSummary>>(`/me/followers${query({ ...params })}`),

  following: (params: PageParams = {}) => request<Paged<UserSummary>>(`/me/following${query({ ...params })}`),

  reports: (params: PageParams = {}) => request<Paged<Report>>(`/me/reports${query({ ...params })}`),

  analyticsOverview: () => request<AnalyticsOverview>('/me/analytics/overview'),

  analyticsTracks: (params: PageParams = {}) =>
    request<Paged<TrackAnalytics>>(`/me/analytics/tracks${query({ ...params })}`),

  analyticsPlays: (from?: string, to?: string, groupBy: AnalyticsGroupBy = 'Day') =>
    request<PlaysSeries>(`/me/analytics/plays${query({ from, to, groupBy })}`),

  topTracks: (limit = 10) => request<TrackAnalytics[]>(`/me/analytics/top-tracks${query({ limit })}`),

  requestExport: () => request<UserExport>('/me/data-export', { method: 'POST' }),

  exports: (params: PageParams = {}) => request<Paged<UserExport>>(`/me/data-exports${query({ ...params })}`),

  export: (id: string) => request<UserExport>(`/me/data-exports/${id}`),

  deleteAccount: (confirmUsername: string) =>
    request<void>('/me', { method: 'DELETE', body: { confirm: true, confirmUsername } }),
};

/** Profils publics et abonnements. */
export const usersApi = {
  get: (username: string) => request<UserProfile>(`/users/${encodeURIComponent(username)}`),

  tracks: (username: string, params: PageParams = {}) =>
    request<Paged<Track>>(`/users/${encodeURIComponent(username)}/tracks${query({ ...params })}`),

  playlists: (username: string, params: PageParams = {}) =>
    request<Paged<Playlist>>(`/users/${encodeURIComponent(username)}/playlists${query({ ...params })}`),

  follow: (userId: string) => request<void>(`/users/${userId}/follow`, { method: 'POST' }),

  unfollow: (userId: string) => request<void>(`/users/${userId}/follow`, { method: 'DELETE' }),

  followers: (userId: string, params: PageParams = {}) =>
    request<Paged<UserSummary>>(`/users/${userId}/followers${query({ ...params })}`),

  following: (userId: string, params: PageParams = {}) =>
    request<Paged<UserSummary>>(`/users/${userId}/following${query({ ...params })}`),
};

/** Morceaux, upload, écoutes, likes et commentaires. */
export const tracksApi = {
  list: (params: TrackListParams = {}) => request<Paged<Track>>(`/tracks${query({ ...params })}`),

  get: (trackId: string) => request<TrackDetails>(`/tracks/${trackId}`),

  create: (form: FormData) => request<UploadAccepted>('/tracks', { method: 'POST', formData: form }),

  /** Importe un morceau depuis un lien YouTube : audio et pochette sont récupérés par le serveur. */
  importFromYoutube: (body: ImportYoutubeRequest) =>
    request<UploadAccepted>('/tracks/import/youtube', { method: 'POST', body }),

  replaceFile: (trackId: string, file: File) => {
    const form = new FormData();
    form.append('file', file);
    return request<UploadAccepted>(`/tracks/${trackId}/upload`, { method: 'POST', formData: form });
  },

  update: (trackId: string, body: Record<string, unknown>) =>
    request<TrackDetails>(`/tracks/${trackId}`, { method: 'PATCH', body }),

  remove: (trackId: string) => request<void>(`/tracks/${trackId}`, { method: 'DELETE' }),

  publish: (trackId: string) => request<TrackDetails>(`/tracks/${trackId}/publish`, { method: 'POST' }),

  unpublish: (trackId: string) => request<TrackDetails>(`/tracks/${trackId}/unpublish`, { method: 'POST' }),

  setCover: (trackId: string, file: File) => {
    const form = new FormData();
    form.append('file', file);
    return request<void>(`/tracks/${trackId}/cover`, { method: 'POST', formData: form });
  },

  removeCover: (trackId: string) => request<void>(`/tracks/${trackId}/cover`, { method: 'DELETE' }),

  like: (trackId: string) => request<LikeState>(`/tracks/${trackId}/like`, { method: 'POST' }),

  unlike: (trackId: string) => request<LikeState>(`/tracks/${trackId}/like`, { method: 'DELETE' }),

  likeState: (trackId: string) => request<LikeState>(`/tracks/${trackId}/like`),

  registerPlay: (trackId: string, body: { sessionId: string; positionSeconds: number; durationSeconds: number; source: string }) =>
    request<RegisterPlayResult>(`/tracks/${trackId}/plays`, { method: 'POST', body }),

  saveProgress: (trackId: string, positionSeconds: number) =>
    request<PlaybackProgress>(`/tracks/${trackId}/progress`, { method: 'PUT', body: { positionSeconds } }),

  progress: (trackId: string) => request<PlaybackProgress>(`/tracks/${trackId}/progress`),

  comments: (trackId: string, params: PageParams = {}) =>
    request<Paged<Comment>>(`/tracks/${trackId}/comments${query({ ...params })}`),

  addComment: (trackId: string, content: string, timestampSeconds?: number | null) =>
    request<Comment>(`/tracks/${trackId}/comments`, { method: 'POST', body: { content, timestampSeconds } }),
};

/** Import de playlists YouTube. */
export const importsApi = {
  /** Décrit une playlist et ses morceaux sans rien importer. */
  preview: (url: string) =>
    request<PlaylistPreview>('/imports/playlists/preview', { method: 'POST', body: { url } }),

  /** Liste les playlists publiques d'une chaîne. */
  profilePlaylists: (profileId: string) =>
    request<ExternalPlaylist[]>(`/imports/playlists/profile${query({ profileId })}`),

  /** Programme l'import d'une playlist. */
  start: (body: StartPlaylistImportRequest) =>
    request<PlaylistImport>('/imports/playlists', { method: 'POST', body }),

  /** Imports récents de l'utilisateur. */
  list: () => request<PlaylistImport[]>('/imports/playlists'),

  /** Progression d'un import et état de chacun de ses morceaux. */
  get: (importId: string) => request<PlaylistImportDetails>(`/imports/playlists/${importId}`),

  cancel: (importId: string) =>
    request<PlaylistImport>(`/imports/playlists/${importId}/cancel`, { method: 'POST' }),

  retry: (importId: string) =>
    request<PlaylistImport>(`/imports/playlists/${importId}/retry`, { method: 'POST' }),
};

/** Modification et suppression des commentaires. */
export const commentsApi = {
  update: (commentId: string, content: string) =>
    request<Comment>(`/comments/${commentId}`, { method: 'PATCH', body: { content } }),

  remove: (commentId: string) => request<void>(`/comments/${commentId}`, { method: 'DELETE' }),
};

/** Playlists. */
export const playlistsApi = {
  list: (params: PageParams & { ownerId?: string; sort?: string } = {}) =>
    request<Paged<Playlist>>(`/playlists${query({ ...params })}`),

  get: (playlistId: string) => request<PlaylistDetails>(`/playlists/${playlistId}`),

  create: (body: { name: string; description?: string; visibility: string }) =>
    request<Playlist>('/playlists', { method: 'POST', body }),

  update: (playlistId: string, body: Record<string, unknown>) =>
    request<Playlist>(`/playlists/${playlistId}`, { method: 'PATCH', body }),

  remove: (playlistId: string) => request<void>(`/playlists/${playlistId}`, { method: 'DELETE' }),

  setCover: (playlistId: string, file: File) => {
    const form = new FormData();
    form.append('file', file);
    return request<Playlist>(`/playlists/${playlistId}/cover`, { method: 'POST', formData: form });
  },

  addTrack: (playlistId: string, trackId: string) =>
    request<Playlist>(`/playlists/${playlistId}/tracks`, { method: 'POST', body: { trackId } }),

  removeTrack: (playlistId: string, trackId: string) =>
    request<Playlist>(`/playlists/${playlistId}/tracks/${trackId}`, { method: 'DELETE' }),

  reorder: (playlistId: string, items: { trackId: string; position: number }[]) =>
    request<Playlist>(`/playlists/${playlistId}/tracks/reorder`, { method: 'PATCH', body: { items } }),

  duplicate: (playlistId: string, body: { name?: string; visibility?: string } = {}) =>
    request<Playlist>(`/playlists/${playlistId}/duplicate`, { method: 'POST', body }),

  follow: (playlistId: string) => request<Playlist>(`/playlists/${playlistId}/follow`, { method: 'POST' }),

  unfollow: (playlistId: string) => request<Playlist>(`/playlists/${playlistId}/follow`, { method: 'DELETE' }),

  favorite: (playlistId: string) => request<Playlist>(`/playlists/${playlistId}/favorite`, { method: 'POST' }),

  unfavorite: (playlistId: string) => request<Playlist>(`/playlists/${playlistId}/favorite`, { method: 'DELETE' }),
};

/** Découverte : accueil, recherche, recommandations, genres et tags. */
export const discoveryApi = {
  home: () => request<Home>('/home'),

  search: (params: { q?: string; type?: SearchType } & TrackListParams) =>
    request<SearchResult>(`/search${query({ ...params })}`),

  recommendedTracks: (limit = 20) => request<Track[]>(`/recommendations/tracks${query({ limit })}`),

  recommendedArtists: (limit = 20) => request<UserSummary[]>(`/recommendations/artists${query({ limit })}`),

  genres: () => request<Genre[]>('/genres'),

  genreTracks: (genreId: string, params: PageParams & { sort?: string } = {}) =>
    request<Paged<Track>>(`/genres/${genreId}/tracks${query({ ...params })}`),

  tags: (params: PageParams & { q?: string } = {}) => request<Paged<Tag>>(`/tags${query({ ...params })}`),

  tagTracks: (tag: string, params: PageParams & { sort?: string } = {}) =>
    request<Paged<Track>>(`/tags/${encodeURIComponent(tag)}/tracks${query({ ...params })}`),
};

/** Signalements émis par les utilisateurs. */
export const reportsApi = {
  create: (body: { targetType: string; targetId: string; reason: string; description?: string }) =>
    request<Report>('/reports', { method: 'POST', body }),
};

/** Administration et modération. */
export const adminApi = {
  users: (params: PageParams & { q?: string; role?: string; status?: string } = {}) =>
    request<Paged<AdminUser>>(`/admin/users${query({ ...params })}`),

  user: (userId: string) => request<AdminUser>(`/admin/users/${userId}`),

  updateUser: (userId: string, body: { role?: string; status?: string }) =>
    request<AdminUser>(`/admin/users/${userId}`, { method: 'PATCH', body }),

  deleteUser: (userId: string) => request<void>(`/admin/users/${userId}`, { method: 'DELETE' }),

  tracks: (params: PageParams & { q?: string; includeDeleted?: boolean } = {}) =>
    request<Paged<AdminTrack>>(`/admin/tracks${query({ ...params })}`),

  hideTrack: (trackId: string) => request<void>(`/admin/tracks/${trackId}/hide`, { method: 'POST' }),

  restoreTrack: (trackId: string) => request<void>(`/admin/tracks/${trackId}/restore`, { method: 'POST' }),

  deleteTrack: (trackId: string) => request<void>(`/admin/tracks/${trackId}`, { method: 'DELETE' }),

  reports: (params: PageParams & { status?: ReportStatus; reason?: string; targetType?: string } = {}) =>
    request<Paged<Report>>(`/admin/reports${query({ ...params })}`),

  report: (reportId: string) => request<Report>(`/admin/reports/${reportId}`),

  resolveReport: (reportId: string, body: { status: string; resolutionNote?: string; hideTarget?: boolean }) =>
    request<Report>(`/admin/reports/${reportId}`, { method: 'PATCH', body }),

  auditLogs: (params: PageParams & { action?: string } = {}) =>
    request<Paged<AuditLog>>(`/admin/audit-logs${query({ ...params })}`),

  statistics: () => request<AdminStatistics>('/admin/statistics'),

  createGenre: (name: string) => request<Genre>('/admin/genres', { method: 'POST', body: { name } }),

  updateGenre: (genreId: string, name: string) =>
    request<Genre>(`/admin/genres/${genreId}`, { method: 'PATCH', body: { name } }),

  deleteGenre: (genreId: string) => request<void>(`/admin/genres/${genreId}`, { method: 'DELETE' }),
};
