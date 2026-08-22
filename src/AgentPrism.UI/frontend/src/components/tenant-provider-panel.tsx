import { useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { relativeTime } from '../lib/format';
import { useT } from '../lib/i18n';
import { Badge, Button, Empty, ErrorNote, Field, Loading, Mono, Panel, Select, TextInput } from './ui';
import type { TenantProviderBindingResponse as TenantProviderBinding } from '@agentprism/client';
import type { ModelProviderDescriptor } from '../lib/server-types';

/**
 * Per-tenant model provider bindings (BYOK) and egress policy (phase 65).
 *
 * No field here ever carries a credential value: a binding stores only the
 * NAME of the configuration key the value is read from at call time, and
 * whether it currently resolves. The value itself is set through
 * `dotnet user-secrets`, an environment variable, or a key vault — never
 * through this screen.
 */
export function TenantProviderPanel(): ReactNode {
  const t = useT();
  const current = useQuery({
    queryKey: ['current-tenant'],
    queryFn: () => unwrap(client.GET('/api/tenants/current')),
  });
  const [tenantId, setTenantId] = useState<string | null>(null);
  const effectiveTenantId = tenantId ?? current.data?.tenantId ?? '';

  return (
    <Panel title={t('tenantProviders.title')} className="lg:col-span-2">
      <div className="border-b border-line p-4">
        <Field label={t('tenantProviders.tenantId')} hint={t('tenantProviders.tenantIdHint')}>
          <TextInput
            value={effectiveTenantId}
            data-testid="tenant-provider-tenant-id"
            onChange={(event) => setTenantId(event.target.value)}
          />
        </Field>
      </div>

      {effectiveTenantId.trim() === '' ? (
        <Empty title={t('tenantProviders.empty.title')}>{t('tenantProviders.empty.body')}</Empty>
      ) : (
        <>
          <BindingsSection tenantId={effectiveTenantId} />
          <EgressSection tenantId={effectiveTenantId} />
        </>
      )}
    </Panel>
  );
}

function BindingsSection({ tenantId }: { tenantId: string }): ReactNode {
  const t = useT();
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(false);

  const providers = useQuery({
    queryKey: ['models'],
    queryFn: () => unwrap(client.GET('/api/models')) as Promise<ModelProviderDescriptor[]>,
  });
  const bindings = useQuery({
    queryKey: ['tenant-provider-bindings', tenantId],
    queryFn: () =>
      unwrap(client.GET('/api/tenants/{tenantId}/providers', { params: { path: { tenantId } } })),
  });

  const remove = useMutation({
    mutationFn: (provider: string) =>
      unwrap(
        client.DELETE('/api/tenants/{tenantId}/providers/{provider}', {
          params: { path: { tenantId, provider } },
        }),
      ),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['tenant-provider-bindings', tenantId] }),
  });

  return (
    <div className="border-b border-line">
      <div className="flex items-center justify-between px-4 py-2.5">
        <h3 className="text-[12px] font-semibold text-muted">{t('tenantProviders.bindings.title')}</h3>
        <Button tone="default" onClick={() => setOpen((value) => !value)} testId="tenant-provider-add">
          {open ? t('common.close') : t('tenantProviders.bindings.add')}
        </Button>
      </div>

      {bindings.isPending && <Loading />}
      {bindings.isError && (
        <div className="px-4 pb-3">
          <ErrorNote error={bindings.error} />
        </div>
      )}

      {open && (
        <BindingForm
          tenantId={tenantId}
          providerNames={providers.data?.map((provider) => provider.name) ?? []}
          onSaved={() => {
            setOpen(false);
            void queryClient.invalidateQueries({ queryKey: ['tenant-provider-bindings', tenantId] });
          }}
          onCancel={() => setOpen(false)}
        />
      )}

      {bindings.isSuccess &&
        (bindings.data.length === 0 ? (
          <Empty title={t('tenantProviders.bindings.empty.title')}>
            {t('tenantProviders.bindings.empty.body')}
          </Empty>
        ) : (
          <div className="divide-y divide-line">
            {bindings.data.map((binding) => (
              <BindingRow
                key={binding.providerName}
                binding={binding}
                onDelete={() => remove.mutate(binding.providerName)}
              />
            ))}
          </div>
        ))}

      <p className="px-4 py-2.5 text-[11px] text-subtle">{t('tenantProviders.bindings.notice')}</p>
    </div>
  );
}

