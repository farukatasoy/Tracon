import { useState, type ReactNode } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { useT } from '../lib/i18n';
import { Badge, Button, Empty, ErrorNote, Loading, Mono, Panel, Tabs, Unauthorized } from './ui';
import type {
  TraconRoleMeta as RoleMeta,
  McpPromptContent,
  McpResourceSummary,
} from '@tracon/client';
import type { McpPromptSummary, McpResourceContent } from '../lib/server-types';
import { Tooltip } from './tooltip';

/**
 * A registered MCP server's prompts and resources (Phase 22.1 / 22.2).
 *
 * Prompt content is a SNAPSHOT: the button copies it to the clipboard for
 * pasting into an agent's instructions, it never wires the agent to fetch it
 * live. A remote server changing its prompt must never change agent behavior
 * without a human re-copying it (decision, docs/arsiv/fazlar/22-MCP-DERINLESMESI.md §22.1).
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
      <div className="px-3 pt-3">
        <Tabs
          label={t('mcp.promptsAndResources')}
          value={tab}
          onChange={setTab}
          panels={[
            {
              id: 'prompts',
              label: t('mcp.prompts'),
              render: () =>
                // 🚨 Refused, not empty. This used to be an `Empty`, which reads
                // as "this server exposes no prompts" — the opposite of what it
                // means.
                roles.canAdminister ? (
                  <PromptsTab serverName={serverName} />
                ) : (
                  <Unauthorized requires="administrator" />
                ),
            },
            {
              id: 'resources',
              label: t('skills.resources'),
              render: () =>
                roles.canRead ? (
                  <ResourcesTab serverName={serverName} canRead={roles.canOperate} />
                ) : (
                  <Unauthorized requires="operator" />
                ),
            },
          ]}
        />
      </div>
    </Panel>
  );
}

function PromptsTab({ serverName }: { serverName: string }): ReactNode {
  const t = useT();
  const [copiedName, setCopiedName] = useState<string | null>(null);

  const prompts = useQuery({
    queryKey: ['mcp-prompts', serverName],
    queryFn: () =>
      unwrap(
        client.GET('/api/mcp-servers/{name}/prompts', { params: { path: { name: serverName } } }),
      ) as Promise<McpPromptSummary[]>,
  });

  const fetchContent = useMutation({
    mutationFn: (prompt: string) =>
      unwrap(
        client.POST('/api/mcp-servers/{name}/prompts/{prompt}', {
          params: { path: { name: serverName, prompt } },
          body: {},
        }),
      ) as Promise<McpPromptContent>,
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
    return <Loading rows={3} />;
  }

  if (prompts.isError) {
    return (
      <div className="p-4">
        <ErrorNote error={prompts.error} onRetry={() => void prompts.refetch()} />
      </div>
    );
  }

  if (prompts.data.length === 0) {
    // No action: the prompts are the remote server's own, and this console
    // cannot add one. Refreshing the catalogue is the MCP screen's button.
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
            <Mono className="text-sm font-semibold">{prompt.title ?? prompt.name}</Mono>
            {prompt.description != null && prompt.description.length > 0 && (
              <p className="truncate text-xs text-subtle">{prompt.description}</p>
            )}
            {prompt.arguments.length > 0 && (
              <p className="text-xs text-muted">
                {t('mcp.args')} {prompt.arguments.map((argument) => argument.name).join(', ')}
              </p>
            )}
          </div>
          <Tooltip text={t('mcp.copyPromptTitle')}>
            <Button
              busy={fetchContent.isPending && fetchContent.variables === prompt.name}
              onClick={() => fetchContent.mutate(prompt.name)}
            >
              {copiedName === prompt.name ? t('mcp.copied') : t('common.copy')}
            </Button>
          </Tooltip>
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
    queryFn: () =>
      unwrap(
        client.GET('/api/mcp-servers/{name}/resources', { params: { path: { name: serverName } } }),
      ) as Promise<McpResourceSummary[]>,
  });

  const preview = useQuery({
    queryKey: ['mcp-resource-content', serverName, previewUri],
    queryFn: () =>
      unwrap(
        client.GET('/api/mcp-servers/{name}/resources/read', {
          params: { path: { name: serverName }, query: { uri: previewUri as string } },
        }),
      ) as Promise<McpResourceContent>,
    enabled: previewUri != null,
  });

  if (resources.isPending) {
    return <Loading rows={3} />;
  }

  if (resources.isError) {
    return (
      <div className="p-4">
        <ErrorNote error={resources.error} onRetry={() => void resources.refetch()} />
      </div>
    );
  }

  if (resources.data.length === 0) {
    // Same as prompts: the remote server decides what it exposes.
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
              <Mono className="truncate text-sm font-semibold">{resource.name}</Mono>
              <p className="truncate text-xs text-subtle">{resource.uri}</p>
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
          {preview.isPending && <Loading rows={2} />}
          {preview.isError && (
            <ErrorNote error={preview.error} onRetry={() => void preview.refetch()} />
          )}
          {preview.isSuccess &&
            (preview.data.isBinary ? (
              <p className="text-xs text-muted">
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
                <pre className="max-h-64 overflow-auto whitespace-pre-wrap text-xs text-fg">
                  {preview.data.text}
                </pre>
              </>
            ))}
        </div>
      )}
    </>
  );
}
