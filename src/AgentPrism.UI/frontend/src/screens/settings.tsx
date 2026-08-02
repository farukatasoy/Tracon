import { useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from '../lib/api';
import { apiBase, uiBase } from '../lib/base';
import { setToken, useToken } from '../lib/auth';
import { readThemePreference, writeThemePreference, applyTheme, type ThemePreference } from '../lib/theme';
import { count } from '../lib/format';
import {
  Badge,
  Button,
  ErrorNote,
  Loading,
  Mono,
  PageHeader,
  Panel,
  Select,
} from '../components/ui';
import type { Meta } from '../lib/types';

export function SettingsScreen({ meta }: { meta: Meta }): ReactNode {
  const token = useToken();
  const [preference, setPreference] = useState<ThemePreference>(readThemePreference);

  const stats = useQuery({ queryKey: ['stats', ''], queryFn: () => api.stats({}) });

  return (
    <>
      <PageHeader
        title="Settings"
        description="What this instance is running, how it is protected and where its data lives."
      />

      <div className="grid gap-4 lg:grid-cols-2">
        <Panel title="Instance">
          <dl className="divide-y divide-line">
            <Row label="Version"><Mono>{meta.version}</Mono></Row>
            <Row label="Prefix"><Mono>{meta.prefix}</Mono></Row>
            <Row label="UI base"><Mono>{uiBase}</Mono></Row>
            <Row label="API base"><Mono>{apiBase}</Mono></Row>
          </dl>
        </Panel>

        <Panel title="Access">
          <dl className="divide-y divide-line">
            <Row label="Remote access">
              {meta.authentication.allowRemoteAccess ? (
                <Badge tone="warn">enabled</Badge>
              ) : (
                <Badge tone="success" title="Only requests from this machine are served.">
                  loopback only
                </Badge>
              )}
            </Row>
            <Row label="Bearer token">
              {meta.authentication.requiresBearerToken ? (
                <Badge tone="accent">required</Badge>
              ) : (
                <span className="text-subtle">not configured</span>
              )}
            </Row>
            <Row label="Authorization policy">
              {meta.authentication.requiresAuthorizationPolicy ? (
                <Badge tone="accent">applied</Badge>
              ) : (
                <span className="text-subtle">not configured</span>
              )}
            </Row>
            {token !== null && (
              <Row label="This tab">
                <div className="flex items-center gap-2">
                  <Badge tone="success">token stored</Badge>
                  <Button tone="ghost" onClick={() => setToken(null)}>
                    Forget
                  </Button>
                </div>
              </Row>
            )}
          </dl>
          <p className="border-t border-line px-4 py-2.5 text-[11px] text-subtle">
            The token is held in this browser tab only and is never written to disk. Secrets are
            never returned by the API and never appear on this screen.
          </p>
        </Panel>

        <Panel title="Storage">
          <dl className="divide-y divide-line">
            <Row label="Mode">
              {meta.storage.persistent ? (
                <Badge tone="success">persistent</Badge>
              ) : (
                <Badge tone="warn">in-memory</Badge>
              )}
            </Row>
            <Row label="Agent definitions"><Mono>{meta.storage.agentDefinitionStore}</Mono></Row>
            <Row label="Runs"><Mono>{meta.storage.runStore}</Mono></Row>
            <Row label="Sessions"><Mono>{meta.storage.sessionStore}</Mono></Row>
          </dl>
          {!meta.storage.persistent && (
            <p className="border-t border-line px-4 py-2.5 text-[11px] text-subtle">
              In-memory storage is a supported mode, not a fallback for a broken setup. Its limits
              are the process lifetime and a single node. Call{' '}
              <Mono>UsePostgreSql(connectionString)</Mono> to persist.
            </p>
          )}
        </Panel>

        <Panel title="Console">
          <div className="p-4">
            <label className="block">
              <span className="mb-1 block text-[12px] font-medium text-muted">Theme</span>
              <Select
                value={preference}
                onChange={(value) => {
                  const next = value as ThemePreference;

                  setPreference(next);
                  writeThemePreference(next);
                  applyTheme(next);
                }}
              >
                <option value="system">Follow system</option>
                <option value="light">Light</option>
                <option value="dark">Dark</option>
              </Select>
            </label>
          </div>
        </Panel>

        <Panel title="Activity">
          {stats.isPending && <Loading />}
          {stats.isError && <div className="p-4"><ErrorNote error={stats.error} /></div>}
          {stats.isSuccess && (
            <dl className="divide-y divide-line">
              <Row label="Runs">{count(stats.data.totalRuns)}</Row>
              <Row label="Completed">{count(stats.data.completedRuns)}</Row>
              <Row label="Failed">{count(stats.data.failedRuns)}</Row>
              <Row label="Running">{count(stats.data.runningRuns)}</Row>
              <Row label="Total tokens">{count(stats.data.totalTokens)}</Row>
            </dl>
          )}
          <p className="border-t border-line px-4 py-2.5 text-[11px] text-subtle">
            Cost is not reported: the run record does not store which model answered, so there is
            nothing to price. It arrives with the observability phase.
          </p>
        </Panel>
      </div>
    </>
  );
}

function Row({ label, children }: { label: string; children: ReactNode }): ReactNode {
  return (
    <div className="flex items-center gap-4 px-4 py-2">
      <dt className="w-40 shrink-0 text-[12px] text-subtle">{label}</dt>
      <dd className="min-w-0 flex-1 text-[13px]">{children}</dd>
    </div>
  );
}
