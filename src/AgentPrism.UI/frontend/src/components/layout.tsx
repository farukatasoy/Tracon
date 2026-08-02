import { useEffect, useState, type ReactNode } from 'react';
import { Link, usePath } from '../lib/router';
import { applyTheme, readThemePreference, writeThemePreference, type ThemePreference } from '../lib/theme';
import { cx } from './ui';
import {
  AgentsIcon,
  McpIcon,
  ModelsIcon,
  MoonIcon,
  PlaygroundIcon,
  PrismMark,
  RunsIcon,
  SessionsIcon,
  SettingsIcon,
  SunIcon,
  ToolsIcon,
} from './icons';
import type { Meta } from '../lib/types';

/**
 * Navigation.
 *
 * Every screen owns a hue from the prism spectrum. The colour is not decoration:
 * it is the same hue used for that domain's badges and accents everywhere else,
 * so a glance tells you which kind of object you are looking at. Text and icon
 * shape carry the meaning too, so the UI still reads without colour.
 */
const NAV = [
  { path: 'agents', label: 'Agents', icon: AgentsIcon, hue: 'var(--ap-violet)' },
  { path: 'playground', label: 'Playground', icon: PlaygroundIcon, hue: 'var(--ap-cyan)' },
  { path: 'sessions', label: 'Sessions', icon: SessionsIcon, hue: 'var(--ap-emerald)' },
  { path: 'runs', label: 'Runs', icon: RunsIcon, hue: 'var(--ap-amber)' },
  { path: 'tools', label: 'Tools', icon: ToolsIcon, hue: 'var(--ap-rose)' },
  { path: 'models', label: 'Models', icon: ModelsIcon, hue: 'var(--ap-indigo)' },
  { path: 'mcp', label: 'MCP', icon: McpIcon, hue: 'var(--ap-amber)' },
  { path: 'settings', label: 'Settings', icon: SettingsIcon, hue: 'var(--ap-muted)' },
] as const;

export function Layout({ meta, children }: { meta: Meta; children: ReactNode }): ReactNode {
  const path = usePath();
  const section = path.split('/')[0] ?? '';
  const active = section.length === 0 ? 'agents' : section;

  return (
    <div className="flex min-h-screen">
      <aside className="sticky top-0 hidden h-screen w-56 shrink-0 flex-col border-r border-line bg-panel md:flex">
        <div className="ap-prism h-0.5 w-full" />

        <Link to="agents" className="flex items-center gap-2.5 px-4 py-4">
          <PrismMark className="size-6 text-fg" />
          <span className="text-[15px] font-semibold tracking-tight">AgentPrism</span>
        </Link>

        <nav className="flex flex-1 flex-col gap-0.5 px-2">
          {NAV.map((item) => {
            const isActive = active === item.path;
            const Icon = item.icon;

            return (
              <Link
                key={item.path}
                to={item.path}
                className={cx(
                  'group relative flex items-center gap-2.5 rounded-md px-2.5 py-1.5 text-[13px] font-medium transition-colors',
                  isActive ? 'bg-raised text-fg' : 'text-muted hover:bg-raised hover:text-fg',
                )}
              >
                <span
                  aria-hidden="true"
                  className={cx(
                    'absolute top-1.5 bottom-1.5 -left-2 w-0.5 rounded-full transition-opacity',
                    isActive ? 'opacity-100' : 'opacity-0',
                  )}
                  style={{ background: item.hue }}
                />
                <Icon className="size-4" />
                {item.label}
              </Link>
            );
          })}
        </nav>

        <StorageNote meta={meta} />
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <TopBar meta={meta} active={active} />
        <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-6 md:px-8">{children}</main>
      </div>
    </div>
  );
}

function TopBar({ meta, active }: { meta: Meta; active: string }): ReactNode {
  return (
    <header className="sticky top-0 z-20 flex h-12 items-center justify-between gap-3 border-b border-line bg-panel/85 px-4 backdrop-blur md:px-8">
      <div className="flex items-center gap-2 md:hidden">
        <PrismMark className="size-5 text-fg" />
        <span className="text-[13px] font-semibold">AgentPrism</span>
      </div>

      <nav className="flex items-center gap-1 overflow-x-auto md:hidden">
        {NAV.map((item) => (
          <Link
            key={item.path}
            to={item.path}
            className={cx(
              'rounded px-2 py-1 text-[12px] whitespace-nowrap',
              active === item.path ? 'bg-raised text-fg' : 'text-muted',
            )}
          >
            {item.label}
          </Link>
        ))}
      </nav>

      <div className="hidden md:block" />

      <div className="flex items-center gap-3">
        <span className="hidden font-mono text-[11px] text-subtle sm:inline">v{meta.version}</span>
        <ThemeToggle />
      </div>
    </header>
  );
}

function ThemeToggle(): ReactNode {
  const [preference, setPreference] = useState<ThemePreference>(readThemePreference);
  const [resolved, setResolved] = useState<'light' | 'dark'>(() => applyTheme(readThemePreference()));

  useEffect(() => {
    setResolved(applyTheme(preference));
    writeThemePreference(preference);

    if (preference !== 'system') {
      return;
    }

    // While following the system, react to the user flipping it in the OS.
    const media = window.matchMedia('(prefers-color-scheme: dark)');
    const onChange = (): void => setResolved(applyTheme('system'));

    media.addEventListener('change', onChange);

    return () => media.removeEventListener('change', onChange);
  }, [preference]);

  return (
    <button
      type="button"
      data-testid="theme-toggle"
      aria-label={`Switch to ${resolved === 'dark' ? 'light' : 'dark'} theme`}
      title={preference === 'system' ? 'Theme: system' : `Theme: ${preference}`}
      onClick={() => setPreference(resolved === 'dark' ? 'light' : 'dark')}
      className="rounded-md border border-line bg-raised p-1.5 text-muted transition-colors hover:text-fg"
    >
      {resolved === 'dark' ? <MoonIcon className="size-4" /> : <SunIcon className="size-4" />}
    </button>
  );
}

function StorageNote({ meta }: { meta: Meta }): ReactNode {
  return (
    <div className="border-t border-line px-4 py-3 text-[11px] leading-relaxed text-subtle">
      <span
        className={cx(
          'mr-1.5 inline-block size-1.5 rounded-full align-middle',
          meta.storage.persistent ? 'bg-success' : 'bg-warn',
        )}
      />
      {meta.storage.persistent ? 'Persistent storage' : 'In-memory storage'}
      {!meta.storage.persistent && (
        <span className="block text-subtle">Data is lost when the process exits.</span>
      )}
    </div>
  );
}
