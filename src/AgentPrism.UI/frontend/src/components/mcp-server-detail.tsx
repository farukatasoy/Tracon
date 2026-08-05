import { useState, type ReactNode } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { api } from '../lib/api';
import { useT } from '../lib/i18n';
import { Badge, Button, Empty, ErrorNote, Loading, Mono, Panel } from './ui';
import type { RoleMeta } from '../lib/types';

/**
 * A registered MCP server's prompts and resources (Faz 22.1 / 22.2).
 *
 * Prompt content is a SNAPSHOT: the button copies it to the clipboard for
 * pasting into an agent's instructions, it never wires the agent to fetch it
 * live. A remote server changing its prompt must never change agent behavior
 * without a human re-copying it (decision, docs/22-MCP-DERINLESMESI.md §22.1).
 */
export function McpServerDetail({
  serverName,
  roles,
}: {
  serverName: string;
  roles: RoleMeta;
}): ReactNode {
  const t = useT();
  const [tab, setTab] = useState<'prompts' | 'resources'>('prompts');

  return (
    <Panel className="mt-2 border-dashed">
      <div className="flex gap-1 border-b border-line px-3 pt-2">
        <TabButton active={tab === 'prompts'} onClick={() => setTab('prompts')}>
          {t('mcp.prompts')}
        </TabButton>
        <TabButton active={tab === 'resources'} onClick={() => setTab('resources')}>
          {t('skills.resources')}
        </TabButton>
      </div>

      {tab === 'prompts' ? (
        roles.canAdminister ? (
          <PromptsTab serverName={serverName} />
        ) : (
          <Empty title={t('mcp.adminRequired')}>{t('mcp.promptsNeedAdmin')}</Empty>
        )
      ) : roles.canRead ? (
        <ResourcesTab serverName={serverName} canRead={roles.canOperate} />
      ) : (
        <Empty title={t('mcp.readerRequired')}>{t('mcp.resourcesNeedReader')}</Empty>
      )}
    </Panel>
  );
}

function TabButton({
  active,
  onClick,
  children,
}: {
  active: boolean;
  onClick: () => void;
  children: ReactNode;
}): ReactNode {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`border-b-2 px-2 pb-2 text-[12px] font-medium ${
        active ? 'border-accent text-fg' : 'border-transparent text-muted hover:text-fg'
      }`}
    >
      {children}
    </button>
  );
}

function PromptsTab({ serverName }: { serverName: string }): ReactNode {
  const t = useT();
  const [copiedName, setCopiedName] = useState<string | null>(null);

  const prompts = useQuery({
    queryKey: ['mcp-prompts', serverName],
    queryFn: () => api.mcpPrompts(serverName),
  });

  const fetchContent = useMutation({
    mutationFn: (prompt: string) => api.mcpPromptContent(serverName, prompt, {}),
    onSuccess: (content, prompt) => {
      // The header stays ENGLISH on purpose: it is pasted into an agent's
      // instructions, which are prompt text sent to a model, not UI copy.
      const note =
        `# Imported from MCP prompt '${serverName}:${prompt}'\n` +
        `# Snapshot hash: ${content.hash} — this text will NOT update on its own.\n` +
        `# Store the source and hash in the agent's metadata as ` +
        `mcp.prompt.server / mcp.prompt.name / mcp.prompt.hash.\n\n${content.text}`;

      void navigator.clipboard.writeText(note);
      setCopiedName(prompt);
    },
  });

  if (prompts.isPending) {
    return <Loading />;
  }

  if (prompts.isError) {
    return (
      <div className="p-4">
        <ErrorNote error={prompts.error} />
      </div>
    );
  }

  if (prompts.data.length === 0) {
    return (
      <Empty title={t('mcp.noPrompts')}>
        {t('mcp.noCapabilityBefore')} <Mono>prompts</Mono> {t('mcp.noCapabilityAfter')}
      </Empty>
    );
  }

  return (
    <ul className="divide-y divide-line">
      {prompts.data.map((prompt) => (
        <li key={prompt.name} className="flex items-center justify-between gap-3 px-3 py-2">
          <div className="min-w-0">
            <Mono className="text-[12px] font-semibold">{prompt.title ?? prompt.name}</Mono>
            {prompt.description != null && prompt.description.length > 0 && (
              <p className="truncate text-[11px] text-subtle">{prompt.description}</p>
            )}
            {prompt.arguments.length > 0 && (
              <p className="text-[11px] text-muted">
                {t('mcp.args')} {prompt.arguments.map((argument) => argument.name).join(', ')}
              </p>
            )}
          </div>
          <Button
            busy={fetchContent.isPending && fetchContent.variables === prompt.name}
            onClick={() => fetchContent.mutate(prompt.name)}
            title={t('mcp.copyPromptTitle')}
          >
            {copiedName === prompt.name ? t('mcp.copied') : t('common.copy')}
          </Button>
        </li>
      ))}
    </ul>
  );
}

function ResourcesTab({
  serverName,
  canRead,
}: {
  serverName: string;
  canRead: boolean;
}): ReactNode {
  const t = useT();
  const [previewUri, setPreviewUri] = useState<string | null>(null);

  const resources = useQuery({
    queryKey: ['mcp-resources', serverName],
    queryFn: () => api.mcpResources(serverName),
  });

  const preview = useQuery({
    queryKey: ['mcp-resource-content', serverName, previewUri],
    queryFn: () => api.mcpResourceContent(serverName, previewUri!),
    enabled: previewUri != null,
  });

  if (resources.isPending) {
    return <Loading />;
  }

  if (resources.isError) {
    return (
      <div className="p-4">
        <ErrorNote error={resources.error} />
      </div>
    );
  }

  if (resources.data.length === 0) {
    return (
      <Empty title={t('mcp.noResources')}>
        {t('mcp.noCapabilityBefore')} <Mono>resources</Mono> {t('mcp.noCapabilityAfter')}
      </Empty>
    );
  }

  return (
    <>
      <ul className="divide-y divide-line">
        {resources.data.map((resource) => (
          <li key={resource.uri} className="flex items-center justify-between gap-3 px-3 py-2">
            <div className="min-w-0">
              <Mono className="truncate text-[12px] font-semibold">{resource.name}</Mono>
              <p className="truncate text-[11px] text-subtle">{resource.uri}</p>
            </div>
            {canRead && (
              <Button onClick={() => setPreviewUri(resource.uri)}>
                {previewUri === resource.uri ? t('common.refresh') : t('mcp.preview')}
              </Button>
            )}
          </li>
        ))}
      </ul>

      {previewUri != null && (
        <div className="border-t border-line p-3">
          {preview.isPending && <Loading />}
          {preview.isError && <ErrorNote error={preview.error} />}
          {preview.isSuccess &&
            (preview.data.isBinary ? (
              <p className="text-[11px] text-muted">
                {t('mcp.binaryContent', {
                  bytes: preview.data.byteSize,
                  type: preview.data.mimeType ?? t('mcp.unknownType'),
                })}
              </p>
            ) : (
              <>
                {preview.data.truncated && (
                  <div className="mb-2">
                    <Badge tone="warn">{t('mcp.truncated')}</Badge>
                  </div>
                )}
                <pre className="max-h-64 overflow-auto whitespace-pre-wrap text-[11px] text-fg">
                  {preview.data.text}
                </pre>
              </>
            ))}
        </div>
      )}
    </>
  );
}
