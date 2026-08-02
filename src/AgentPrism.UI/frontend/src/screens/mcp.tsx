import { useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
import { relativeTime } from '../lib/format';
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
import type { McpServerRequest, McpTransportMode } from '../lib/types';

const EMPTY_FORM: McpServerRequest & { name: string } = {
  name: '',
  description: '',
  endpoint: '',
  transport: 'StreamableHttp',
  authorizationConfigurationKey: '',
  enabled: true,
  requiresApproval: true,
};

/**
 * Remote MCP servers and the persistent approval rules.
 *
 * Adding a server means accepting tool definitions from outside the host
 * application — a deliberate exception to "tools are defined in code only".
 * The screen states that boundary rather than hiding it, and it never asks for
 * a secret: only the *name* of the configuration key that holds one.
 */
export function McpScreen(): ReactNode {
  const client = useQueryClient();
  const [form, setForm] = useState(EMPTY_FORM);
  const [showForm, setShowForm] = useState(false);

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

  const removeRule = useMutation({
    mutationFn: (id: string) => api.deleteApprovalRule(id),
    onSuccess: () => void client.invalidateQueries({ queryKey: ['approval-rules'] }),
  });

  return (
    <>
      <PageHeader
        title="MCP & approvals"
        description="Remote Model Context Protocol servers, and the approvals you chose to remember."
        actions={
          <>
            <Button
              onClick={() => refresh.mutate()}
              busy={refresh.isPending}
              title="Rediscover tools now instead of waiting for the next background refresh."
            >
              Refresh tools
            </Button>
            <Button tone="primary" onClick={() => setShowForm((current) => !current)}>
              <PlusIcon className="size-3.5" />
              Add server
            </Button>
          </>
        }
      />

      <Panel className="mb-4">
        <div className="border-b border-line px-4 py-2.5 text-[12px] text-muted">
          <strong className="text-fg">Security boundary.</strong> Only <Mono>http</Mono> and{' '}
          <Mono>https</Mono> endpoints are accepted — a local process (stdio) transport would let
          anyone who reaches this console start a program on the server. Discovered tools require
          approval by default, and they can never take over the name of a tool defined in code.
        </div>
      </Panel>

      {showForm && (
        <Panel title="New server" className="mb-4">
          <form
            className="grid gap-3 p-4 sm:grid-cols-2"
            onSubmit={(event) => {
              event.preventDefault();
              save.mutate();
            }}
          >
            <Field label="Name" required hint="Discovered tools are prefixed with this name.">
              <TextInput
                value={form.name}
                required
                pattern="[a-zA-Z0-9_-]+"
                placeholder="github"
                onChange={(event) => setForm({ ...form, name: event.target.value })}
              />
            </Field>

            <Field label="Endpoint" required>
              <TextInput
                value={form.endpoint}
                required
                type="url"
                placeholder="https://mcp.example.com/mcp"
                onChange={(event) => setForm({ ...form, endpoint: event.target.value })}
              />
            </Field>

            <Field label="Transport">
              <Select
                value={form.transport}
                onChange={(value) => setForm({ ...form, transport: value as McpTransportMode })}
              >
                <option value="StreamableHttp">Streamable HTTP</option>
                <option value="Sse">SSE (legacy)</option>
              </Select>
            </Field>

            <Field
              label="Authorization configuration key"
              hint="The NAME of the configuration key, never the secret itself. Set the value with dotnet user-secrets."
            >
              <TextInput
                value={form.authorizationConfigurationKey ?? ''}
                placeholder="AgentPrism:Mcp:GithubToken"
                onChange={(event) =>
                  setForm({ ...form, authorizationConfigurationKey: event.target.value })
                }
              />
            </Field>

            <Field label="Description">
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
                Enabled
              </label>

              <label className="flex items-center gap-2 text-[13px]">
                <input
                  type="checkbox"
                  checked={form.requiresApproval}
                  onChange={(event) => setForm({ ...form, requiresApproval: event.target.checked })}
                />
                Require approval
              </label>
            </div>

            <div className="sm:col-span-2 flex items-center gap-2">
              <Button type="submit" tone="primary" busy={save.isPending}>
                Save
              </Button>
              <Button tone="ghost" onClick={() => setShowForm(false)}>
                Cancel
              </Button>
              {save.isError && <ErrorNote error={save.error} />}
            </div>
          </form>
        </Panel>
      )}

      <Panel title="Servers" className="mb-4">
        {servers.isPending && <Loading />}
        {servers.isError && <ErrorNote error={servers.error} />}

        {servers.isSuccess &&
          (servers.data.length === 0 ? (
            <Empty title="No MCP servers">
              AgentPrism works without them. Add one only when you want tools that live outside
              this application.
            </Empty>
          ) : (
            <Table>
              <thead>
                <tr>
                  <Th>Name</Th>
                  <Th>Endpoint</Th>
                  <Th>Auth key</Th>
                  <Th />
                  <Th />
                </tr>
              </thead>
              <tbody>
                {servers.data.map((server) => (
                  <tr key={server.id}>
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
                      {server.authorizationConfigurationKey != null &&
                      server.authorizationConfigurationKey.length > 0 ? (
                        <Mono className="text-[11px] text-muted">
                          {server.authorizationConfigurationKey}
                        </Mono>
                      ) : (
                        <span className="text-[11px] text-subtle">none</span>
                      )}
                    </Td>
                    <Td>
                      <div className="flex gap-1.5">
                        {server.enabled ? (
                          <Badge tone="accent">enabled</Badge>
                        ) : (
                          <Badge>disabled</Badge>
                        )}
                        {server.requiresApproval && <Badge tone="warn">approval</Badge>}
                      </div>
                    </Td>
                    <Td className="text-right">
                      <Button
                        tone="danger"
                        onClick={() => remove.mutate(server.name)}
                        title="Remove this server. Its tools disappear on the next refresh."
                      >
                        <TrashIcon className="size-3.5" />
                      </Button>
                    </Td>
                  </tr>
                ))}
              </tbody>
            </Table>
          ))}
      </Panel>

      <Panel title="Remembered approvals">
        {rules.isPending && <Loading />}
        {rules.isError && <ErrorNote error={rules.error} />}

        {rules.isSuccess &&
          (rules.data.length === 0 ? (
            <Empty title="Nothing remembered">
              When you approve a tool call and tick “don’t ask again”, the rule appears here and
              can be revoked.
            </Empty>
          ) : (
            <Table>
              <thead>
                <tr>
                  <Th>Tool</Th>
                  <Th>Agent</Th>
                  <Th>Scope</Th>
                  <Th>Created</Th>
                  <Th />
                </tr>
              </thead>
              <tbody>
                {rules.data.map((rule) => (
                  <tr key={rule.id}>
                    <Td>
                      <Mono className="font-semibold">{rule.toolName}</Mono>
                    </Td>
                    <Td>{rule.agentName ?? <span className="text-subtle">all agents</span>}</Td>
                    <Td>
                      {rule.argumentsHash != null && rule.argumentsHash.length > 0 ? (
                        <Badge title="Only calls with exactly these arguments are auto-approved.">
                          same arguments
                        </Badge>
                      ) : (
                        <Badge tone="warn" title="Every call to this tool is auto-approved.">
                          any arguments
                        </Badge>
                      )}
                    </Td>
                    <Td className="text-[11px] text-muted">{relativeTime(rule.createdAt)}</Td>
                    <Td className="text-right">
                      <Button tone="danger" onClick={() => removeRule.mutate(rule.id)}>
                        <TrashIcon className="size-3.5" />
                      </Button>
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
