import { Fragment, useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
import { relativeTime } from '../lib/format';
import { useT } from '../lib/i18n';
import { McpServerDetail } from '../components/mcp-server-detail';
import {
  Badge,
  Button,
  Empty,
  ErrorNote,
  Field,
  Loading,
  Mono,
  PageHeader,
  Panel,
  Select,
  Table,
  Td,
  TextInput,
  Th,
} from '../components/ui';
import { PlusIcon, TrashIcon } from '../components/icons';
import type { McpServerRequest, McpTransportMode, Meta } from '../lib/types';

const EMPTY_FORM: McpServerRequest & { name: string } = {
  name: '',
  description: '',
  endpoint: '',
  transport: 'StreamableHttp',
  authorizationConfigurationKey: '',
  enabled: true,
  requiresApproval: true,
  oauthEnabled: false,
  oauthClientId: '',
  oauthClientSecretConfigurationKey: '',
  oauthScopes: '',
};

/**
 * Remote MCP servers and the persistent approval rules.
 *
 * Adding a server means accepting tool definitions from outside the host
 * application — a deliberate exception to "tools are defined in code only".
 * The screen states that boundary rather than hiding it, and it never asks for
 * a secret: only the *name* of the configuration key that holds one.
 */
export function McpScreen({ meta }: { meta: Meta }): ReactNode {
  const t = useT();
  const client = useQueryClient();
  const [form, setForm] = useState(EMPTY_FORM);
  const [showForm, setShowForm] = useState(false);
  const [expanded, setExpanded] = useState<string | null>(null);

  const servers = useQuery({ queryKey: ['mcp-servers'], queryFn: api.mcpServers });
  const rules = useQuery({ queryKey: ['approval-rules'], queryFn: api.approvalRules });

  const invalidate = (): void => {
    void client.invalidateQueries({ queryKey: ['mcp-servers'] });
    void client.invalidateQueries({ queryKey: ['tools'] });
  };

  const save = useMutation({
    mutationFn: () => {
      const { name, ...body } = form;

      return api.saveMcpServer(name, body);
    },
    onSuccess: () => {
      setForm(EMPTY_FORM);
      setShowForm(false);
      invalidate();
    },
  });

  const remove = useMutation({
    mutationFn: (name: string) => api.deleteMcpServer(name),
    onSuccess: invalidate,
  });

  const refresh = useMutation({
    mutationFn: api.refreshMcpTools,
    onSuccess: invalidate,
  });

  const authorize = useMutation({
    mutationFn: (name: string) => api.startMcpOAuth(name),
    onSuccess: (result) => {
      // Opened with an opener reference on purpose: the callback page detects
      // `window.opener` and closes itself once the flow completes.
      window.open(result.authorizationUri, '_blank');
    },
  });

  const removeRule = useMutation({
    mutationFn: (id: string) => api.deleteApprovalRule(id),
    onSuccess: () => void client.invalidateQueries({ queryKey: ['approval-rules'] }),
  });

  return (
    <>
      <PageHeader
        title={t('mcp.title')}
        description={t('mcp.description')}
        actions={
          meta.roles.canAdminister && (
            <>
              <Button
                onClick={() => refresh.mutate()}
                busy={refresh.isPending}
                title={t('mcp.refreshTitle')}
              >
                {t('mcp.refreshTools')}
              </Button>
              <Button tone="primary" onClick={() => setShowForm((current) => !current)}>
                <PlusIcon className="size-3.5" />
                {t('mcp.addServer')}
              </Button>
            </>
          )
        }
      />

      <Panel className="mb-4">
        <div className="border-b border-line px-4 py-2.5 text-[12px] text-muted">
          <strong className="text-fg">{t('mcp.boundary.title')}</strong> {t('mcp.boundary.before')}{' '}
          <Mono>http</Mono> / <Mono>https</Mono>. {t('mcp.boundary.after')}
        </div>
      </Panel>

      {showForm && (
        <Panel title={t('mcp.newServer')} className="mb-4">
          <form
            className="grid gap-3 p-4 sm:grid-cols-2"
            onSubmit={(event) => {
              event.preventDefault();
              save.mutate();
            }}
          >
            <Field label={t('common.name')} required hint={t('mcp.nameHint')}>
              <TextInput
                value={form.name}
                required
                pattern="[a-zA-Z0-9_-]+"
                placeholder="github"
                onChange={(event) => setForm({ ...form, name: event.target.value })}
              />
            </Field>

            <Field label={t('mcp.endpoint')} required>
              <TextInput
                value={form.endpoint}
                required
                type="url"
                placeholder="https://mcp.example.com/mcp"
                onChange={(event) => setForm({ ...form, endpoint: event.target.value })}
              />
            </Field>

            <Field label={t('mcp.transport')}>
              <Select
                value={form.transport}
                onChange={(value) => setForm({ ...form, transport: value as McpTransportMode })}
              >
                <option value="StreamableHttp">Streamable HTTP</option>
                <option value="Sse">{t('mcp.sseLegacy')}</option>
              </Select>
            </Field>

            <Field
              label={t('mcp.authKey')}
              hint={t('mcp.authKeyHint')}
            >
              <TextInput
                value={form.authorizationConfigurationKey ?? ''}
                placeholder="AgentPrism:Mcp:GithubToken"
                onChange={(event) =>
                  setForm({ ...form, authorizationConfigurationKey: event.target.value })
                }
              />
            </Field>

            <Field label={t('common.description')}>
              <TextInput
                value={form.description ?? ''}
                onChange={(event) => setForm({ ...form, description: event.target.value })}
              />
            </Field>

            <div className="flex items-end gap-4">
              <label className="flex items-center gap-2 text-[13px]">
                <input
                  type="checkbox"
                  checked={form.enabled}
                  onChange={(event) => setForm({ ...form, enabled: event.target.checked })}
                />
                {t('common.enabled')}
              </label>

              <label className="flex items-center gap-2 text-[13px]">
                <input
                  type="checkbox"
                  checked={form.requiresApproval}
                  onChange={(event) => setForm({ ...form, requiresApproval: event.target.checked })}
                />
                {t('mcp.requireApproval')}
              </label>
            </div>

            <div className="sm:col-span-2 border-t border-line pt-3">
              <label className="flex items-center gap-2 text-[13px] font-medium">
                <input
                  type="checkbox"
                  checked={form.oauthEnabled ?? false}
                  onChange={(event) =>
                    setForm({
                      ...form,
                      oauthEnabled: event.target.checked,
                      // Mutually exclusive with the static Authorization header —
                      // both would try to own the same header.
                      authorizationConfigurationKey: event.target.checked
                        ? ''
                        : form.authorizationConfigurationKey,
                    })
                  }
                />
                {t('mcp.oauth')}
              </label>
              <p className="mt-1 text-[11px] text-muted">
                {t('mcp.oauthNotice')} <Mono>AgentPrism:Mcp:OAuthCallbackBaseUri</Mono>.
              </p>
            </div>

            {form.oauthEnabled === true && (
              <>
                <Field label={t('mcp.oauthClientId')} required>
                  <TextInput
                    value={form.oauthClientId ?? ''}
                    required
                    onChange={(event) => setForm({ ...form, oauthClientId: event.target.value })}
                  />
                </Field>

                <Field
                  label={t('mcp.oauthSecretKey')}
                  hint={t('mcp.oauthSecretKeyHint')}
                >
                  <TextInput
                    value={form.oauthClientSecretConfigurationKey ?? ''}
                    placeholder="AgentPrism:Mcp:GithubClientSecret"
                    onChange={(event) =>
                      setForm({ ...form, oauthClientSecretConfigurationKey: event.target.value })
                    }
                  />
                </Field>

                <Field label={t('mcp.oauthScopes')} hint={t('mcp.oauthScopesHint')}>
                  <TextInput
                    value={form.oauthScopes ?? ''}
                    placeholder="repo read:user"
                    onChange={(event) => setForm({ ...form, oauthScopes: event.target.value })}
                  />
                </Field>
              </>
            )}

            <div className="sm:col-span-2 flex items-center gap-2">
              <Button type="submit" tone="primary" busy={save.isPending}>
                {t('common.save')}
              </Button>
              <Button tone="ghost" onClick={() => setShowForm(false)}>
                {t('common.cancel')}
              </Button>
              {save.isError && <ErrorNote error={save.error} />}
            </div>
          </form>
        </Panel>
      )}

      <Panel title={t('mcp.servers')} className="mb-4">
        {servers.isPending && <Loading />}
        {servers.isError && <ErrorNote error={servers.error} />}

        {servers.isSuccess &&
          (servers.data.length === 0 ? (
            <Empty title={t('mcp.noServers.title')}>{t('mcp.noServers.body')}</Empty>
          ) : (
            <Table>
              <thead>
                <tr>
                  <Th>{t('common.name')}</Th>
                  <Th>{t('mcp.endpoint')}</Th>
                  <Th>{t('mcp.auth')}</Th>
                  <Th />
                  <Th />
                </tr>
              </thead>
              <tbody>
                {servers.data.map((server) => (
                  <Fragment key={server.id}>
                    <tr>
                      <Td>
                        <Mono className="font-semibold">{server.name}</Mono>
                        {server.description != null && server.description.length > 0 && (
                          <p className="text-[11px] text-subtle">{server.description}</p>
                        )}
                      </Td>
                      <Td className="max-w-xs truncate">
                        <Mono className="text-[11px]">{server.endpoint}</Mono>
                      </Td>
                      <Td>
                        {server.oauthEnabled ? (
                          <Mono className="text-[11px] text-muted">
                            OAuth: {server.oauthClientId}
                          </Mono>
                        ) : server.authorizationConfigurationKey != null &&
                          server.authorizationConfigurationKey.length > 0 ? (
                          <Mono className="text-[11px] text-muted">
                            {server.authorizationConfigurationKey}
                          </Mono>
                        ) : (
                          <span className="text-[11px] text-subtle">{t('common.none')}</span>
                        )}
                      </Td>
                      <Td>
                        <div className="flex gap-1.5">
                          {server.enabled ? (
                            <Badge tone="accent">{t('common.enabled')}</Badge>
                          ) : (
                            <Badge>{t('common.disabled')}</Badge>
                          )}
                          {server.requiresApproval && <Badge tone="warn">{t('mcp.approval')}</Badge>}
                        </div>
                      </Td>
                      <Td className="text-right">
                        <div className="flex justify-end gap-1.5">
                          <Button
                            onClick={() =>
                              setExpanded((current) => (current === server.name ? null : server.name))
                            }
                          >
                            {expanded === server.name ? t('audit.hide') : t('mcp.promptsAndResources')}
                          </Button>
                          {server.oauthEnabled && meta.roles.canAdminister && (
                            <Button
                              busy={authorize.isPending && authorize.variables === server.name}
                              onClick={() => authorize.mutate(server.name)}
                              title={t('mcp.authorizeTitle')}
                            >
                              {t('mcp.authorize')}
                            </Button>
                          )}
                          {meta.roles.canAdminister && (
                            <Button
                              tone="danger"
                              onClick={() => remove.mutate(server.name)}
                              title={t('mcp.removeServerTitle')}
                            >
                              <TrashIcon className="size-3.5" />
                            </Button>
                          )}
                        </div>
                      </Td>
                    </tr>
                    {expanded === server.name && (
                      <tr>
                        <td colSpan={5} className="p-0">
                          <McpServerDetail serverName={server.name} roles={meta.roles} />
                        </td>
                      </tr>
                    )}
                  </Fragment>
                ))}
              </tbody>
            </Table>
          ))}
      </Panel>

      <Panel title={t('mcp.rememberedApprovals')}>
        {rules.isPending && <Loading />}
        {rules.isError && <ErrorNote error={rules.error} />}

        {rules.isSuccess &&
          (rules.data.length === 0 ? (
            <Empty title={t('mcp.noRules.title')}>{t('mcp.noRules.body')}</Empty>
          ) : (
            <Table>
              <thead>
                <tr>
                  <Th>{t('mcp.tool')}</Th>
                  <Th>{t('common.agent')}</Th>
                  <Th>{t('mcp.scope')}</Th>
                  <Th>{t('common.created')}</Th>
                  <Th />
                </tr>
              </thead>
              <tbody>
                {rules.data.map((rule) => (
                  <tr key={rule.id}>
                    <Td>
                      <Mono className="font-semibold">{rule.toolName}</Mono>
                    </Td>
                    <Td>{rule.agentName ?? <span className="text-subtle">{t('runs.allAgents')}</span>}</Td>
                    <Td>
                      {rule.argumentsHash != null && rule.argumentsHash.length > 0 ? (
                        <Badge title={t('mcp.sameArgumentsTitle')}>{t('mcp.sameArguments')}</Badge>
                      ) : (
                        <Badge tone="warn" title={t('mcp.anyArgumentsTitle')}>
                          {t('mcp.anyArguments')}
                        </Badge>
                      )}
                    </Td>
                    <Td className="text-[11px] text-muted">{relativeTime(rule.createdAt)}</Td>
                    <Td className="text-right">
                      {meta.roles.canAdminister && (
                        <Button tone="danger" onClick={() => removeRule.mutate(rule.id)}>
                          <TrashIcon className="size-3.5" />
                        </Button>
                      )}
                    </Td>
                  </tr>
                ))}
              </tbody>
            </Table>
          ))}
      </Panel>
    </>
  );
}
