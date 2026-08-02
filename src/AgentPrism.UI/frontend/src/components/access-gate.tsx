import { useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { ApiError, api } from '../lib/api';
import { setToken, useToken } from '../lib/auth';
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
  const token = useToken();

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
    return <Centered><Loading label="Connecting" /></Centered>;
  }

  if (meta.isError) {
    return (
      <Centered>
        <Card title="Cannot reach AgentPrism">
          <p className="text-[13px] text-muted">
            The management API did not answer at <code className="font-mono">api/meta</code>.
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
    return <Centered><TokenPrompt failed={token !== null} /></Centered>;
  }

  if (meta.data.authentication.requiresBearerToken && token === null) {
    return <Centered><TokenPrompt failed={false} /></Centered>;
  }

  if (error instanceof ApiError && error.status === 403) {
    return (
      <Centered>
        <Card title="Access denied">
          <p className="text-[13px] text-muted">{error.detail ?? error.title}</p>
          {!meta.data.authentication.allowRemoteAccess && (
            <p className="mt-3 text-[12px] text-subtle">
              Remote access is off. AgentPrism only answers requests from the same machine
              unless <code className="font-mono">AllowRemoteAccess</code> is enabled together
              with an authentication method.
            </p>
          )}
          {meta.data.authentication.requiresAuthorizationPolicy && (
            <p className="mt-3 text-[12px] text-subtle">
              An authorization policy is configured. Sign in to the host application first.
            </p>
          )}
        </Card>
      </Centered>
    );
  }

  if (probe.isPending) {
    return <Centered><Loading label="Connecting" /></Centered>;
  }

  return children(meta.data);
}

function TokenPrompt({ failed }: { failed: boolean }): ReactNode {
  const [value, setValue] = useState('');

  return (
    <Card title="Access token required">
      <p className="mb-4 text-[13px] text-muted">
        This AgentPrism instance is protected by a bearer token. The token is kept for this
        browser tab only and is never written to disk.
      </p>

      <form
        onSubmit={(event) => {
          event.preventDefault();
          setToken(value.trim());
        }}
      >
        <Field label="Token">
          <TextInput
            type="password"
            autoFocus
            autoComplete="off"
            value={value}
            placeholder="Bearer token"
            onChange={(event) => setValue(event.target.value)}
          />
        </Field>

        {failed && (
          <p className="mt-2 text-[12px] text-danger">
            That token was rejected. Check the value configured in{' '}
            <code className="font-mono">AgentPrismEndpointOptions.AuthToken</code>.
          </p>
        )}

        <div className="mt-4">
          <Button type="submit" tone="primary" disabled={value.trim().length === 0}>
            Continue
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
