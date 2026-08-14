/**
 * Contrats de l'API, alignés sur les DTO exposés par le backend.
 * Les énumérations circulent sous forme de chaînes.
 */

export type ProfileVisibility = 'Public' | 'Private';
export type ContentVisibility = 'Public' | 'Unlisted' | 'Private';
export type TrackStatus = 'Uploading' | 'Processing' | 'Ready' | 'Failed';
export type UserRole = 'User' | 'Artist' | 'Moderator' | 'Admin';
export type UserStatus = 'Active' | 'Suspended';
export type ReportTargetType = 'Track' | 'Comment' | 'User' | 'Playlist';
export type ReportReason = 'Copyright' | 'Offensive' | 'Spam' | 'Other';
export type ReportStatus = 'Pending' | 'Reviewing' | 'Resolved' | 'Rejected';
export type ExportStatus = 'Pending' | 'Processing' | 'Ready' | 'Failed' | 'Expired';
export type SearchType = 'All' | 'Track' | 'User' | 'Album' | 'Playlist' | 'Tag';
export type AnalyticsGroupBy = 'Day' | 'Week' | 'Month';

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
}

export interface UserRef {
  id: string;
  username: string;
  avatarUrl?: string | null;
}

export interface UserSummary {
  id: string;
  username: string;
  avatarUrl?: string | null;
  followerCount: number;
  trackCount: number;
}

export interface UserProfile {
  id: string;
  username: string;
  bio?: string | null;
  avatarUrl?: string | null;
  socialLinks?: Record<string, string> | null;
  profileVisibility: ProfileVisibility;
  role: UserRole;
  createdAt: string;
  trackCount: number;
  playlistCount: number;
  followerCount: number;
  followingCount: number;
  isFollowedByCurrentUser?: boolean | null;
  isRestricted: boolean;
}

export interface UserSettings {
  showLikeCount: boolean;
  showPlayCount: boolean;
}

export interface Me {
  profile: UserProfile;
  email: string;
  settings: UserSettings;
  status: UserStatus;
}

export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
  user: UserProfile;
}

export interface Genre {
  id: string;
  name: string;
  slug: string;
  trackCount?: number | null;
}

export interface Tag {
  id: string;
  name: string;
  slug: string;
  trackCount?: number | null;
}

export interface Album {
  id: string;
  name: string;
  artistName: string;
  trackCount?: number | null;
}

export interface CoverUrls {
  small: string;
  medium: string;
  large: string;
}

export interface Track {
  id: string;
  title: string;
  artistName: string;
  durationSeconds: number;
  visibility: ContentVisibility;
  status: TrackStatus;
  owner: UserRef;
  genre?: Genre | null;
  tags: string[];
  coverUrls: CoverUrls;
  streamUrl: string;
  likeCount?: number | null;
  playCount?: number | null;
  isLikedByCurrentUser?: boolean | null;
  createdAt: string;
  publishedAt?: string | null;
}

export interface TrackDetails {
  track: Track;
  description?: string | null;
  year?: number | null;
  album?: Album | null;
  commentCount: number;
  failureReason?: string | null;
  isHidden?: boolean | null;
}

export interface UploadAccepted {
  trackId: string;
  uploadOperationId: string;
  status: TrackStatus;
}

export interface LikeState {
  liked: boolean;
  likeCount?: number | null;
}

export interface Comment {
  id: string;
  trackId: string;
  author: UserRef;
  content: string;
  timestampSeconds?: number | null;
  createdAt: string;
  updatedAt: string;
  canEdit: boolean;
  canDelete: boolean;
}

export interface Playlist {
  id: string;
  name: string;
  description?: string | null;
  visibility: ContentVisibility;
  coverUrl?: string | null;
  owner: UserRef;
  trackCount: number;
  totalDurationSeconds: number;
  followerCount: number;
  isFollowedByCurrentUser?: boolean | null;
  isFavoritedByCurrentUser?: boolean | null;
  createdAt: string;
  updatedAt: string;
}

