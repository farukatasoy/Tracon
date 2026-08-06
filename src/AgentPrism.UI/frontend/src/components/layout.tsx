import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { Link, useNavigate, usePath } from '../lib/router';
import { applyTheme, readThemePreference, writeThemePreference, type ThemePreference } from '../lib/theme';
import { LOCALES, useLocale, useT, type Locale } from '../lib/i18n';
import { createShortcutMatcher, isTextEntry, type ShortcutBinding } from '../lib/shortcuts';
import { cx } from './ui';
import { CommandPalette, ShortcutHelp } from './command-palette';
import {
  AgentsIcon,
  AuditIcon,
  DashboardIcon,
  DiagnosticsIcon,
  EvalsIcon,
  ExperimentsIcon,
  JobsIcon,
  LanguageIcon,
  McpIcon,
  ModelsIcon,
  MoonIcon,
  PlaygroundIcon,
  PrismMark,
  RunsIcon,
  SearchIcon,
  SessionsIcon,
  SettingsIcon,
  SunIcon,
  ToolsIcon,
  WorkflowIcon,
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
  { path: 'agents', label: 'nav.agents', icon: AgentsIcon, hue: 'var(--ap-violet)' },
  { path: 'dashboard', label: 'nav.dashboard', icon: DashboardIcon, hue: 'var(--ap-amber)' },
  { path: 'playground', label: 'nav.playground', icon: PlaygroundIcon, hue: 'var(--ap-cyan)' },
  { path: 'sessions', label: 'nav.sessions', icon: SessionsIcon, hue: 'var(--ap-emerald)' },
  { path: 'workflows', label: 'nav.workflows', icon: WorkflowIcon, hue: 'var(--ap-emerald)' },
  { path: 'jobs', label: 'nav.jobs', icon: JobsIcon, hue: 'var(--ap-amber)' },
  { path: 'evals', label: 'nav.evals', icon: EvalsIcon, hue: 'var(--ap-rose)' },
  { path: 'experiments', label: 'nav.experiments', icon: ExperimentsIcon, hue: 'var(--ap-violet)' },
  { path: 'runs', label: 'nav.runs', icon: RunsIcon, hue: 'var(--ap-amber)' },
  { path: 'tools', label: 'nav.tools', icon: ToolsIcon, hue: 'var(--ap-rose)' },
  { path: 'skills', label: 'nav.skills', icon: ToolsIcon, hue: 'var(--ap-cyan)' },
  { path: 'models', label: 'nav.models', icon: ModelsIcon, hue: 'var(--ap-indigo)' },
  { path: 'mcp', label: 'nav.mcp', icon: McpIcon, hue: 'var(--ap-amber)' },
  { path: 'audit', label: 'nav.audit', icon: AuditIcon, hue: 'var(--ap-indigo)', adminOnly: true },
  { path: 'diagnostics', label: 'nav.diagnostics', icon: DiagnosticsIcon, hue: 'var(--ap-rose)', adminOnly: true },
  { path: 'settings', label: 'nav.settings', icon: SettingsIcon, hue: 'var(--ap-muted)' },
] as const;

/**
 * Global key bindings.
 *
 * `Ctrl/Cmd + Enter` is NOT here: submitting belongs to the form that has the
 * caret, and the playground handles it on its own textarea. A global binding
 * would have to guess which form was meant.
 */
const BINDINGS: readonly ShortcutBinding[] = [
  { keys: 'mod+k', action: 'palette', insideText: true },
  { keys: 'escape', action: 'close', insideText: true },
  { keys: '?', action: 'help' },
  { keys: '/', action: 'search' },
  { keys: 'g a', action: 'go:agents' },
  { keys: 'g r', action: 'go:runs' },
  { keys: 'g s', action: 'go:sessions' },
  { keys: 'g d', action: 'go:dashboard' },
  { keys: 'g w', action: 'go:workflows' },
  { keys: 'g p', action: 'go:playground' },
];

