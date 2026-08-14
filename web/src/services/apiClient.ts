import type { ApiProblem } from '../types/api';
import { clearTokens, readTokens, writeTokens } from './tokenStore';

/** Base de l'API, surchargeable à la construction via `VITE_API_BASE_URL`. */
export const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '');

/** Préfixe versionné de tous les endpoints. */
const API_PREFIX = '/api/v1';

/** Erreur portant le code métier et le détail renvoyés par l'API. */
export class ApiError extends Error {
  readonly status: number;
  readonly problem: ApiProblem;

  constructor(status: number, problem: ApiProblem) {
    super(problem.detail ?? problem.title ?? `Request failed with status ${status}`);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
  }

  /** Code métier stable, par exemple `TRACK_NOT_FOUND`. */
  get code(): string | undefined {
    return this.problem.code;
  }

  /** Erreurs de validation par champ, lorsqu'elles sont fournies. */
  get fieldErrors(): Record<string, string[]> | undefined {
    return this.problem.errors;
  }
}

/** Callback invoqué lorsque la session devient définitivement invalide. */
let onSessionExpired: (() => void) | null = null;

/** Enregistre la réaction à une session expirée (déconnexion de l'interface). */
export function setSessionExpiredHandler(handler: () => void): void {
  onSessionExpired = handler;
}

/**
 * Renouvellement du jeton en cours, partagé entre les appels concurrents.
 * Sans cela, plusieurs requêtes recevant un 401 simultanément déclencheraient
 * plusieurs rotations et invalideraient mutuellement leurs jetons.
 */
let refreshInFlight: Promise<string | null> | null = null;

/** Construit l'URL absolue d'un endpoint. */
export function apiUrl(path: string): string {
  return `${API_BASE_URL}${path.startsWith(API_PREFIX) ? path : API_PREFIX + path}`;
}

/**
 * Résout une URL renvoyée par l'API (pochette, avatar, flux) en URL absolue.
 * Les DTO exposent des chemins relatifs afin de rester indépendants de l'hôte.
 */
export function mediaUrl(path: string | null | undefined): string | undefined {
  if (!path) {
    return undefined;
  }
  return path.startsWith('http') ? path : `${API_BASE_URL}${path}`;
}

interface RequestOptions {
  method?: string;
  body?: unknown;
  /** Corps déjà encodé, utilisé pour les envois `multipart/form-data`. */
  formData?: FormData;
  signal?: AbortSignal;
  /** N'ajoute pas l'en-tête d'autorisation, même si une session existe. */
  anonymous?: boolean;
}

/**
 * Exécute une requête vers l'API.
 *
 * En cas de 401 sur un appel authentifié, un renouvellement du jeton est tenté une
 * seule fois : si celui-ci échoue, la session est fermée et l'erreur est propagée.
 */
export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const response = await send(path, options);

  if (response.status === 401 && !options.anonymous) {
    const token = await refreshAccessToken();
    if (token) {
      const retried = await send(path, options, token);
      return handleResponse<T>(retried);
    }
  }

  return handleResponse<T>(response);
}

/** Envoie la requête HTTP avec les en-têtes appropriés. */
async function send(path: string, options: RequestOptions, overrideToken?: string): Promise<Response> {
  const headers: Record<string, string> = {};

  if (!options.anonymous) {
    const token = overrideToken ?? readTokens()?.accessToken;
    if (token) {
      headers.Authorization = `Bearer ${token}`;
    }
  }

  let body: BodyInit | undefined;
  if (options.formData) {
    // Le navigateur pose lui-même le Content-Type avec la frontière multipart.
    body = options.formData;
  } else if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json';
    body = JSON.stringify(options.body);
  }

  return fetch(apiUrl(path), {
    method: options.method ?? 'GET',
    headers,
    body,
    signal: options.signal,
  });
}

/** Convertit la réponse en données typées, ou lève une `ApiError`. */
async function handleResponse<T>(response: Response): Promise<T> {
  if (response.status === 204) {
    return undefined as T;
  }

  const text = await response.text();
  const payload = text ? safeParse(text) : null;

  if (!response.ok) {
    throw new ApiError(response.status, (payload as ApiProblem) ?? { status: response.status });
  }

  return payload as T;
}

/** Analyse un corps JSON en tolérant une réponse non structurée. */
function safeParse(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return { detail: text };
  }
}

/**
 * Échange le refresh token contre un nouvel access token.
 * Retourne `null` si la session ne peut pas être prolongée.
 */
async function refreshAccessToken(): Promise<string | null> {
  if (refreshInFlight) {
    return refreshInFlight;
  }

  const tokens = readTokens();
  if (!tokens) {
    return null;
  }

  refreshInFlight = (async () => {
    try {
      const response = await fetch(apiUrl('/auth/refresh'), {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken: tokens.refreshToken }),
      });

      if (!response.ok) {
        clearTokens();
        onSessionExpired?.();
        return null;
      }

      const refreshed = (await response.json()) as { accessToken: string; refreshToken: string };
      writeTokens({ accessToken: refreshed.accessToken, refreshToken: refreshed.refreshToken });
      return refreshed.accessToken;
    } catch {
      // Une panne réseau ne doit pas effacer la session : l'appel suivant réessaiera.
      return null;
    } finally {
      refreshInFlight = null;
    }
  })();

  return refreshInFlight;
}

/** Sérialise des paramètres de requête en ignorant les valeurs absentes. */
export function query(params: Record<string, string | number | boolean | null | undefined>): string {
  const search = new URLSearchParams();

  for (const [key, value] of Object.entries(params)) {
    if (value !== null && value !== undefined && value !== '') {
      search.set(key, String(value));
    }
  }

  const serialized = search.toString();
  return serialized ? `?${serialized}` : '';
}
