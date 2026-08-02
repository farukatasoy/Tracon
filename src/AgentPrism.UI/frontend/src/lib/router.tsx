import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import { uiBase } from './base';

/**
 * A ~100 line history router.
 *
 * The app has eight screens and two dynamic segments. A routing library would
 * add more bytes than the whole feature is worth, and the base path is only
 * known at run time — which every library has to be told about explicitly and
 * this one gets for free.
 */

export interface RouteMatch {
  /** Path relative to the UI base, without a leading slash. */
  path: string;
  /** Values captured by `:name` segments. */
  params: Record<string, string>;
}

interface RouterValue {
  path: string;
  navigate: (to: string, options?: { replace?: boolean }) => void;
}

const RouterContext = createContext<RouterValue | null>(null);

/**
 * Turns the browser location into a path relative to the UI base.
 *
 * The base always ends with `/`, but the address bar usually does not:
 * `/agentprism` and `/agentprism/` are the same page. Comparing against the
 * base alone left the first form unmatched and rendered "page not found" on
 * the most common entry URL there is.
 */
export function toRelativePath(pathname: string, base: string): string {
  const withoutSlash = base.slice(0, -1);

  const relative =
    pathname === withoutSlash
      ? ''
      : pathname.startsWith(base)
        ? pathname.slice(base.length)
        : pathname;

  return relative.replace(/^\/+/, '').replace(/\/+$/, '');
}

function currentPath(): string {
  return toRelativePath(window.location.pathname, uiBase);
}

export function RouterProvider({ children }: { children: ReactNode }): ReactNode {
  const [path, setPath] = useState(currentPath);

  useEffect(() => {
    const onPopState = (): void => setPath(currentPath());

    window.addEventListener('popstate', onPopState);

    return () => window.removeEventListener('popstate', onPopState);
  }, []);

  const navigate = useCallback((to: string, options?: { replace?: boolean }) => {
    const target = to.replace(/^\/+/, '');
    const url = uiBase + target;

    if (options?.replace === true) {
      window.history.replaceState(null, '', url);
    } else {
      window.history.pushState(null, '', url);
    }

    setPath(target.replace(/\/+$/, ''));
    window.scrollTo(0, 0);
  }, []);

  const value = useMemo<RouterValue>(() => ({ path, navigate }), [path, navigate]);

  return <RouterContext.Provider value={value}>{children}</RouterContext.Provider>;
}

function useRouter(): RouterValue {
  const value = useContext(RouterContext);

  if (value === null) {
    throw new Error('useRouter must be used inside RouterProvider.');
  }

  return value;
}

export function useNavigate(): (to: string, options?: { replace?: boolean }) => void {
  return useRouter().navigate;
}

export function usePath(): string {
  return useRouter().path;
}

/**
 * Matches a pattern such as `agents/:name` against the current path.
 *
 * Patterns are tried in order, so a literal segment must be registered before
 * the dynamic one that would also match it (`agents/new` before `agents/:name`).
 */
export function matchRoute(pattern: string, path: string): RouteMatch | null {
  const patternParts = pattern.split('/').filter((part) => part.length > 0);
  const pathParts = path.split('/').filter((part) => part.length > 0);

  if (patternParts.length !== pathParts.length) {
    return null;
  }

  const params: Record<string, string> = {};

  for (let index = 0; index < patternParts.length; index++) {
    const expected = patternParts[index] as string;
    const actual = pathParts[index] as string;

    if (expected.startsWith(':')) {
      params[expected.slice(1)] = decodeURIComponent(actual);

      continue;
    }

    if (expected !== actual) {
      return null;
    }
  }

  return { path, params };
}

export interface RouteDefinition {
  pattern: string;
  render: (params: Record<string, string>) => ReactNode;
}

export function useRoute(routes: readonly RouteDefinition[]): ReactNode | null {
  const path = usePath();

  for (const route of routes) {
    const match = matchRoute(route.pattern, path);

    if (match !== null) {
      return route.render(match.params);
    }
  }

  return null;
}

/**
 * An anchor that navigates without a full page load.
 *
 * The `href` is a real URL so middle-click, copy-link and open-in-new-tab all
 * behave the way a user expects.
 */
export function Link({
  to,
  className,
  children,
  title,
}: {
  to: string;
  className?: string;
  children: ReactNode;
  title?: string;
}): ReactNode {
  const navigate = useNavigate();
  const href = uiBase + to.replace(/^\/+/, '');

  return (
    <a
      href={href}
      className={className}
      title={title}
      onClick={(event) => {
        if (event.defaultPrevented || event.button !== 0) {
          return;
        }

        if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
          return;
        }

        event.preventDefault();
        navigate(to);
      }}
    >
      {children}
    </a>
  );
}
