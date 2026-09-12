/**
 * Tracon can be mapped under any prefix, so nothing may be hard coded.
 *
 * In production the .NET host injects `<base href="{prefix}/">` into the shell,
 * which makes `document.baseURI` the single source of truth for both routing
 * and API calls.
 *
 * In development the app is served from the Vite root while the API lives
 * behind the dev-server proxy, so the two bases differ.
 */
const documentBase = (): string => {
  try {
    return new URL(document.baseURI).pathname;
  } catch {
    return '/';
  }
};

const withTrailingSlash = (value: string): string => (value.endsWith('/') ? value : `${value}/`);

/** Path the single-page app is mounted on. Always ends with `/`. */
export const uiBase: string = import.meta.env.DEV ? '/' : withTrailingSlash(documentBase());

/** Path the management API is served from. Always ends with `/`. */
export const apiBase: string = import.meta.env.DEV
  ? '/tracon/'
  : withTrailingSlash(documentBase());

/** Joins a relative path onto the API base. */
export function apiUrl(path: string): string {
  return apiBase + path.replace(/^\/+/, '');
}

/** Joins a relative path onto the UI base, for links and history entries. */
export function uiUrl(path: string): string {
  return uiBase + path.replace(/^\/+/, '');
}