export function Layout({ meta, children }: { meta: Meta; children: ReactNode }): ReactNode {
  const t = useT();
  const path = usePath();
  const navigate = useNavigate();
  const section = path.split('/')[0] ?? '';
  const active = section.length === 0 ? 'dashboard' : section;

  const [palette, setPalette] = useState(false);
  const [help, setHelp] = useState(false);

  const closeOverlays = useCallback(() => {
    setPalette(false);
    setHelp(false);
  }, []);

  useConsoleShortcuts({
    onPalette: () => setPalette((current) => !current),
    onHelp: () => setHelp(true),
    onClose: closeOverlays,
    onNavigate: navigate,
  });

  // A hidden nav item is a UX courtesy, not a security boundary: the server
  // is still the only real enforcement (docs/09-YONETISIM-VE-DENETIM-IZI.md).
  const nav = NAV.filter((item) => !('adminOnly' in item && item.adminOnly) || meta.roles.canAdminister);

  return (
    <div className="flex min-h-screen">
      <aside className="sticky top-0 hidden h-screen w-56 shrink-0 flex-col border-r border-line bg-panel md:flex">
        <div className="ap-prism h-0.5 w-full" />

        <Link to="dashboard" className="flex items-center gap-2.5 px-4 py-4">
          <PrismMark className="size-6 text-fg" />
          <span className="text-[15px] font-semibold tracking-tight">AgentPrism</span>
        </Link>

        <nav aria-label={t('nav.primary')} className="flex flex-1 flex-col gap-0.5 px-2">
          {nav.map((item) => {
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
                {t(item.label)}
              </Link>
            );
          })}
        </nav>

        <StorageNote meta={meta} />
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <TopBar meta={meta} active={active} nav={nav} onOpenPalette={() => setPalette(true)} />
        <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-6 md:px-8">{children}</main>
      </div>

      <CommandPalette
        meta={meta}
        open={palette}
        onClose={() => setPalette(false)}
        onShowShortcuts={() => setHelp(true)}
      />
      <ShortcutHelp open={help} onClose={() => setHelp(false)} />
    </div>
  );
}

/**
 * Wires the global key bindings to the window.
 *
 * 🚨 A keystroke typed into a text field belongs to the text field. The matcher
 * enforces that, but the DOM has to be asked what the caret is in — which is
 * why the target check lives here and the decision lives in `shortcuts.ts`,
 * where it is unit tested without a browser.
 */
function useConsoleShortcuts(handlers: {
  onPalette: () => void;
  onHelp: () => void;
  onClose: () => void;
  onNavigate: (to: string) => void;
}): void {
  const matcher = useMemo(() => createShortcutMatcher(BINDINGS), []);

  // The handlers close over fresh state on every render. Holding them in a ref
  // keeps ONE window listener for the whole session instead of removing and
  // re-adding it after each keystroke that changes state.
  const latest = useRef(handlers);
  latest.current = handlers;

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent): void => {
      // A key that is part of composing a character (IME) is never a shortcut.
      if (event.isComposing || event.altKey) {
        return;
      }

      const target = event.target instanceof HTMLElement ? event.target : null;
      const action = matcher.push(event, { now: performance.now(), inTextEntry: isTextEntry(target) });

      if (action === null) {
        return;
      }

      if (action === 'search') {
        const search = document.querySelector<HTMLInputElement>('input[data-search]');

        if (search === null) {
          return;
        }

        event.preventDefault();
        search.focus();
        search.select();

        return;
      }

      event.preventDefault();

      if (action === 'palette') {
        latest.current.onPalette();
      } else if (action === 'help') {
        latest.current.onHelp();
      } else if (action === 'close') {
        latest.current.onClose();
      } else if (action.startsWith('go:')) {
        latest.current.onNavigate(action.slice(3));
      }
    };

    window.addEventListener('keydown', onKeyDown);

    return () => window.removeEventListener('keydown', onKeyDown);
  }, [matcher]);
}

