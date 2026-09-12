import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { shortId } from '../lib/format';
import { LOCALES, useLocale, useT } from '../lib/i18n';
import { rankCommands } from '../lib/palette';
import { useNavigate } from '../lib/router';
import { setThemePreference, useThemePreference } from '../lib/theme';
import { cx } from './ui';
import { Dialog, useFocusTrap } from './dialog';
import { NAV_GROUPS } from './navigation';
import { SearchIcon } from './icons';
import type { TraconMetaResponse as Meta, SessionRecord } from '@tracon/client';
import type { AgentDescriptor, RunRecord, WorkflowDescriptor } from '../lib/server-types';

/**
 * `Ctrl/Cmd + K` command palette.
 *
 * Everything a keyboard operator reaches for: the screens, the agents and
 * workflows by name, a recent run by the first characters of its id, and the
 * few actions that are not a navigation.
 *
 * 🚨 Commands are filtered BY ROLE, exactly like the navigation is. A reader
 * must not be offered "New agent" — the server would refuse it, and offering an
 * action that always fails is worse than not offering it. As everywhere else in
 * this console, hiding is a courtesy; the server is the enforcement
 * (docs/arsiv/fazlar/09-YONETISIM-VE-DENETIM-IZI.md).
 */

export interface PaletteCommand {
  id: string;
  label: string;
  group: string;
  /** Matched but not shown — an identifier, or the English name of a screen. */
  keywords?: string;
  /** Shown on the right of the row. */
  hint?: string;
  perform: () => void;
}

export function CommandPalette({
  meta,
  open,
  onClose,
  onShowShortcuts,
}: {
  meta: Meta;
  open: boolean;
  onClose: () => void;
  onShowShortcuts: () => void;
}): ReactNode {
  const t = useT();
  const [query, setQuery] = useState('');
  const [highlighted, setHighlighted] = useState(0);
  const list = useRef<HTMLUListElement | null>(null);
  const panel = useRef<HTMLDivElement | null>(null);

  const commands = usePaletteCommands(meta, open, onClose, onShowShortcuts);
  const matches = useMemo(() => rankCommands(commands, query).slice(0, 40), [commands, query]);

  // Focus, Esc and focus restoration are the modal layer's job, shared with
  // every other dialog in the console (`dialog.tsx`).
  const onTrapKeyDown = useFocusTrap(open, panel, onClose);

  useEffect(() => {
    if (open) {
      setQuery('');
      setHighlighted(0);
    }
  }, [open]);

  useEffect(() => {
    setHighlighted(0);
  }, [query]);

  // Keeps the highlighted row inside the scroll box while the arrows move it.
  useEffect(() => {
    list.current?.children[highlighted]?.scrollIntoView({ block: 'nearest' });
  }, [highlighted]);

  if (!open) {
    return null;
  }

  const activeId = matches[highlighted] === undefined ? undefined : `tracon-command-${highlighted}`;

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center bg-black/40 px-4 pt-[12vh] backdrop-blur-sm"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) {
          onClose();
        }
      }}
    >
      <div
        ref={panel}
        role="dialog"
        aria-modal="true"
        aria-label={t('palette.title')}
        data-testid="command-palette"
        className="w-full max-w-lg overflow-hidden rounded border border-line-strong bg-panel shadow-panel"
        onKeyDown={(event) => {
          // Esc and Tab belong to the modal layer; the arrows and Enter belong
          // to the list, which is driven by `aria-activedescendant`.
          onTrapKeyDown(event);

          if (event.key === 'Escape' || event.key === 'Tab') {
            return;
          }

          if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault();

            if (matches.length === 0) {
              return;
            }

            const step = event.key === 'ArrowDown' ? 1 : -1;
            setHighlighted((current) => (current + step + matches.length) % matches.length);

            return;
          }

          if (event.key === 'Enter') {
            event.preventDefault();
            matches[highlighted]?.perform();
          }
        }}
      >
        <div className="flex items-center gap-2 border-b border-line px-3">
          <SearchIcon className="size-4 shrink-0 text-subtle" />
          <input
            type="text"
            role="combobox"
            aria-expanded="true"
            aria-controls="tracon-command-list"
            aria-autocomplete="list"
            aria-activedescendant={activeId}
            aria-label={t('palette.placeholder')}
            placeholder={t('palette.placeholder')}
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            className="h-11 w-full bg-transparent text-body text-fg placeholder:text-subtle focus:outline-none"
          />
        </div>

        {matches.length === 0 ? (
          <p className="px-4 py-8 text-center text-base text-muted">{t('palette.empty')}</p>
        ) : (
          <ul
            ref={list}
            id="tracon-command-list"
            role="listbox"
            aria-label={t('palette.results')}
            className="max-h-80 overflow-y-auto py-1"
          >
            {matches.map((command, index) => (
              <li
                key={command.id}
                id={`tracon-command-${index}`}
                role="option"
                aria-selected={index === highlighted}
                onMouseMove={() => setHighlighted(index)}
                onClick={() => command.perform()}
                className={cx(
                  'flex cursor-pointer items-center justify-between gap-3 px-4 py-2 text-base',
                  index === highlighted ? 'bg-raised text-fg' : 'text-muted',
                )}
              >
                <span className="min-w-0 truncate">
                  <span className="mr-2 text-xs tracking-wide text-subtle uppercase">
                    {command.group}
                  </span>
                  {command.label}
                </span>
                {command.hint !== undefined && (
                  <span className="shrink-0 font-mono text-xs text-subtle">{command.hint}</span>
                )}
              </li>
            ))}
          </ul>
        )}

        <p className="border-t border-line px-4 py-2 text-xs text-subtle">{t('palette.footer')}</p>
      </div>
    </div>
  );
}

