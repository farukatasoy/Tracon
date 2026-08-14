import { useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { ApiError, api } from '../lib/api';
import { setToken, useToken, useTokenRejected } from '../lib/auth';
import { useT } from '../lib/i18n';
import { Button, Field, Loading, Panel, TextInput } from './ui';
import { PrismMark } from './icons';
import type { Meta } from '../lib/types';

/**
 * Decides whether the console can talk to the API, and explains it when it cannot.
 *
 * `/api/meta` is reachable without authentication (decision K-010) precisely so
 * this screen can exist: the console has no other way to learn which of the
 * three access layers is switched on.
 */
export function AccessGate({ children }: { children: (meta: Meta) => ReactNode }): ReactNode {
  const t = useT();
  const token = useToken();
  const rejected = useTokenRejected();

  const meta = useQuery({ queryKey: ['meta'], queryFn: api.meta, retry: false });

  // The shell is served without the bearer-token check, so reaching this code
  // says nothing about whether data endpoints will answer. One cheap probe does.
  const probe = useQuery({
    queryKey: ['probe', token],
    queryFn: api.agents,
    enabled: meta.isSuccess,
    retry: false,
  });

  if (meta.isPending) {
    return <Centered><Loading label={t('access.connecting')} /></Centered>;
  }

  if (meta.isError) {
    return (
      <Centered>
        <Card title={t('access.unreachable.title')}>
          <p className="text-[13px] text-muted">
            {t('access.unreachable.body', { path: 'api/meta' })}
          </p>
          <p className="mt-2 text-[12px] text-subtle">
            {meta.error instanceof Error ? meta.error.message : String(meta.error)}
          </p>
        </Card>
      </Centered>
    );
  }

  const error = probe.error;

  if (error instanceof ApiError && error.status === 401) {
    return <Centered><TokenPrompt failed={rejected} /></Centered>;
  }

  if (meta.data.authentication.requiresBearerToken && token === null) {
    return <Centered><TokenPrompt failed={false} /></Centered>;
  }

  if (error instanceof ApiError && error.status === 403) {
    return (
      <Centered>
        <Card title={t('access.denied.title')}>
          {/* Server text, shown as it came: the API contract is single-language. */}
          <p className="text-[13px] text-muted">{error.detail ?? error.title}</p>
          {!meta.data.authentication.allowRemoteAccess && (
            <p className="mt-3 text-[12px] text-subtle">{t('access.denied.remote')}</p>
          )}
          {meta.data.authentication.requiresAuthorizationPolicy && (
            <p className="mt-3 text-[12px] text-subtle">{t('access.denied.policy')}</p>
          )}
        </Card>
      </Centered>
    );
  }

  if (probe.isPending) {
    return <Centered><Loading label={t('access.connecting')} /></Centered>;
  }

  return children(meta.data);
}

function TokenPrompt({ failed }: { failed: boolean }): ReactNode {
  const t = useT();
  const [value, setValue] = useState('');

  return (
    <Card title={t('access.token.title')}>
      <p className="mb-4 text-[13px] text-muted">{t('access.token.body')}</p>

      <form
        onSubmit={(event) => {
          event.preventDefault();
          setToken(value.trim());
        }}
      >
        <Field label={t('access.token.label')}>
          <TextInput
            type="password"
            autoFocus
            autoComplete="off"
            value={value}
            placeholder={t('access.token.placeholder')}
            onChange={(event) => setValue(event.target.value)}
          />
        </Field>

        {failed && (
          <p role="alert" className="mt-2 text-[12px] text-danger">
            {t('access.token.rejected', { setting: 'AgentPrismEndpointOptions.AuthToken' })}
          </p>
        )}

        <div className="mt-4">
          <Button type="submit" tone="primary" disabled={value.trim().length === 0}>
            {t('access.continue')}
          </Button>
        </div>
      </form>
    </Card>
  );
}

function Centered({ children }: { children: ReactNode }): ReactNode {
  return <div className="flex min-h-screen items-center justify-center px-4">{children}</div>;
}

function Card({ title, children }: { title: string; children: ReactNode }): ReactNode {
  return (
    <div className="w-full max-w-md">
      <div className="mb-5 flex items-center gap-2.5">
        <PrismMark className="size-7 text-fg" />
        <span className="text-lg font-semibold tracking-tight">AgentPrism</span>
      </div>
      <Panel className="p-5">
        <h1 className="mb-2 text-[15px] font-semibold">{title}</h1>
        {children}
      </Panel>
    </div>
  );
}
