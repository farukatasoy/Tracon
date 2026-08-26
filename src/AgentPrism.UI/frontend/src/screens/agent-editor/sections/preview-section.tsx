import type { ReactNode } from 'react';
import type { AgentDefinitionRequest } from '@agentprism/client';
import { useT } from '../../../lib/i18n';
import { JsonView, Mono, Panel } from '../../../components/ui';

export function PreviewSection({
  request,
  editing,
  name,
}: {
  request: AgentDefinitionRequest;
  editing: boolean;
  name: string | undefined;
}): ReactNode {
  const t = useT();

  return (
    <Panel title={t('agentEditor.preview')}>
      <div className="p-4">
        <p className="mb-3 text-[12px] text-muted">
          {t('agentEditor.previewNotice')}{' '}
          <Mono>{editing ? `PUT api/agents/${name}` : 'POST api/agents'}</Mono>
        </p>
        <JsonView value={request} maxHeight="max-h-[32rem]" />
      </div>
    </Panel>
  );
}
