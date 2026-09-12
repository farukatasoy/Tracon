import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { Link, useNavigate, usePath } from '../lib/router';
import { setThemePreference, useThemePreference } from '../lib/theme';
import { LOCALES, useLocale, useT, type Locale } from '../lib/i18n';
import { createShortcutMatcher, isTextEntry, type ShortcutBinding } from '../lib/shortcuts';
import { cx } from './ui';
import { StatusDot } from './status-dot';
import { CommandPalette, ShortcutHelp } from './command-palette';
import { visibleNavigation, type NavGroup } from './navigation';
import { LanguageIcon, MoonIcon, SearchIcon, SunIcon, TraconMark } from './icons';
import type { TraconMetaResponse as Meta } from '@tracon/client';
import { Tooltip } from './tooltip';

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
  const [drawer, setDrawer] = useState(false);

  const closeOverlays = useCallback(() => {
    setPalette(false);
    setHelp(false);
    setDrawer(false);
  }, []);

  useConsoleShortcuts({
    onPalette: () => setPalette((current) => !current),
    onHelp: () => setHelp(true),
    onClose: closeOverlays,
    onNavigate: navigate,
  });

  // The drawer is a mobile affordance, not a route: walking to another screen
  // has to put it away or it covers the screen that was just opened.
  useEffect(() => setDrawer(false), [path]);

  // A hidden nav item is a UX courtesy, not a security boundary: the server
  // is still the only real enforcement (docs/arsiv/fazlar/09-YONETISIM-VE-DENETIM-IZI.md).
  const groups = visibleNavigation(meta.roles.canAdminister);

  return (
    <div className="flex min-h-screen">
      {/* The first Tab stop on every screen. A keyboard operator should not have
          to walk eighteen navigation entries to reach the table they opened. */}
      <a
        href="#tracon-main"
        className={cx(
          'sr-only rounded border border-accent bg-panel px-3 py-2 text-base font-medium text-accent',
          'focus:not-sr-only focus:absolute focus:top-2 focus:left-2 focus:z-60',
        )}
      >
        {t('shell.skipToContent')}
      </a>

      <aside className="sticky top-0 hidden h-screen w-52 shrink-0 flex-col border-r border-line bg-panel md:flex">
        <div className="tracon-sweep h-px w-full" />

        <Link to="dashboard" className="flex items-center gap-2 px-3 py-3.5">
          <TraconMark className="size-5" />
          <span className="text-section font-semibold tracking-tight">Tracon</span>
        </Link>

        <nav aria-label={t('nav.primary')} className="flex flex-1 flex-col gap-3 overflow-y-auto px-2 pb-2">
          {groups.map((group) => (
            <NavGroupList key={group.group} group={group} active={active} />
          ))}
        </nav>

        <StorageNote meta={meta} />
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <TopBar
          meta={meta}
          drawer={drawer}
          onToggleDrawer={() => setDrawer((current) => !current)}
          onOpenPalette={() => setPalette(true)}
        />

        {drawer && (
          <nav
            aria-label={t('nav.primary')}
            data-testid="nav-drawer"
            className="flex flex-col gap-3 border-b border-line bg-panel px-3 py-3 md:hidden"
          >
            {groups.map((group) => (
              <NavGroupList key={group.group} group={group} active={active} />
            ))}
          </nav>
        )}

        <main id="tracon-main" tabIndex={-1} className="mx-auto w-full max-w-6xl flex-1 px-3 py-5 md:px-6">
          {children}
        </main>
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

function NavGroupList({ group, active }: { group: NavGroup; active: string }): ReactNode {
  const t = useT();

  if (group.items.length === 0) {
    return null;
  }

  return (
    <div>
      <p className="px-2 pb-1 text-2xs font-semibold tracking-widest text-subtle uppercase">
        {t(group.group)}
      </p>
      <ul className="flex flex-col gap-px">
        {group.items.map((item) => {
          const isActive = active === item.path;
          const Icon = item.icon;

          return (
            <li key={item.path}>
              <Link
                to={item.path}
                aria-current={isActive ? 'page' : undefined}
                className={cx(
                  'relative flex items-center gap-2 rounded-sm px-2 py-1 text-base transition-colors',
                  isActive
                    ? 'bg-accent-soft font-medium text-accent'
                    : 'text-muted hover:bg-raised hover:text-fg',
                )}
              >
                <span
                  aria-hidden="true"
                  className={cx(
                    'absolute top-1 bottom-1 -left-1 w-0.5 rounded-full bg-accent transition-opacity',
                    isActive ? 'opacity-100' : 'opacity-0',
                  )}
                />
                <Icon className="size-3.5 shrink-0" />
                {t(item.label)}
              </Link>
            </li>
          );
        })}
      </ul>
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
  drawer,
  onToggleDrawer,
  onOpenPalette,
}: {
  meta: Meta;
  drawer: boolean;
  onToggleDrawer: () => void;
  onOpenPalette: () => void;
}): ReactNode {
  const t = useT();

  return (
    <header className="sticky top-0 z-20 flex h-11 items-center gap-2 border-b border-line bg-panel/90 px-3 backdrop-blur md:px-6">
      <button
        type="button"
        data-testid="nav-toggle"
        onClick={onToggleDrawer}
        aria-expanded={drawer}
        aria-controls="tracon-main"
        className="flex items-center gap-2 rounded px-1 py-1 text-base font-semibold md:hidden"
      >
        <TraconMark className="size-4" />
        Tracon
      </button>

      <div className="ml-auto flex items-center gap-1.5">
        <button
          type="button"
          data-testid="palette-open"
          onClick={onOpenPalette}
          aria-label={t('palette.title')}
          className={cx(
            'hidden h-7 items-center gap-2 rounded border border-line-strong bg-raised px-2',
            'text-sm text-muted transition-colors hover:border-accent hover:text-fg sm:flex',
          )}
        >
          <SearchIcon className="size-3.5" />
          <span>{t('palette.open')}</span>
          <span className="rounded-sm border border-line px-1 font-mono text-2xs">⌘K</span>
        </button>

        <span className="hidden font-mono text-2xs text-subtle sm:inline">v{meta.version}</span>
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
      className="flex h-7 items-center gap-1 rounded border border-line-strong bg-raised px-1.5 text-2xs font-medium text-muted transition-colors hover:text-fg"
    >
      <LanguageIcon className="size-3.5" />
      <span className="uppercase">{locale}</span>
    </button>
  );
}

function ThemeToggle(): ReactNode {
  const t = useT();
  const { preference, resolved } = useThemePreference();

  return (
    // The label says what pressing it DOES; the tooltip says what the current
    // preference IS — which is the only place "follow the system" is visible
    // from outside Settings, and a `title` never showed it to a keyboard or a
    // touch user at all.
    <Tooltip
      text={t(`shell.theme.${preference === 'system' ? 'system' : preference}`)}
      placement="bottom"
    >
      <button
        type="button"
        data-testid="theme-toggle"
        aria-label={resolved === 'dark' ? t('shell.theme.toLight') : t('shell.theme.toDark')}
        onClick={() => setThemePreference(resolved === 'dark' ? 'light' : 'dark')}
        className="rounded border border-line-strong bg-raised p-1.5 text-muted transition-colors hover:text-fg"
      >
        {resolved === 'dark' ? <MoonIcon className="size-3.5" /> : <SunIcon className="size-3.5" />}
      </button>
    </Tooltip>
  );
}

function StorageNote({ meta }: { meta: Meta }): ReactNode {
  const t = useT();
  const label = meta.storage.persistent ? t('shell.storage.persistent') : t('shell.storage.memory');

  return (
    <div className="flex items-start gap-2 border-t border-line px-3 py-2.5 text-2xs leading-relaxed text-subtle">
      <StatusDot tone={meta.storage.persistent ? 'success' : 'warn'} className="mt-1" />
      <span>
        {label}
        {!meta.storage.persistent && <span className="block">{t('shell.storage.volatile')}</span>}
      </span>
    </div>
  );
}