export interface PlaylistTrack {
  track: Track;
  position: number;
  addedAt: string;
}

export interface PlaylistDetails {
  playlist: Playlist;
  tracks: PlaylistTrack[];
  canEdit: boolean;
}

export interface HistoryEntry {
  track: Track;
  lastPositionSeconds: number;
  lastPlayedAt: string;
}

export interface PlaybackProgress {
  trackId: string;
  positionSeconds: number;
  lastPlayedAt?: string | null;
}

export interface RegisterPlayResult {
  counted: boolean;
  reason?: string | null;
  playCount?: number | null;
}

export interface SearchResult {
  type: SearchType;
  query?: string | null;
  tracks?: Paged<Track> | null;
  users?: Paged<UserSummary> | null;
  albums?: Paged<Album> | null;
  playlists?: Paged<Playlist> | null;
  tags?: Paged<Tag> | null;
}

export interface Home {
  recentTracks: Track[];
  popularTracks: Track[];
  popularArtists: UserSummary[];
  popularPlaylists: Playlist[];
  recommendations: Track[];
  fromFollowedArtists: Track[];
}

export interface AnalyticsOverview {
  trackCount: number;
  publicTrackCount: number;
  totalPlays: number;
  totalLikes: number;
  followerCount: number;
  commentCount: number;
  playsLast30Days: number;
}

export interface TrackAnalytics {
  trackId: string;
  title: string;
  playCount: number;
  likeCount: number;
  commentCount: number;
  playlistCount: number;
  visibility: ContentVisibility;
  createdAt: string;
}

export interface PlaysPoint {
  date: string;
  plays: number;
  uniqueListeners: number;
}

export interface PlaysSeries {
  from: string;
  to: string;
  groupBy: AnalyticsGroupBy;
  points: PlaysPoint[];
}

export interface Report {
  id: string;
  targetType: ReportTargetType;
  targetId: string;
  reason: ReportReason;
  description?: string | null;
  status: ReportStatus;
  resolutionNote?: string | null;
  createdAt: string;
  reviewedAt?: string | null;
  reporter?: UserRef | null;
  targetLabel?: string | null;
}

export interface AdminUser {
  id: string;
  username: string;
  email: string;
  role: UserRole;
  status: UserStatus;
  profileVisibility: ProfileVisibility;
  trackCount: number;
  playlistCount: number;
  followerCount: number;
  createdAt: string;
  deletedAt?: string | null;
}

export interface AdminTrack {
  id: string;
  title: string;
  artistName: string;
  owner: UserRef;
  visibility: ContentVisibility;
  status: TrackStatus;
  playCount: number;
  likeCount: number;
  isHidden: boolean;
  isDeleted: boolean;
  createdAt: string;
}

export interface AuditLog {
  id: string;
  actor?: UserRef | null;
  action: string;
  targetType?: string | null;
  targetId?: string | null;
  metadata?: string | null;
  createdAt: string;
}

export interface AdminStatistics {
  totalUsers: number;
  activeUsers: number;
  suspendedUsers: number;
  totalTracks: number;
  publicTracks: number;
  hiddenTracks: number;
  totalPlaylists: number;
  totalComments: number;
  totalPlays: number;
  totalLikes: number;
  pendingReports: number;
  storageBytesUsed: number;
  playsLast30Days: PlaysPoint[];
}

export interface UserExport {
  id: string;
  status: ExportStatus;
  fileSize?: number | null;
  failureReason?: string | null;
  expiresAt?: string | null;
  createdAt: string;
  completedAt?: string | null;
  downloadUrl?: string | null;
}

/** Erreur métier renvoyée par l'API au format Problem Details. */
export interface ApiProblem {
  type?: string;
  title?: string;
  status?: number;
  code?: string;
  detail?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
}