/**
 * Builds the command list.
 *
 * The catalogue queries only run while the palette is open: a console left on a
 * dashboard should not poll the agent list because a dialog might be opened.
 */
function usePaletteCommands(
  meta: Meta,
  open: boolean,
  onClose: () => void,
  onShowShortcuts: () => void,
): PaletteCommand[] {
  const t = useT();
  const { locale, setLocale } = useLocale();
  const navigate = useNavigate();
  const { resolved } = useThemePreference();

  const agents = useQuery({
    queryKey: ['agents'],
    queryFn: () => unwrap(client.GET('/api/agents')) as Promise<AgentDescriptor[]>,
    enabled: open,
  });
  const workflows = useQuery({
    queryKey: ['workflows'],
    queryFn: () => unwrap(client.GET('/api/workflows')) as Promise<WorkflowDescriptor[]>,
    enabled: open,
  });
  const runs = useQuery({
    queryKey: ['runs', 'palette'],
    queryFn: () =>
      unwrap(client.GET('/api/runs', { params: { query: { take: 50 } } })) as Promise<RunRecord[]>,
    enabled: open,
  });
  const sessions = useQuery({
    queryKey: ['sessions', 'palette'],
    queryFn: () =>
      unwrap(client.GET('/api/sessions', { params: { query: { take: 25 } } })) as Promise<SessionRecord[]>,
    enabled: open,
  });

  return useMemo(() => {
    const go = (path: string) => () => {
      onClose();
      navigate(path);
    };

    const commands: PaletteCommand[] = [];

    for (const group of NAV_GROUPS) {
      for (const item of group.items) {
        if (item.adminOnly === true && !meta.roles.canAdminister) {
          continue;
        }

        commands.push({
          id: `go:${item.path}`,
          group: t('palette.group.navigate'),
          label: t(item.label),
          keywords: `${item.english} ${item.path} ${t(group.group)}`,
          perform: go(item.path),
        });
      }
    }

    if (meta.roles.canAdminister) {
      commands.push({
        id: 'action:new-agent',
        group: t('palette.group.action'),
        label: t('palette.newAgent'),
        keywords: 'new agent create',
        perform: go('agents/new'),
      });
      commands.push({
        id: 'action:new-workflow',
        group: t('palette.group.action'),
        label: t('palette.newWorkflow'),
        keywords: 'new workflow create',
        perform: go('workflows/new'),
      });
    }

    commands.push({
      id: 'action:theme',
      group: t('palette.group.action'),
      label: t('palette.toggleTheme'),
      keywords: 'theme dark light',
      perform: () => {
        setThemePreference(resolved === 'dark' ? 'light' : 'dark');
        onClose();
      },
    });

    for (const candidate of LOCALES) {
      if (candidate === locale) {
        continue;
      }

      commands.push({
        id: `action:locale:${candidate}`,
        group: t('palette.group.action'),
        label: t('palette.switchLanguage', { language: t(`shell.language.${candidate}`) }),
        keywords: `language ${candidate}`,
        perform: () => {
          setLocale(candidate);
          onClose();
        },
      });
    }

    commands.push({
      id: 'action:shortcuts',
      group: t('palette.group.action'),
      label: t('palette.showShortcuts'),
      keywords: 'keyboard shortcuts',
      hint: '?',
      perform: () => {
        onClose();
        onShowShortcuts();
      },
    });

    for (const agent of agents.data ?? []) {
      commands.push({
        id: `agent:${agent.name}`,
        group: t('palette.group.agent'),
        label: agent.displayName ?? agent.name,
        keywords: agent.name,
        perform: go(`agents/${encodeURIComponent(agent.name)}`),
      });
      commands.push({
        id: `agent-run:${agent.name}`,
        group: t('palette.group.agent'),
        label: t('palette.runAgent', { name: agent.displayName ?? agent.name }),
        keywords: `${agent.name} playground run`,
        perform: go(`playground/${encodeURIComponent(agent.name)}`),
      });
    }

    for (const workflow of workflows.data ?? []) {
      commands.push({
        id: `workflow:${workflow.name}`,
        group: t('palette.group.workflow'),
        label: workflow.displayName ?? workflow.name,
        keywords: workflow.name,
        perform: go(`workflows/${encodeURIComponent(workflow.name)}`),
      });
    }

    for (const run of runs.data ?? []) {
      commands.push({
        id: `run:${run.id}`,
        group: t('palette.group.run'),
        label: run.agentName,
        keywords: run.id,
        hint: shortId(run.id),
        perform: go(`runs/${encodeURIComponent(run.id)}`),
      });
    }

    for (const session of sessions.data ?? []) {
      commands.push({
        id: `session:${session.id}`,
        group: t('palette.group.session'),
        label: session.agentName,
        keywords: session.id,
        hint: shortId(session.id),
        perform: go(`sessions/${encodeURIComponent(session.id)}`),
      });
    }

    return commands;
  }, [
    agents.data,
    locale,
    meta.roles.canAdminister,
    navigate,
    onClose,
    onShowShortcuts,
    runs.data,
    sessions.data,
    setLocale,
    t,
    workflows.data,
  ]);
}

