import { useEffect, useState } from 'react';

/**
 * Applique et mémorise le thème clair ou sombre.
 * Le thème est posé sur l'élément racine, où la feuille de style le lit.
 */
export function useTheme() {
  const [theme, setTheme] = useState<'dark' | 'light'>(
    () => (localStorage.getItem('mp.theme') as 'dark' | 'light' | null) ?? 'dark',
  );

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    localStorage.setItem('mp.theme', theme);
  }, [theme]);

  return { theme, toggle: () => setTheme((current) => (current === 'dark' ? 'light' : 'dark')) };
}

/**
 * Retourne la valeur après un délai sans changement.
 * Utilisé par la recherche instantanée pour ne pas interroger l'API à chaque frappe.
 */
export function useDebounced<T>(value: T, delayMs = 300): T {
  const [debounced, setDebounced] = useState(value);

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(timer);
  }, [value, delayMs]);

  return debounced;
}
