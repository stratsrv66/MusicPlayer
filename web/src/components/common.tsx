import { useEffect, useRef, useState, type ReactNode } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { reportsApi, usersApi } from '../services/api';
import { ApiError } from '../services/apiClient';
import { useAuthStore } from '../features/auth/authStore';
import { CloseIcon, FlagIcon } from './Icons';

/** Affiche un message d'erreur d'API en exploitant son code métier. */
export function ErrorMessage({ error }: { error: unknown }) {
  if (!error) {
    return null;
  }

  const message =
    error instanceof ApiError
      ? (error.problem.detail ?? error.problem.title ?? 'Une erreur est survenue.')
      : 'Une erreur inattendue est survenue.';

  const fieldErrors = error instanceof ApiError ? error.fieldErrors : undefined;

  return (
    <div className="alert alert-error" role="alert">
      <p style={{ margin: 0 }}>{message}</p>
      {fieldErrors && (
        <ul style={{ margin: '8px 0 0', paddingLeft: 18 }}>
          {Object.entries(fieldErrors).map(([field, messages]) => (
            <li key={field}>
              <strong>{field}</strong> : {messages.join(' ')}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

/** Indicateur de chargement, sous forme de blocs animés. */
export function Loading({ rows = 3 }: { rows?: number }) {
  return (
    <div className="stack" aria-busy="true" aria-live="polite">
      <span className="sr-only">Chargement en cours…</span>
      {Array.from({ length: rows }, (_, index) => (
        <div key={index} className="skeleton" style={{ height: 56 }} />
      ))}
    </div>
  );
}

/** Message d'état vide. */
export function Empty({ children }: { children: ReactNode }) {
  return <p className="empty">{children}</p>;
}

interface PaginationProps {
  page: number;
  totalPages: number;
  onChange: (page: number) => void;
}

/** Navigation entre les pages d'une collection. */
export function Pagination({ page, totalPages, onChange }: PaginationProps) {
  if (totalPages <= 1) {
    return null;
  }

  return (
    <nav className="pagination" aria-label="Pagination">
      <button type="button" className="btn btn-sm" onClick={() => onChange(page - 1)} disabled={page <= 1}>
        Précédent
      </button>
      <span className="small muted" aria-live="polite">
        Page {page} sur {totalPages}
      </span>
      <button type="button" className="btn btn-sm" onClick={() => onChange(page + 1)} disabled={page >= totalPages}>
        Suivant
      </button>
    </nav>
  );
}

interface DialogProps {
  title: string;
  onClose: () => void;
  children: ReactNode;
}

/**
 * Boîte de dialogue modale.
 *
 * Le focus est déplacé à l'ouverture et la touche Échap referme la boîte,
 * conformément aux pratiques d'accessibilité pour les fenêtres modales.
 */
export function Dialog({ title, onClose, children }: DialogProps) {
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    ref.current?.focus();

    function handleKey(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        onClose();
      }
    }

    document.addEventListener('keydown', handleKey);
    return () => document.removeEventListener('keydown', handleKey);
  }, [onClose]);

  return (
    <div className="dialog-backdrop" onMouseDown={(event) => event.target === event.currentTarget && onClose()}>
      <div className="dialog" role="dialog" aria-modal="true" aria-label={title} tabIndex={-1} ref={ref}>
        <div className="row-between" style={{ marginBottom: 16 }}>
          <h2 style={{ margin: 0 }}>{title}</h2>
          <button type="button" className="icon-btn" onClick={onClose} aria-label="Fermer">
            <CloseIcon size={18} />
          </button>
        </div>
        {children}
      </div>
    </div>
  );
}

interface FollowButtonProps {
  userId: string;
  username: string;
  isFollowing: boolean;
  onChanged?: () => void;
}

/** Bouton d'abonnement à un utilisateur. */
export function FollowButton({ userId, username, isFollowing, onChanged }: FollowButtonProps) {
  const isAuthenticated = useAuthStore((state) => state.me !== null);
  const currentUserId = useAuthStore((state) => state.me?.profile.id);
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: () => (isFollowing ? usersApi.unfollow(userId) : usersApi.follow(userId)),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['profile', username] });
      queryClient.invalidateQueries({ queryKey: ['following'] });
      onChanged?.();
    },
  });

  // On ne propose jamais de s'abonner à soi-même : le backend le refuse également.
  if (currentUserId === userId) {
    return null;
  }

  return (
    <button
      type="button"
      className={isFollowing ? 'btn' : 'btn btn-primary'}
      disabled={mutation.isPending}
      onClick={() => (isAuthenticated ? mutation.mutate() : navigate('/login'))}
    >
      {isFollowing ? 'Abonné' : "S'abonner"}
    </button>
  );
}

interface ReportDialogProps {
  targetType: 'Track' | 'Comment' | 'User' | 'Playlist';
  targetId: string;
  targetLabel: string;
}

/** Bouton et formulaire de signalement d'un contenu. */
export function ReportDialog({ targetType, targetId, targetLabel }: ReportDialogProps) {
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState('Copyright');
  const [description, setDescription] = useState('');
  const isAuthenticated = useAuthStore((state) => state.me !== null);
  const navigate = useNavigate();

  const mutation = useMutation({
    mutationFn: () => reportsApi.create({ targetType, targetId, reason, description: description || undefined }),
    onSuccess: () => setOpen(false),
  });

  return (
    <>
      <button
        type="button"
        className="icon-btn"
        title="Signaler ce contenu"
        aria-label={`Signaler ${targetLabel}`}
        onClick={() => (isAuthenticated ? setOpen(true) : navigate('/login'))}
      >
        <FlagIcon size={18} />
      </button>

      {open && (
        <Dialog title={`Signaler « ${targetLabel} »`} onClose={() => setOpen(false)}>
          <ErrorMessage error={mutation.error} />

          <div className="field">
            <label htmlFor="report-reason">Motif</label>
            <select id="report-reason" value={reason} onChange={(event) => setReason(event.target.value)}>
              <option value="Copyright">Violation de droits d'auteur</option>
              <option value="Offensive">Contenu offensant</option>
              <option value="Spam">Spam</option>
              <option value="Other">Autre</option>
            </select>
          </div>

          <div className="field">
            <label htmlFor="report-description">Description (facultatif)</label>
            <textarea
              id="report-description"
              value={description}
              maxLength={2000}
              onChange={(event) => setDescription(event.target.value)}
              placeholder="Précisez le problème rencontré."
            />
          </div>

          <div className="row" style={{ justifyContent: 'flex-end' }}>
            <button type="button" className="btn" onClick={() => setOpen(false)}>
              Annuler
            </button>
            <button
              type="button"
              className="btn btn-primary"
              onClick={() => mutation.mutate()}
              disabled={mutation.isPending}
            >
              Envoyer le signalement
            </button>
          </div>
        </Dialog>
      )}
    </>
  );
}