/** The `?` cheat sheet. */
export function ShortcutHelp({ open, onClose }: { open: boolean; onClose: () => void }): ReactNode {
  const t = useT();

  const rows: { keys: string; label: string }[] = [
    { keys: '⌘/Ctrl + K', label: t('shortcuts.palette') },
    { keys: 'g a', label: t('shortcuts.goAgents') },
    { keys: 'g r', label: t('shortcuts.goRuns') },
    { keys: 'g s', label: t('shortcuts.goSessions') },
    { keys: 'g d', label: t('shortcuts.goDashboard') },
    { keys: 'g w', label: t('shortcuts.goWorkflows') },
    { keys: 'g p', label: t('shortcuts.goPlayground') },
    { keys: '/', label: t('shortcuts.focusSearch') },
    { keys: '⌘/Ctrl + ⏎', label: t('shortcuts.submit') },
    { keys: 'Esc', label: t('shortcuts.close') },
    { keys: '?', label: t('shortcuts.help') },
  ];

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title={t('shortcuts.title')}
      description={t('shortcuts.note')}
      width="max-w-sm"
      testId="shortcut-help"
    >
      <dl className="flex flex-col gap-1.5 px-4 py-3 text-base">
        {rows.map((row) => (
          <div key={row.keys} className="flex items-center justify-between gap-4">
            <dt className="text-muted">{row.label}</dt>
            <dd className="rounded-sm border border-line-strong bg-raised px-1.5 py-px font-mono text-xs">
              {row.keys}
            </dd>
          </div>
        ))}
      </dl>
    </Dialog>
  );
}