function TopBar({
  meta,
  active,
  nav,
  onOpenPalette,
}: {
  meta: Meta;
  active: string;
  nav: readonly (typeof NAV)[number][];
  onOpenPalette: () => void;
}): ReactNode {
  const t = useT();

  return (
    <header className="sticky top-0 z-20 flex h-12 items-center justify-between gap-3 border-b border-line bg-panel/85 px-4 backdrop-blur md:px-8">
      <div className="flex items-center gap-2 md:hidden">
        <PrismMark className="size-5 text-fg" />
        <span className="text-[13px] font-semibold">AgentPrism</span>
      </div>

      <nav aria-label={t('nav.primary')} className="flex items-center gap-1 overflow-x-auto md:hidden">
        {nav.map((item) => (
          <Link
            key={item.path}
            to={item.path}
            className={cx(
              'rounded px-2 py-1 text-[12px] whitespace-nowrap',
              active === item.path ? 'bg-raised text-fg' : 'text-muted',
            )}
          >
            {t(item.label)}
          </Link>
        ))}
      </nav>

      <div className="hidden md:block" />

      <div className="flex items-center gap-2">
        <button
          type="button"
          data-testid="palette-open"
          onClick={onOpenPalette}
          title={t('palette.title')}
          aria-label={t('palette.title')}
          className="hidden h-8 items-center gap-2 rounded-md border border-line bg-raised px-2.5 text-[12px] text-muted transition-colors hover:text-fg sm:flex"
        >
          <SearchIcon className="size-3.5" />
          <span>{t('palette.open')}</span>
          <span className="rounded border border-line px-1 font-mono text-[10px]">⌘K</span>
        </button>

        <span className="hidden font-mono text-[11px] text-subtle sm:inline">v{meta.version}</span>
        <LanguageToggle />
        <ThemeToggle />
      </div>
    </header>
  );
}

/**
 * Language switch.
 *
 * Two languages, so a toggle button beats a menu. A third language turns this
 * into a `<select>`; the rest of the console does not change.
 */
function LanguageToggle(): ReactNode {
  const t = useT();
  const { locale, setLocale } = useLocale();
  const next = (LOCALES.find((candidate) => candidate !== locale) ?? 'en') as Locale;

  return (
    <button
      type="button"
      data-testid="language-toggle"
      onClick={() => setLocale(next)}
      title={t('shell.language')}
      aria-label={t('palette.switchLanguage', { language: t(`shell.language.${next}`) })}
      className="flex h-8 items-center gap-1.5 rounded-md border border-line bg-raised px-2 text-[11px] font-medium text-muted transition-colors hover:text-fg"
    >
      <LanguageIcon className="size-3.5" />
      <span className="uppercase">{locale}</span>
    </button>
  );
}

function ThemeToggle(): ReactNode {
  const t = useT();
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
      aria-label={resolved === 'dark' ? t('shell.theme.toLight') : t('shell.theme.toDark')}
      title={t(`shell.theme.${preference === 'system' ? 'system' : preference}`)}
      onClick={() => setPreference(resolved === 'dark' ? 'light' : 'dark')}
      className="rounded-md border border-line bg-raised p-1.5 text-muted transition-colors hover:text-fg"
    >
      {resolved === 'dark' ? <MoonIcon className="size-4" /> : <SunIcon className="size-4" />}
    </button>
  );
}

function StorageNote({ meta }: { meta: Meta }): ReactNode {
  const t = useT();

  return (
    <div className="border-t border-line px-4 py-3 text-[11px] leading-relaxed text-subtle">
      <span
        className={cx(
          'mr-1.5 inline-block size-1.5 rounded-full align-middle',
          meta.storage.persistent ? 'bg-success' : 'bg-warn',
        )}
      />
      {meta.storage.persistent ? t('shell.storage.persistent') : t('shell.storage.memory')}
      {!meta.storage.persistent && <span className="block text-subtle">{t('shell.storage.volatile')}</span>}
    </div>
  );
}
