import { useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { relativeTime } from '../lib/format';
import { useT } from '../lib/i18n';
import {
  Badge,
  Button,
  CopyButton,
  Empty,
  ErrorNote,
  Field,
  Loading,
  Mono,
  Panel,
  TextInput,
} from './ui';
import type { ApiKeyCreationResult, ApiKeyScope } from '@agentprism/client';
import type { ApiKeyRecord } from '../lib/server-types';

/** Every scope AgentPrism recognises. Mirrors `ApiKeyScope` on the server. */
const SCOPES: ApiKeyScope[] = [
  'RunsRead',
  'RunsWrite',
  'AgentsRead',
  'AgentsAdmin',
  'ExternalInvoke',
  'KnowledgeRead',
  'KnowledgeAdmin',
  'WorkflowsRead',
  'WorkflowsAdmin',
  'EvalsRead',
  'EvalsAdmin',
  'ExperimentsRead',
  'ExperimentsAdmin',
  'PlatformRead',
  'PlatformAdmin',
  'SecurityAdmin',
  'AuditRead',
];

/**
 * Tenant-scoped API keys — a second identity source next to the single
 * static bearer token (Phase 53).
 *
 * No field on this screen carries a raw key value after creation: the store
 * keeps only a SHA-256 hash, and the record type has no field for it at all.
 * The raw value the create call returns is held in local state only, for as
 * long as this panel stays open.
 */
export function ApiKeyPanel(): ReactNode {
  const t = useT();
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(false);
  const [revealed, setRevealed] = useState<ApiKeyCreationResult | null>(null);

  const keys = useQuery({
    queryKey: ['api-keys'],
    queryFn: () => unwrap(client.GET('/api/api-keys')) as Promise<ApiKeyRecord[]>,
  });

  const revoke = useMutation({
    mutationFn: (id: string) => unwrap(client.DELETE('/api/api-keys/{id}', { params: { path: { id } } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['api-keys'] }),
  });

  return (
    <Panel
      title={t('apiKeys.title')}
      className="lg:col-span-2"
      actions={
        <Button tone="default" onClick={() => setOpen((value) => !value)}>
          {open ? t('common.close') : t('apiKeys.add')}
        </Button>
      }
    >
      {keys.isPending && <Loading />}
      {keys.isError && (
        <div className="p-4">
          <ErrorNote error={keys.error} />
        </div>
      )}

      {revealed && (
        <div className="space-y-2 border-b border-line bg-raised/40 p-4">
          <p className="text-[12px] font-medium">
            {t('apiKeys.newKeyTitle')}: {revealed.record.name}
          </p>
          <div className="relative">
            <CopyButton value={revealed.plaintextKey} />
            <div
              className="overflow-auto rounded-md border border-line bg-raised p-3 pr-10 font-mono text-[12px]"
              data-testid="api-key-plaintext"
            >
              {revealed.plaintextKey}
            </div>
          </div>
          <p className="text-[11px] text-subtle">{t('apiKeys.newKeyNotice')}</p>
          <Button tone="ghost" onClick={() => setRevealed(null)}>
            {t('common.close')}
          </Button>
        </div>
      )}

      {open && (
        <ApiKeyForm
          onCreated={(result) => {
            setOpen(false);
            setRevealed(result);
            void queryClient.invalidateQueries({ queryKey: ['api-keys'] });
          }}
          onCancel={() => setOpen(false)}
        />
      )}

      {keys.isSuccess &&
        (keys.data.length === 0 ? (
          <Empty title={t('apiKeys.empty.title')}>{t('apiKeys.empty.body')}</Empty>
        ) : (
          <div className="divide-y divide-line">
            {keys.data.map((record) => (
              <KeyRow key={record.id} record={record} onRevoke={() => revoke.mutate(record.id)} />
            ))}
          </div>
        ))}

      <p className="border-t border-line px-4 py-2.5 text-[11px] text-subtle">{t('apiKeys.notice')}</p>
    </Panel>
  );
}

function KeyRow({ record, onRevoke }: { record: ApiKeyRecord; onRevoke: () => void }): ReactNode {
  const t = useT();

  return (
    <div className="px-4 py-3" data-testid="api-key-row">
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-[13px] font-medium">{record.name}</span>
        {record.isActive ? (
          <Badge tone="success">{t('apiKeys.active')}</Badge>
        ) : (
          <Badge tone="danger">{t('apiKeys.revoked')}</Badge>
        )}
        <div className="ml-auto flex items-center gap-1.5">
          {record.isActive && (
            <Button tone="danger" onClick={onRevoke}>
              {t('apiKeys.revoke')}
            </Button>
          )}
        </div>
      </div>

      <div className="mt-1 text-[12px] text-muted">
        <Mono>{record.keyPrefix}…</Mono>
      </div>

      <div className="mt-1.5 flex flex-wrap gap-1">
        {record.scopes.map((scope) => (
          <Badge key={scope} tone="neutral">
            {t(`apiKeys.scope.${scope}`)}
          </Badge>
        ))}
      </div>

      <div className="mt-1.5 text-[11px] text-subtle">
        {t('apiKeys.created')}: {relativeTime(record.createdAt)}
        {' · '}
        {record.lastUsedAt != null
          ? `${t('apiKeys.lastUsed')}: ${relativeTime(record.lastUsedAt)}`
          : t('apiKeys.never')}
        {record.expiresAt != null && ` · ${t('apiKeys.expiresAt')}: ${relativeTime(record.expiresAt)}`}
      </div>
    </div>
  );
}

function ApiKeyForm({
  onCreated,
  onCancel,
}: {
  onCreated: (result: ApiKeyCreationResult) => void;
  onCancel: () => void;
}): ReactNode {
  const t = useT();
  const [name, setName] = useState('');
  const [scopes, setScopes] = useState<ApiKeyScope[]>([]);
  const [expiresAt, setExpiresAt] = useState('');

  const save = useMutation({
    mutationFn: () =>
      unwrap(
        client.POST('/api/api-keys', {
          body: {
            name,
            scopes,
            expiresAt: expiresAt.trim() === '' ? null : new Date(expiresAt).toISOString(),
          },
        }),
      ),
    onSuccess: onCreated,
  });

  return (
    <div className="space-y-3 border-b border-line bg-raised/40 p-4">
      <div className="grid gap-3 sm:grid-cols-2">
        <Field label={t('apiKeys.name')} required>
          <TextInput
            value={name}
            placeholder="ci"
            data-testid="api-key-name"
            onChange={(event) => setName(event.target.value)}
          />
        </Field>
        <Field label={t('apiKeys.expiresAt')} hint={t('apiKeys.expiresAtHint')}>
          <TextInput
            type="date"
            value={expiresAt}
            data-testid="api-key-expires"
            onChange={(event) => setExpiresAt(event.target.value)}
          />
        </Field>
      </div>

      <Field label={t('apiKeys.scopes')} required>
        <div className="flex flex-wrap gap-1.5">
          {SCOPES.map((scope) => {
            const selected = scopes.includes(scope);

            return (
              <button
                key={scope}
                type="button"
                onClick={() =>
                  setScopes((current) =>
                    current.includes(scope)
                      ? current.filter((value) => value !== scope)
                      : [...current, scope],
                  )
                }
                className={
                  selected
                    ? 'rounded border border-transparent bg-accent-soft px-1.5 py-0.5 text-[11px] font-medium text-accent'
                    : 'rounded border border-line bg-raised px-1.5 py-0.5 text-[11px] font-medium text-muted'
                }
              >
                {t(`apiKeys.scope.${scope}`)}
              </button>
            );
          })}
        </div>
      </Field>

      {save.isError && <ErrorNote error={save.error} />}

      <div className="flex items-center gap-2">
        <Button
          tone="primary"
          disabled={save.isPending || name.trim() === '' || scopes.length === 0}
          testId="api-key-save"
          onClick={() => save.mutate()}
        >
          {save.isPending ? t('common.saving') : t('apiKeys.save')}
        </Button>
        <Button tone="ghost" onClick={onCancel}>
          {t('common.cancel')}
        </Button>
      </div>
    </div>
  );
}
