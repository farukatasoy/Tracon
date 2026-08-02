export type ThemePreference = 'system' | 'light' | 'dark';

const STORAGE_KEY = 'agentprism.theme';

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
