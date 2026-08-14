import { useState, type FormEvent } from 'react';
import { Link, Navigate, useNavigate } from 'react-router-dom';
import { useAuthStore } from '../features/auth/authStore';
import { ErrorMessage } from '../components/common';

/** Longueur minimale exigée côté interface, alignée sur la validation du backend. */
const MIN_PASSWORD_LENGTH = 8;

/** Formulaire de connexion. */
export function LoginPage() {
  const login = useAuthStore((state) => state.login);
  const me = useAuthStore((state) => state.me);
  const navigate = useNavigate();

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [pending, setPending] = useState(false);

  if (me) {
    return <Navigate to="/" replace />;
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setPending(true);

    try {
      await login(email, password);
      navigate('/');
    } catch (caught) {
      setError(caught);
    } finally {
      setPending(false);
    }
  }

  return (
    <div className="auth-page card">
      <h1>Connexion</h1>
      <ErrorMessage error={error} />

      <form onSubmit={handleSubmit}>
        <div className="field">
          <label htmlFor="login-email">Adresse email</label>
          <input
            id="login-email"
            type="email"
            autoComplete="email"
            required
            value={email}
            onChange={(event) => setEmail(event.target.value)}
          />
        </div>

        <div className="field">
          <label htmlFor="login-password">Mot de passe</label>
          <input
            id="login-password"
            type="password"
            autoComplete="current-password"
            required
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
        </div>

        <button type="submit" className="btn btn-primary" style={{ width: '100%' }} disabled={pending}>
          {pending ? 'Connexion…' : 'Se connecter'}
        </button>
      </form>

      <p className="small muted" style={{ marginTop: 16, marginBottom: 0 }}>
        Pas encore de compte ? <Link to="/register">Créer un compte</Link>
      </p>
    </div>
  );
}

/** Formulaire d'inscription. */
export function RegisterPage() {
  const register = useAuthStore((state) => state.register);
  const me = useAuthStore((state) => state.me);
  const navigate = useNavigate();

  const [email, setEmail] = useState('');
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [pending, setPending] = useState(false);

  if (me) {
    return <Navigate to="/" replace />;
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setPending(true);

    try {
      await register(email, username, password);
      navigate('/');
    } catch (caught) {
      setError(caught);
    } finally {
      setPending(false);
    }
  }

  return (
    <div className="auth-page card">
      <h1>Créer un compte</h1>
      <ErrorMessage error={error} />

      <form onSubmit={handleSubmit}>
        <div className="field">
          <label htmlFor="register-email">Adresse email</label>
          <input
            id="register-email"
            type="email"
            autoComplete="email"
            required
            value={email}
            onChange={(event) => setEmail(event.target.value)}
          />
        </div>

        <div className="field">
          <label htmlFor="register-username">Nom d'utilisateur</label>
          <input
            id="register-username"
            type="text"
            autoComplete="username"
            required
            minLength={3}
            maxLength={32}
            pattern="[A-Za-z0-9._\-]+"
            value={username}
            onChange={(event) => setUsername(event.target.value)}
            aria-describedby="register-username-help"
          />
          <span id="register-username-help" className="small muted">
            3 à 32 caractères : lettres, chiffres, point, tiret ou tiret bas.
          </span>
        </div>

        <div className="field">
          <label htmlFor="register-password">Mot de passe</label>
          <input
            id="register-password"
            type="password"
            autoComplete="new-password"
            required
            minLength={MIN_PASSWORD_LENGTH}
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            aria-describedby="register-password-help"
          />
          <span id="register-password-help" className="small muted">
            Au moins {MIN_PASSWORD_LENGTH} caractères, avec une majuscule, une minuscule et un chiffre.
          </span>
        </div>

        <button type="submit" className="btn btn-primary" style={{ width: '100%' }} disabled={pending}>
          {pending ? 'Création…' : 'Créer mon compte'}
        </button>
      </form>

      <p className="small muted" style={{ marginTop: 16, marginBottom: 0 }}>
        Déjà inscrit ? <Link to="/login">Se connecter</Link>
      </p>
    </div>
  );
}
