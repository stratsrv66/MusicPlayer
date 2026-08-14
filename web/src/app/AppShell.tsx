import { useState, type FormEvent } from 'react';
import { Link, NavLink, Outlet, useNavigate } from 'react-router-dom';
import { mediaUrl } from '../services/apiClient';
import { useAuthStore, useCanModerate } from '../features/auth/authStore';
import { AudioEngine } from '../features/player/AudioEngine';
import { PlayerBar } from '../features/player/PlayerBar';
import { useTheme } from '../hooks';
import {
  ChartIcon,
  HeartIcon,
  HistoryIcon,
  HomeIcon,
  LibraryIcon,
  MoonIcon,
  MusicIcon,
  SearchIcon,
  SettingsIcon,
  ShieldIcon,
  SunIcon,
  UploadIcon,
  UserIcon,
} from '../components/Icons';

/**
 * Coquille applicative : navigation, barre supérieure et lecteur persistant.
 *
 * Le moteur audio et la barre de lecture sont rendus en dehors de l'`Outlet`,
 * ce qui garantit qu'ils ne sont jamais démontés lors d'un changement de page.
 */
export function AppShell() {
  const me = useAuthStore((state) => state.me);
  const logout = useAuthStore((state) => state.logout);
  const canModerate = useCanModerate();
  const { theme, toggle: toggleTheme } = useTheme();
  const [search, setSearch] = useState('');
  const navigate = useNavigate();

  function handleSearch(event: FormEvent) {
    event.preventDefault();
    const term = search.trim();
    if (term) {
      navigate(`/search?q=${encodeURIComponent(term)}`);
    }
  }

  return (
    <>
      <a className="skip-link" href="#main">
        Aller au contenu principal
      </a>

      <div className="app-shell">
        <aside className="sidebar">
          <Link to="/" className="brand">
            <MusicIcon size={24} />
            MusicPlatform
          </Link>

          <nav className="nav" aria-label="Navigation principale">
            <NavLink to="/" end>
              <HomeIcon size={18} /> Accueil
            </NavLink>
            <NavLink to="/search">
              <SearchIcon size={18} /> Recherche
            </NavLink>

            {me && (
              <>
                <p className="nav-section">Ma bibliothèque</p>
                <NavLink to="/me/tracks">
                  <LibraryIcon size={18} /> Mes morceaux
                </NavLink>
                <NavLink to="/me/playlists">
                  <MusicIcon size={18} /> Mes playlists
                </NavLink>
                <NavLink to="/me/likes">
                  <HeartIcon size={18} /> Mes likes
                </NavLink>
                <NavLink to="/me/history">
                  <HistoryIcon size={18} /> Écoutés récemment
                </NavLink>
                <NavLink to="/upload">
                  <UploadIcon size={18} /> Importer
                </NavLink>

                <p className="nav-section">Mon compte</p>
                <NavLink to="/me">
                  <UserIcon size={18} /> Mon profil
                </NavLink>
                <NavLink to="/me/analytics">
                  <ChartIcon size={18} /> Statistiques
                </NavLink>
                <NavLink to="/me/settings">
                  <SettingsIcon size={18} /> Paramètres
                </NavLink>
              </>
            )}

            {canModerate && (
              <>
                <p className="nav-section">Administration</p>
                <NavLink to="/admin">
                  <ShieldIcon size={18} /> Console
                </NavLink>
              </>
            )}
          </nav>
        </aside>

        <div style={{ minWidth: 0 }}>
          <header className="topbar">
            <form role="search" onSubmit={handleSearch} className="grow" style={{ maxWidth: 460 }}>
              <label htmlFor="topbar-search" className="sr-only">
                Rechercher un morceau, un artiste, une playlist ou un tag
              </label>
              <input
                id="topbar-search"
                type="search"
                value={search}
                onChange={(event) => setSearch(event.target.value)}
                placeholder="Rechercher un titre, un artiste, #tag…"
              />
            </form>

            <button
              type="button"
              className="icon-btn"
              onClick={toggleTheme}
              aria-label={theme === 'dark' ? 'Passer en thème clair' : 'Passer en thème sombre'}
            >
              {theme === 'dark' ? <SunIcon size={18} /> : <MoonIcon size={18} />}
            </button>

            {me ? (
              <div className="row" style={{ gap: 8 }}>
                <Link to="/me" className="row" style={{ gap: 8 }}>
                  {me.profile.avatarUrl ? (
                    <img className="avatar" src={mediaUrl(me.profile.avatarUrl)} alt="" />
                  ) : (
                    <span className="avatar" aria-hidden="true" />
                  )}
                  <span className="small">{me.profile.username}</span>
                </Link>
                <button type="button" className="btn btn-sm" onClick={() => void logout()}>
                  Déconnexion
                </button>
              </div>
            ) : (
              <div className="row" style={{ gap: 8 }}>
                <Link to="/login" className="btn btn-sm">
                  Connexion
                </Link>
                <Link to="/register" className="btn btn-sm btn-primary">
                  Créer un compte
                </Link>
              </div>
            )}
          </header>

          <main id="main" className="content">
            <Outlet />
          </main>
        </div>
      </div>

      <nav className="mobile-nav" aria-label="Navigation mobile">
        <NavLink to="/" end>
          <HomeIcon size={20} /> Accueil
        </NavLink>
        <NavLink to="/search">
          <SearchIcon size={20} /> Recherche
        </NavLink>
        {me ? (
          <>
            <NavLink to="/me/tracks">
              <LibraryIcon size={20} /> Bibliothèque
            </NavLink>
            <NavLink to="/upload">
              <UploadIcon size={20} /> Importer
            </NavLink>
            <NavLink to="/me">
              <UserIcon size={20} /> Profil
            </NavLink>
          </>
        ) : (
          <NavLink to="/login">
            <UserIcon size={20} /> Connexion
          </NavLink>
        )}
      </nav>

      <PlayerBar />
      <AudioEngine />
    </>
  );
}