function BindingRow({
  binding,
  onDelete,
}: {
  binding: TenantProviderBinding;
  onDelete: () => void;
}): ReactNode {
  const t = useT();

  return (
    <div className="px-4 py-3" data-testid="tenant-provider-binding-row">
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-[13px] font-medium">{binding.providerName}</span>
        {binding.resolved ? (
          <Badge tone="success">{t('tenantProviders.bindings.resolved')}</Badge>
        ) : (
          <Badge tone="danger">{t('tenantProviders.bindings.unresolved')}</Badge>
        )}
        <div className="ml-auto">
          <Button tone="danger" onClick={onDelete}>
            {t('common.delete')}
          </Button>
        </div>
      </div>

      <div className="mt-1 text-[12px] text-muted">
        <Mono>{binding.apiKeyConfigurationName}</Mono>
      </div>

      {binding.endpoint != null && binding.endpoint !== '' && (
        <div className="mt-1 text-[12px] text-muted">
          <Mono>{binding.endpoint}</Mono>
        </div>
      )}

      <div className="mt-1.5 text-[11px] text-subtle">
        {t('tenantProviders.bindings.updated')}: {relativeTime(binding.updatedAt)}
      </div>
    </div>
  );
}

function BindingForm({
  tenantId,
  providerNames,
  onSaved,
  onCancel,
}: {
  tenantId: string;
  providerNames: string[];
  onSaved: () => void;
  onCancel: () => void;
}): ReactNode {
  const t = useT();
  const [provider, setProvider] = useState(providerNames[0] ?? '');
  const [configKeyName, setConfigKeyName] = useState('');
  const [endpoint, setEndpoint] = useState('');

  const save = useMutation({
    mutationFn: () =>
      unwrap(
        client.PUT('/api/tenants/{tenantId}/providers/{provider}', {
          params: { path: { tenantId, provider } },
          body: {
            apiKeyConfigurationName: configKeyName,
            endpoint: endpoint.trim() === '' ? null : endpoint,
          },
        }),
      ),
    onSuccess: onSaved,
  });

  return (
    <div className="space-y-3 border-b border-line bg-raised/40 p-4">
      <div className="grid gap-3 sm:grid-cols-2">
        <Field label={t('tenantProviders.bindings.provider')} required>
          {providerNames.length > 0 ? (
            <Select value={provider} onChange={setProvider} testId="tenant-provider-select">
              {providerNames.map((name) => (
                <option key={name} value={name}>
                  {name}
                </option>
              ))}
            </Select>
          ) : (
            <TextInput
              value={provider}
              placeholder="openai"
              onChange={(event) => setProvider(event.target.value)}
            />
          )}
        </Field>
        <Field
          label={t('tenantProviders.bindings.configKeyName')}
          hint={t('tenantProviders.bindings.configKeyNameHint')}
          required
        >
          <TextInput
            value={configKeyName}
            placeholder="AgentPrism:ProviderKeys:Acme:OpenAI"
            data-testid="tenant-provider-config-key"
            onChange={(event) => setConfigKeyName(event.target.value)}
          />
        </Field>
      </div>

      <Field label={t('tenantProviders.bindings.endpoint')} hint={t('tenantProviders.bindings.endpointHint')}>
        <TextInput
          value={endpoint}
          placeholder="https://proxy.example.com/"
          onChange={(event) => setEndpoint(event.target.value)}
        />
      </Field>

      {save.isError && <ErrorNote error={save.error} />}

      <div className="flex items-center gap-2">
        <Button
          tone="primary"
          disabled={save.isPending || provider.trim() === '' || configKeyName.trim() === ''}
          testId="tenant-provider-save"
          onClick={() => save.mutate()}
        >
          {save.isPending ? t('common.saving') : t('common.save')}
        </Button>
        <Button tone="ghost" onClick={onCancel}>
          {t('common.cancel')}
        </Button>
      </div>
    </div>
  );
}

