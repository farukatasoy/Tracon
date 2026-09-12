import { useSyncExternalStore } from 'react';

export type ThemePreference = 'system' | 'light' | 'dark';

const STORAGE_KEY = 'tracon.theme';

/**
 * Resolves the stored preference to the palette that should actually render.
 */
export function resolveTheme(preference: ThemePreference): 'light' | 'dark' {
  if (preference !== 'system') {
    return preference;
  }

  return typeof window !== 'undefined' &&
    window.matchMedia('(prefers-color-scheme: dark)').matches
    ? 'dark'
    : 'light';
}

export function readThemePreference(): ThemePreference {
  try {
    const stored = window.localStorage.getItem(STORAGE_KEY);

    return stored === 'light' || stored === 'dark' || stored === 'system' ? stored : 'system';
  } catch {
    // Private browsing modes can throw on storage access. The default is fine.
    return 'system';
  }
}

export function writeThemePreference(preference: ThemePreference): void {
  try {
    window.localStorage.setItem(STORAGE_KEY, preference);
  } catch {
    // A theme that does not survive a reload is better than a broken page.
  }
}

/**
 * Writes the resolved palette onto <html>.
 *
 * Called once at module load, before React renders, so the correct palette is
 * present on the first paint. Deriving the palette from CSS alone would flash
 * the system colours whenever the user has chosen the other one.
 */
export function applyTheme(preference: ThemePreference): 'light' | 'dark' {
  const resolved = resolveTheme(preference);

  document.documentElement.dataset['theme'] = resolved;

  return resolved;
}

/**
 * Shared theme state.
 *
 * `ThemeToggle` (the top-bar button), the Settings screen's `<select>` and the
 * command palette's "Toggle theme" action all write the preference. Before this
 * store existed each one held its own `useState`, seeded once from
 * `localStorage` at mount: a change made through one of them left the others
 * showing a stale value until a full page reload. `setThemePreference` is now
 * the single write path, and `useThemePreference` the single read path.
 */
let themeState: { preference: ThemePreference; resolved: 'light' | 'dark' } = (() => {
  const preference = readThemePreference();

  return { preference, resolved: applyTheme(preference) };
})();

const themeListeners = new Set<() => void>();

function notifyThemeListeners(): void {
  for (const listener of themeListeners) {
    listener();
  }
}

export function setThemePreference(preference: ThemePreference): void {
  writeThemePreference(preference);
  themeState = { preference, resolved: applyTheme(preference) };
  notifyThemeListeners();
}

if (typeof window !== 'undefined') {
  // While following the system, react to the user flipping it in the OS.
  // One listener for the whole session, not one per subscribed component.
  window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
    if (themeState.preference !== 'system') {
      return;
    }

    themeState = { preference: 'system', resolved: applyTheme('system') };
    notifyThemeListeners();
  });
}

function subscribeToTheme(listener: () => void): () => void {
  themeListeners.add(listener);

  return () => {
    themeListeners.delete(listener);
  };
}

export function useThemePreference(): { preference: ThemePreference; resolved: 'light' | 'dark' } {
  return useSyncExternalStore(subscribeToTheme, () => themeState, () => themeState);
}
