/**
 * Conservation des jetons d'authentification.
 *
 * Les jetons vivent dans `localStorage` afin que la session survive à un rechargement.
 * Le module est isolé pour rester le seul point de lecture et d'écriture, ce qui permet
 * de changer de support de stockage sans toucher au reste de l'application.
 */

const ACCESS_TOKEN_KEY = 'mp.accessToken';
const REFRESH_TOKEN_KEY = 'mp.refreshToken';

export interface StoredTokens {
  accessToken: string;
  refreshToken: string;
}

/** Lit les jetons courants, ou `null` si aucune session n'est ouverte. */
export function readTokens(): StoredTokens | null {
  const accessToken = localStorage.getItem(ACCESS_TOKEN_KEY);
  const refreshToken = localStorage.getItem(REFRESH_TOKEN_KEY);
  return accessToken && refreshToken ? { accessToken, refreshToken } : null;
}

/** Enregistre un nouveau couple de jetons. */
export function writeTokens(tokens: StoredTokens): void {
  localStorage.setItem(ACCESS_TOKEN_KEY, tokens.accessToken);
  localStorage.setItem(REFRESH_TOKEN_KEY, tokens.refreshToken);
}

/** Efface la session locale. */
export function clearTokens(): void {
  localStorage.removeItem(ACCESS_TOKEN_KEY);
  localStorage.removeItem(REFRESH_TOKEN_KEY);
}
