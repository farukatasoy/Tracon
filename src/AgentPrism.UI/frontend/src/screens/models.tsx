import type { ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
import { count, latencyText, relativeTime } from '../lib/format';
import { Link } from '../lib/router';
import type { ModelProviderHealth, ModelProviderHealthStatus } from '../lib/types';
import {
  Badge,
  Button,
  Empty,
  ErrorNote,
  Loading,
  Mono,
  PageHeader,
  Panel,
  Table,
  Td,
  Th,
} from '../components/ui';

const STATUS_TONE: Record<ModelProviderHealthStatus, 'neutral' | 'success' | 'warn' | 'danger'> = {
  Unknown: 'neutral',
  Healthy: 'success',
  Degraded: 'warn',
  Unhealthy: 'danger',
};

const STATUS_LABEL: Record<ModelProviderHealthStatus, string> = {
  Unknown: 'unknown',
  Healthy: 'healthy',
  Degraded: 'degraded',
  Unhealthy: 'unhealthy',
};

function HealthBadge({ health }: { health: ModelProviderHealth | undefined }): ReactNode {
  const status = health?.status ?? 'Unknown';

  return (
    <Badge
      tone={STATUS_TONE[status]}
      title={
        status === 'Unknown'
          ? 'This provider does not implement the health-check contract.'
          : `Checked ${relativeTime(health?.checkedAt)}${health?.latency ? ` in ${latencyText(health.latency)}` : ''}`
      }
    >
      {STATUS_LABEL[status]}
    </Badge>
  );
}

/**
 * Registered providers, their model catalogue, and connectivity status.
 *
 * An empty catalogue is not an error. AgentPrism ships no built-in model list
 * (decision K-032): a package cannot keep provider model names current, and a
 * stale list produced a 403 model_not_found the day it was written.
 *
 * The catalogue is also not a validation list — a model name that is missing
 * here still works.
 *
 * Health checks never make a paid model call: they hit `GET {endpoint}/models`.
 * A provider that does not implement the check reports "unknown", not an error.
 */
export function ModelsScreen(): ReactNode {
  const client = useQueryClient();

  const providers = useQuery({ queryKey: ['models'], queryFn: api.models });
  const health = useQuery({
    queryKey: ['models-health'],
    queryFn: () => api.modelsHealth(),
    // While a provider looks unhealthy (possibly a circuit breaker counting
    // down its break duration), poll gently so the card catches up without a
    // dedicated countdown timer.
    refetchInterval: (query) =>
      query.state.data?.some((entry) => entry.status === 'Unhealthy') === true ? 5_000 : false,
  });

  const checkNow = useMutation({
    mutationFn: (name: string) => api.modelHealth(name, true),
    onSuccess: (result) => {
      client.setQueryData<ModelProviderHealth[]>(['models-health'], (current) =>
        current === undefined
          ? [result]
          : current.map((entry) => (entry.providerName === result.providerName ? result : entry)),
      );
    },
  });

  const healthByProvider = new Map((health.data ?? []).map((entry) => [entry.providerName, entry]));

  return (
    <>
      <PageHeader
        title="Models"
        description="Providers registered in the host application, the models configured for them, and whether they are reachable."
      />

      {providers.isPending && <Loading />}
      {providers.isError && <ErrorNote error={providers.error} />}

      {providers.isSuccess && providers.data.length === 0 && (
        <Panel>
          <Empty title="No providers registered">
            Register one in code, for example with <Mono>UseOpenAI(apiKey)</Mono> or{' '}
            <Mono>UseOpenAICompatible(name, ...)</Mono>. Without a provider an agent definition
            cannot be compiled.
          </Empty>
        </Panel>
      )}

      <div className="flex flex-col gap-3">
        {(providers.data ?? []).map((provider) => {
          const providerHealth = healthByProvider.get(provider.name);

          return (
            <Panel
              key={provider.name}
              title={
                <span className="flex items-center gap-2">
                  {provider.displayName ?? provider.name}
                  <Mono className="text-subtle">{provider.name}</Mono>
                  <HealthBadge health={providerHealth} />
                </span>
              }
              actions={
                <Button
                  onClick={() => checkNow.mutate(provider.name)}
                  busy={checkNow.isPending && checkNow.variables === provider.name}
                  title="Check connectivity now instead of waiting for the cached result to expire."
                >
                  Check now
                </Button>
              }
            >
              {providerHealth?.detail != null && providerHealth.detail.length > 0 && (
                <div className="border-b border-line px-4 py-2 text-[11px] text-muted">
                  {providerHealth.detail}
                </div>
              )}

              {provider.models.length === 0 ? (
                <Empty title="No models configured">
                  This is not an error. Add names under{' '}
                  <Mono>AgentPrism:Providers:OpenAI:Models</Mono> (or the matching
                  <Mono>OpenAICompatible:{'{name}'}</Mono> section) in configuration to see them
                  here. A model that is missing from this list can still be used — the catalogue
                  does not validate names.
                </Empty>
              ) : (
                <Table>
                  <thead>
                    <tr>
                      <Th>Model</Th>
                      <Th>Context</Th>
                      <Th>Max output</Th>
                      <Th>Capabilities</Th>
                    </tr>
                  </thead>
                  <tbody>
                    {provider.models.map((model) => (
                      <tr key={model.name} className="hover:bg-raised">
                        <Td>
                          <Mono className="font-medium">{model.name}</Mono>
                          {model.displayName != null && (
                            <span className="ml-2 text-muted">{model.displayName}</span>
                          )}
                        </Td>
                        <Td className="text-muted">{count(model.contextWindowTokens)}</Td>
                        <Td className="text-muted">{count(model.maxOutputTokens)}</Td>
                        <Td>
                          <div className="flex flex-wrap gap-1">
                            {model.supportsStreaming && <Badge tone="info">streaming</Badge>}
                            {model.supportsTools && <Badge tone="accent">tools</Badge>}
                            {model.supportsReasoning && <Badge tone="warn">reasoning</Badge>}
                          </div>
                        </Td>
                      </tr>
                    ))}
                  </tbody>
                </Table>
              )}
            </Panel>
          );
        })}
      </div>

      <p className="mt-4 text-[11px] text-subtle">
        Runs record which model answered; token and cost use per model is on the{' '}
        <Link to="dashboard" className="text-accent hover:underline">
          Dashboard
        </Link>
        . Cost only shows once a price is configured (providers do not publish machine-readable
        pricing) — see <Mono>AgentPrism:Pricing</Mono>.
      </p>
    </>
  );
}
