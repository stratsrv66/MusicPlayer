import { create } from 'zustand';
import { authApi, meApi } from '../../services/api';
import { clearTokens, readTokens, writeTokens } from '../../services/tokenStore';
import type { Me } from '../../types/api';

/**
 * État d'authentification.
 *
 * Il est séparé de l'état serveur (React Query) et de l'état du lecteur : seule
 * l'identité de l'utilisateur courant y est conservée, car elle conditionne le
 * rendu de toute l'application.
 */
interface AuthState {
  me: Me | null;
  /** Vrai tant que la session n'a pas été restaurée au démarrage. */
  loading: boolean;
  login: (email: string, password: string) => Promise<void>;
  register: (email: string, username: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  /** Recharge le profil depuis l'API, après une modification par exemple. */
  refresh: () => Promise<void>;
  /** Restaure la session à partir des jetons persistés. */
  restore: () => Promise<void>;
  /** Ferme la session côté interface, sans appel réseau. */
  clear: () => void;
}

export const useAuthStore = create<AuthState>((set, get) => ({
  me: null,
  loading: true,

  login: async (email, password) => {
    const response = await authApi.login(email, password);
    writeTokens({ accessToken: response.accessToken, refreshToken: response.refreshToken });
    set({ me: await meApi.get() });
  },

  register: async (email, username, password) => {
    const response = await authApi.register(email, username, password);
    writeTokens({ accessToken: response.accessToken, refreshToken: response.refreshToken });
    set({ me: await meApi.get() });
  },

  logout: async () => {
    const tokens = readTokens();
    if (tokens) {
      try {
        await authApi.logout(tokens.refreshToken);
      } catch {
        // Une révocation impossible côté serveur ne doit pas bloquer la déconnexion locale.
      }
    }

    clearTokens();
    set({ me: null });
  },

  refresh: async () => {
    if (!readTokens()) {
      return;
    }
    set({ me: await meApi.get() });
  },

  restore: async () => {
    if (!readTokens()) {
      set({ me: null, loading: false });
      return;
    }

    try {
      set({ me: await meApi.get(), loading: false });
    } catch {
      clearTokens();
      set({ me: null, loading: false });
    }
  },

  clear: () => {
    clearTokens();
    if (get().me) {
      set({ me: null });
    }
  },
}));

/** Raccourci : identifiant de l'utilisateur connecté, ou `null`. */
export function useCurrentUserId(): string | null {
  return useAuthStore((state) => state.me?.profile.id ?? null);
}

/** Raccourci : vrai si l'utilisateur peut accéder à l'administration. */
export function useIsAdmin(): boolean {
  return useAuthStore((state) => state.me?.profile.role === 'Admin');
}

/** Raccourci : vrai si l'utilisateur peut modérer les contenus. */
export function useCanModerate(): boolean {
  return useAuthStore((state) => {
    const role = state.me?.profile.role;
    return role === 'Admin' || role === 'Moderator';
  });
}
