import type { UseQueryResult } from '@tanstack/react-query';
import type { Dispatch, ReactNode, SetStateAction } from 'react';
import { useT } from '../../../lib/i18n';
import { Badge, ErrorNote, Loading, Mono, Panel } from '../../../components/ui';
import type { ToolDescriptor } from '../../../lib/server-types';
import type { FormState } from '../model';

export function ToolsSection({
  form,
  setForm,
  tools,
}: {
  form: FormState;
  setForm: Dispatch<SetStateAction<FormState>>;
  tools: UseQueryResult<ToolDescriptor[]>;
}): ReactNode {
  const t = useT();

  return (
    <Panel title={t('common.tools')}>
      <div className="p-4">
        <p className="mb-3 text-sm text-muted">{t('agentEditor.toolsNotice')}</p>

        {tools.isPending && <Loading rows={4} />}
        {tools.isError && <ErrorNote error={tools.error} onRetry={() => void tools.refetch()} />}

        {tools.isSuccess && tools.data.length === 0 && (
          <p className="text-base text-subtle">
            {t('agentEditor.noTools')} <Mono>AddTool(...)</Mono> / <Mono>AddToolsFrom(typeof(...))</Mono>
          </p>
        )}

        <div className="flex flex-col gap-1.5">
          {(tools.data ?? []).map((tool) => {
            const checked = form.toolNames.includes(tool.name);

            return (
              <label
                key={tool.name}
                className="flex cursor-pointer items-start gap-2.5 rounded-md border border-line px-3 py-2 hover:bg-raised"
              >
                <input
                  type="checkbox"
                  className="mt-0.5 accent-[var(--tracon-accent)]"
                  checked={checked}
                  onChange={() =>
                    setForm({
                      ...form,
                      toolNames: checked
                        ? form.toolNames.filter((item) => item !== tool.name)
                        : [...form.toolNames, tool.name],
                    })
                  }
                />
                <span className="min-w-0">
                  <Mono className="font-medium">{tool.name}</Mono>
                  {tool.requiresApproval && (
                    <Badge tone="warn" description={t('agentEditor.approvalTitle')}>
                      approval
                    </Badge>
                  )}
                  {tool.runsOnClient && (
                    <Badge tone="accent" description={t('agentEditor.runsOnClientTitle')}>
                      {t('tools.runsOnClient')}
                    </Badge>
                  )}
                  {tool.description !== null && tool.description !== undefined && (
                    <span className="block text-sm text-muted">{tool.description}</span>
                  )}
                </span>
              </label>
            );
          })}
        </div>
      </div>
    </Panel>
  );
}