function EgressSection({ tenantId }: { tenantId: string }): ReactNode {
  const t = useT();
  const queryClient = useQueryClient();

  const providers = useQuery({
    queryKey: ['models'],
    queryFn: () => unwrap(client.GET('/api/models')) as Promise<ModelProviderDescriptor[]>,
  });
  const policy = useQuery({
    queryKey: ['tenant-egress-policy', tenantId],
    queryFn: () => unwrap(client.GET('/api/tenants/{tenantId}/egress', { params: { path: { tenantId } } })),
  });

  const [draft, setDraft] = useState<string[] | null>(null);

  const save = useMutation({
    mutationFn: (allowedProviders: string[]) =>
      unwrap(
        client.PUT('/api/tenants/{tenantId}/egress', {
          params: { path: { tenantId } },
          body: { allowedProviders },
        }),
      ),
    onSuccess: () => {
      setDraft(null);
      void queryClient.invalidateQueries({ queryKey: ['tenant-egress-policy', tenantId] });
    },
  });

  const clear = useMutation({
    mutationFn: () =>
      unwrap(client.DELETE('/api/tenants/{tenantId}/egress', { params: { path: { tenantId } } })),
    onSuccess: () => {
      setDraft(null);
      void queryClient.invalidateQueries({ queryKey: ['tenant-egress-policy', tenantId] });
    },
  });

  const allowedProviders = draft ?? policy.data?.allowedProviders ?? null;
  const restricted = allowedProviders != null;

  const toggle = (name: string) => {
    const current = allowedProviders ?? [];

    setDraft(current.includes(name) ? current.filter((value) => value !== name) : [...current, name]);
  };

  return (
    <div>
      <div className="px-4 py-2.5">
        <h3 className="text-[12px] font-semibold text-muted">{t('tenantProviders.egress.title')}</h3>
      </div>

      {policy.isPending && <Loading />}
      {policy.isError && (
        <div className="px-4 pb-3">
          <ErrorNote error={policy.error} />
        </div>
      )}

      {policy.isSuccess && (
        <div className="space-y-3 px-4 pb-4">
          {!restricted && draft === null ? (
            <div className="flex items-center gap-2">
              <Badge tone="neutral">{t('tenantProviders.egress.unrestricted')}</Badge>
              <Button tone="default" onClick={() => setDraft([])}>
                {t('tenantProviders.egress.restrict')}
              </Button>
            </div>
          ) : (
            <>
              <div className="flex flex-wrap gap-1.5">
                {(providers.data ?? []).map((provider) => {
                  const selected = (allowedProviders ?? []).includes(provider.name);

                  return (
                    <button
                      key={provider.name}
                      type="button"
                      onClick={() => toggle(provider.name)}
                      className={
                        selected
                          ? 'rounded border border-transparent bg-accent-soft px-1.5 py-0.5 text-[11px] font-medium text-accent'
                          : 'rounded border border-line bg-raised px-1.5 py-0.5 text-[11px] font-medium text-muted'
                      }
                    >
                      {provider.name}
                    </button>
                  );
                })}
              </div>

              <div className="flex items-center gap-2">
                <Button
                  tone="primary"
                  disabled={save.isPending}
                  testId="tenant-egress-save"
                  onClick={() => save.mutate(allowedProviders ?? [])}
                >
                  {save.isPending ? t('common.saving') : t('common.save')}
                </Button>
                <Button tone="ghost" onClick={() => setDraft(null)}>
                  {t('common.cancel')}
                </Button>
                {restricted && (
                  <Button
                    tone="danger"
                    disabled={clear.isPending}
                    onClick={() => clear.mutate()}
                  >
                    {t('tenantProviders.egress.clear')}
                  </Button>
                )}
              </div>
            </>
          )}

          {save.isError && <ErrorNote error={save.error} />}
          {clear.isError && <ErrorNote error={clear.error} />}
        </div>
      )}

      <p className="border-t border-line px-4 py-2.5 text-[11px] text-subtle">
        {t('tenantProviders.egress.notice')}
      </p>
    </div>
  );
}
