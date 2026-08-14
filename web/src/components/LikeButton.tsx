import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { tracksApi } from '../services/api';
import { useAuthStore } from '../features/auth/authStore';
import { formatNumber } from '../lib/format';
import type { Track } from '../types/api';
import { HeartIcon } from './Icons';

interface LikeButtonProps {
  track: Track;
  /** Affiche le compteur à côté du cœur. */
  showCount?: boolean;
}

/**
 * Bouton de like.
 *
 * L'état est mis à jour de façon optimiste pour que l'interface réponde
 * immédiatement, puis les listes concernées sont invalidées afin de resynchroniser
 * les compteurs avec le serveur, seul détenteur de la vérité.
 */
export function LikeButton({ track, showCount = false }: LikeButtonProps) {
  const isAuthenticated = useAuthStore((state) => state.me !== null);
  const queryClient = useQueryClient();
  const navigate = useNavigate();

  const mutation = useMutation({
    mutationFn: (liked: boolean) => (liked ? tracksApi.unlike(track.id) : tracksApi.like(track.id)),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['track', track.id] });
      queryClient.invalidateQueries({ queryKey: ['likes'] });
      queryClient.invalidateQueries({ queryKey: ['tracks'] });
      queryClient.invalidateQueries({ queryKey: ['home'] });
    },
  });

  const liked = Boolean(track.isLikedByCurrentUser);
  const label = liked ? `Retirer le like de ${track.title}` : `Aimer ${track.title}`;

  function handleClick() {
    if (!isAuthenticated) {
      navigate('/login');
      return;
    }
    mutation.mutate(liked);
  }

  return (
    <span className="row" style={{ gap: 4 }}>
      <button
        type="button"
        className="icon-btn"
        onClick={handleClick}
        aria-pressed={liked}
        aria-label={label}
        title={label}
        disabled={mutation.isPending}
      >
        <HeartIcon size={18} filled={liked} />
      </button>
      {showCount && track.likeCount !== null && track.likeCount !== undefined && (
        <span className="small muted">{formatNumber(track.likeCount)}</span>
      )}
    </span>
  );
}
