/** Fonctions de formatage partagées par l'interface. */

/**
 * Formate une durée en `m:ss`, ou `h:mm:ss` au-delà d'une heure.
 * Une valeur non finie est ramenée à zéro afin de ne jamais afficher `NaN`.
 */
export function formatDuration(totalSeconds: number): string {
  const seconds = Number.isFinite(totalSeconds) && totalSeconds > 0 ? Math.floor(totalSeconds) : 0;
  const hours = Math.floor(seconds / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  const rest = seconds % 60;

  if (hours > 0) {
    return `${hours}:${String(minutes).padStart(2, '0')}:${String(rest).padStart(2, '0')}`;
  }

  return `${minutes}:${String(rest).padStart(2, '0')}`;
}

/** Formate un nombre avec les séparateurs de milliers de la locale. */
export function formatNumber(value: number | null | undefined): string {
  return value === null || value === undefined ? '—' : new Intl.NumberFormat().format(value);
}

/** Formate une taille en octets vers l'unité binaire la plus lisible. */
export function formatBytes(bytes: number): string {
  const units = ['o', 'Ko', 'Mo', 'Go', 'To'];
  let value = bytes;
  let unit = 0;

  // Le nombre d'itérations est borné par la taille du tableau d'unités.
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }

  return `${value.toFixed(unit === 0 ? 0 : 1)} ${units[unit]}`;
}

/** Formate une date ISO en date locale courte. */
export function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
}

/** Formate une date ISO en date et heure locales. */
export function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString(undefined, { dateStyle: 'short', timeStyle: 'short' });
}

/** Formate un écart de temps en langage relatif (« il y a 3 h »). */
export function formatRelative(iso: string): string {
  const deltaSeconds = (Date.now() - new Date(iso).getTime()) / 1000;
  const formatter = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });

  const thresholds: [number, Intl.RelativeTimeFormatUnit][] = [
    [60, 'second'],
    [3600, 'minute'],
    [86400, 'hour'],
    [604800, 'day'],
    [2629800, 'week'],
    [31557600, 'month'],
  ];

  let previous = 1;
  for (const [limit, unit] of thresholds) {
    if (deltaSeconds < limit) {
      return formatter.format(-Math.round(deltaSeconds / previous), unit);
    }
    previous = limit;
  }

  return formatter.format(-Math.round(deltaSeconds / 31557600), 'year');
}
