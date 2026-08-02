import type { ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from '../lib/api';
import { count } from '../lib/format';
import {
  Badge,
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

/**
 * Registered providers and their model catalogue.
 *
 * An empty catalogue is not an error. AgentPrism ships no built-in model list
 * (decision K-032): a package cannot keep provider model names current, and a
 * stale list produced a 403 model_not_found the day it was written.
 *
 * The catalogue is also not a validation list — a model name that is missing
 * here still works.
 */
export function ModelsScreen(): ReactNode {
  const providers = useQuery({ queryKey: ['models'], queryFn: api.models });

  return (
    <>
      <PageHeader
        title="Models"
        description="Providers registered in the host application, and the models configured for them."
      />

      {providers.isPending && <Loading />}
      {providers.isError && <ErrorNote error={providers.error} />}

      {providers.isSuccess && providers.data.length === 0 && (
        <Panel>
          <Empty title="No providers registered">
            Register one in code, for example with <Mono>UseOpenAI(apiKey)</Mono>. Without a
            provider an agent definition cannot be compiled.
          </Empty>
        </Panel>
      )}

      <div className="flex flex-col gap-3">
        {(providers.data ?? []).map((provider) => (
          <Panel
            key={provider.name}
            title={
              <span className="flex items-center gap-2">
                {provider.displayName ?? provider.name}
                <Mono className="text-subtle">{provider.name}</Mono>
              </span>
            }
          >
            {provider.models.length === 0 ? (
              <Empty title="No models configured">
                This is not an error. Add names under{' '}
                <Mono>AgentPrism:Providers:OpenAI:Models</Mono> in configuration to see them here.
                A model that is missing from this list can still be used — the catalogue does not
                validate names.
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
        ))}
      </div>

      <p className="mt-4 text-[11px] text-subtle">
        Provider connectivity checks and cost reporting arrive with the observability phase. Cost
        needs the model name on the run record, which is not stored yet.
      </p>
    </>
  );
}
