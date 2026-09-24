import { Fragment, useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { absoluteTime, relativeTime } from '../lib/format';
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
  Unauthorized,
} from '../components/ui';
import { Toolbar } from '../components/toolbar';
import { Tooltip } from '../components/tooltip';
import { PlusIcon, TrashIcon } from '../components/icons';
import type {
  TraconMetaResponse as Meta,
  McpServerRequest,
  McpTransportMode,
  ToolArgumentCondition,
  ToolArgumentOperator,
} from '@tracon/client';
import type { McpServerDefinition, ToolApprovalRule } from '../lib/server-types';

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
 * What the list shows in the authentication column: the configuration key the
 * `Authorization` header is read from (the deprecated field or its
 * `headerConfigurationKeys` entry), else the names of the other headers read
 * from configuration. A server authenticated only by `X-Api-Key` would
 * otherwise show "none".
 */
function credentialSummary(server: McpServerDefinition): string | null {
  const keyed = Object.entries(server.headerConfigurationKeys);
  const authorization = keyed.find(([header]) => header.toLowerCase() === 'authorization');

  if (authorization != null) {
    return authorization[1];
  }

  if (server.authorizationConfigurationKey != null && server.authorizationConfigurationKey.length > 0) {
    return server.authorizationConfigurationKey;
  }

  return keyed.length > 0 ? keyed.map(([header]) => header).join(', ') : null;
}

/** A condition row as edited in the form; `value` stays raw text until submit. */
interface ConditionRow {
  path: string;
  operator: ToolArgumentOperator;
  value: string;
}

interface RuleForm {
  toolName: string;
  agentName: string;
  conditions: ConditionRow[];
}

const EMPTY_RULE_FORM: RuleForm = { toolName: '', agentName: '', conditions: [] };

const EMPTY_CONDITION_ROW: ConditionRow = { path: '', operator: 'Equals', value: '' };

const OPERATOR_SYMBOLS: Record<ToolArgumentOperator, string> = {
  Equals: '=',
  NotEquals: '≠',
  GreaterThan: '>',
  GreaterThanOrEqual: '≥',
  LessThan: '<',
  LessThanOrEqual: '≤',
  In: '∈',
  NotIn: '∉',
};

/**
 * Parses a scalar token typed by hand into the JSON kind it looks like. No
 * quoting syntax: "true"/"false" become booleans, a number-looking token
 * becomes a number, anything else stays text.
 */
function parseScalar(token: string): string | number | boolean {
  const trimmed = token.trim();

  if (trimmed.toLowerCase() === 'true') return true;
  if (trimmed.toLowerCase() === 'false') return false;

  const numeric = Number(trimmed);

  return trimmed.length > 0 && Number.isFinite(numeric) ? numeric : trimmed;
}

/**
 * Builds the JSON value a condition row sends, following the operator's
 * expected shape (K2: no expressions, a closed operator set decides the shape).
 */
function buildConditionValue(operator: ToolArgumentOperator, raw: string): ToolArgumentCondition['value'] {
  if (operator === 'In' || operator === 'NotIn') {
    return raw
      .split(',')
      .map((token) => token.trim())
      .filter((token) => token.length > 0)
      .map((token) => {
        const value = parseScalar(token);

        // The server's In/NotIn only accepts text or numbers, never booleans
        // (docs/arsiv/fazlar/63-ARGUMAN-DUZEYINDE-ONAY-POLITIKASI.md, 63.2) — a "true"/"false"
        // token inside a list stays text rather than becoming an invalid element.
        return typeof value === 'boolean' ? token : value;
      });
  }

  if (
    operator === 'GreaterThan' ||
    operator === 'GreaterThanOrEqual' ||
    operator === 'LessThan' ||
    operator === 'LessThanOrEqual'
  ) {
    return Number(raw.trim());
  }

  return parseScalar(raw);
}

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
  const queryClient = useQueryClient();
  const [form, setForm] = useState(EMPTY_FORM);
  const [showForm, setShowForm] = useState(false);
  const [expanded, setExpanded] = useState<string | null>(null);
  const [ruleForm, setRuleForm] = useState(EMPTY_RULE_FORM);
  const [showRuleForm, setShowRuleForm] = useState(false);
  const [serverQuery, setServerQuery] = useState('');
  const [ruleQuery, setRuleQuery] = useState('');

  const servers = useQuery({
    queryKey: ['mcp-servers'],
    queryFn: () => unwrap(client.GET('/api/mcp-servers')) as Promise<McpServerDefinition[]>,
  });
  const rules = useQuery({
    queryKey: ['approval-rules'],
    queryFn: () => unwrap(client.GET('/api/approvals/rules')) as Promise<ToolApprovalRule[]>,
    // 🚨 Not requested at all without the role — the same reason the prompts tab
    // above gives: a reader used to get the 403 rendered as a generic failure
    // with a "try again" button, which reads as "the console is broken" rather
    // than "this panel is not yours", and retrying can never succeed.
    enabled: meta.roles.canAdminister,
  });

  const invalidate = (): void => {
    void queryClient.invalidateQueries({ queryKey: ['mcp-servers'] });
    void queryClient.invalidateQueries({ queryKey: ['tools'] });
  };

  const save = useMutation({
    mutationFn: () => {
      const { name, ...body } = form;

      return unwrap(client.PUT('/api/mcp-servers/{name}', { params: { path: { name } }, body }));
    },
    onSuccess: () => {
      setForm(EMPTY_FORM);
      setShowForm(false);
      invalidate();
    },
  });

  const remove = useMutation({
    mutationFn: (name: string) =>
      unwrap(client.DELETE('/api/mcp-servers/{name}', { params: { path: { name } } })),
    onSuccess: invalidate,
  });

  const refresh = useMutation({
    mutationFn: () => unwrap(client.POST('/api/mcp-servers/refresh', {})),
    onSuccess: invalidate,
  });

  const authorize = useMutation({
    mutationFn: (name: string) =>
      unwrap(client.POST('/api/mcp-servers/{name}/oauth/start', { params: { path: { name } } })),
    onSuccess: (result) => {
      // Opened with an opener reference on purpose: the callback page detects
      // `window.opener` and closes itself once the flow completes.
      window.open(result.authorizationUri, '_blank');
    },
  });

  const removeRule = useMutation({
    mutationFn: (id: string) =>
      unwrap(client.DELETE('/api/approvals/rules/{ruleId}', { params: { path: { ruleId: id } } })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['approval-rules'] }),
  });

  const createRule = useMutation({
    mutationFn: () =>
      unwrap(
        client.POST('/api/approvals/rules', {
          body: {
            toolName: ruleForm.toolName,
            agentName: ruleForm.agentName.length > 0 ? ruleForm.agentName : null,
            argumentConditions: ruleForm.conditions.map((row) => ({
              path: row.path,
              operator: row.operator,
              value: buildConditionValue(row.operator, row.value),
            })),
          },
        }),
      ),
    onSuccess: () => {
      setRuleForm(EMPTY_RULE_FORM);
      setShowRuleForm(false);
      void queryClient.invalidateQueries({ queryKey: ['approval-rules'] });
    },
  });

  const serverNeedle = serverQuery.trim().toLowerCase();
  const serverFiltering = serverNeedle.length > 0;
  const filteredServers = (servers.data ?? []).filter(
    (server) =>
      !serverFiltering ||
      server.name.toLowerCase().includes(serverNeedle) ||
      server.endpoint.toLowerCase().includes(serverNeedle) ||
      (server.description?.toLowerCase().includes(serverNeedle) ?? false),
  );

  const ruleNeedle = ruleQuery.trim().toLowerCase();
  const ruleFiltering = ruleNeedle.length > 0;
  const filteredRules = (rules.data ?? []).filter(
    (rule) =>
      !ruleFiltering ||
      rule.toolName.toLowerCase().includes(ruleNeedle) ||
      (rule.agentName?.toLowerCase().includes(ruleNeedle) ?? false),
  );

  return (
    <>
      <PageHeader
        title={t('mcp.title')}
        description={t('mcp.description')}
        actions={
          meta.roles.canAdminister && (
            <>
              <Tooltip text={t('mcp.refreshTitle')}>
                <Button onClick={() => refresh.mutate()} busy={refresh.isPending}>
                  {t('mcp.refreshTools')}
                </Button>
              </Tooltip>
              <Button tone="primary" onClick={() => setShowForm((current) => !current)}>
                <PlusIcon className="size-3.5" />
                {t('mcp.addServer')}
              </Button>
            </>
          )
        }
      />

      <Panel className="mb-4">
        <div className="border-b border-line px-4 py-2.5 text-sm text-muted">
          <strong className="text-fg">{t('mcp.boundary.title')}</strong> {t('mcp.boundary.before')}{' '}
          <Mono>http</Mono> / <Mono>https</Mono>. {t('mcp.boundary.after')}
        </div>
      </Panel>

      {/* Four mutations whose failure used to be swallowed. A refresh that the
          server refused, an OAuth start that never opened, a delete that did
          not take — each leaves the screen looking exactly as it did. */}
      {(refresh.isError || authorize.isError || remove.isError || removeRule.isError) && (
        <div className="mb-4 flex flex-col gap-2">
          {refresh.isError && (
            <ErrorNote error={refresh.error} onRetry={() => refresh.mutate()} />
          )}
          {authorize.isError && (
            <ErrorNote
              error={authorize.error}
              onRetry={
                authorize.variables === undefined
                  ? undefined
                  : () => authorize.mutate(authorize.variables)
              }
            />
          )}
          {remove.isError && (
            <ErrorNote
              error={remove.error}
              onRetry={
                remove.variables === undefined ? undefined : () => remove.mutate(remove.variables)
              }
            />
          )}
          {removeRule.isError && (
            <ErrorNote
              error={removeRule.error}
              onRetry={
                removeRule.variables === undefined
                  ? undefined
                  : () => removeRule.mutate(removeRule.variables)
              }
            />
          )}
        </div>
      )}

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
                value={form.transport ?? 'StreamableHttp'}
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
                placeholder="Tracon:McpSecrets:GithubToken"
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
              <label className="flex items-center gap-2 text-base">
                <input
                  type="checkbox"
                  checked={form.enabled}
                  onChange={(event) => setForm({ ...form, enabled: event.target.checked })}
                />
                {t('common.enabled')}
              </label>

              <label className="flex items-center gap-2 text-base">
                <input
                  type="checkbox"
                  checked={form.requiresApproval}
                  onChange={(event) => setForm({ ...form, requiresApproval: event.target.checked })}
                />
                {t('mcp.requireApproval')}
              </label>
            </div>

            <div className="sm:col-span-2 border-t border-line pt-3">
              <label className="flex items-center gap-2 text-base font-medium">
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
              <p className="mt-1 text-xs text-muted">
                {t('mcp.oauthNotice')} <Mono>Tracon:Mcp:OAuthCallbackBaseUri</Mono>.
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
                    placeholder="Tracon:McpSecrets:GithubClientSecret"
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
              {save.isError && <ErrorNote error={save.error} onRetry={() => save.mutate()} />}
            </div>
          </form>
        </Panel>
      )}

      <Toolbar
        search={{ value: serverQuery, onChange: setServerQuery, label: t('mcp.searchServers') }}
        onReset={serverQuery.trim().length > 0 ? () => setServerQuery('') : undefined}
      />

      <Panel title={t('mcp.servers')} className="mb-4">
        {servers.isPending && <Loading rows={4} />}
        {servers.isError && (
          <div className="p-4">
            <ErrorNote error={servers.error} onRetry={() => void servers.refetch()} />
          </div>
        )}

        {servers.isSuccess &&
          (filteredServers.length === 0 ? (
            <Empty
              title={serverFiltering ? t('common.noResults') : t('mcp.noServers.title')}
              action={
                serverFiltering ? (
                  <Button onClick={() => setServerQuery('')}>{t('toolbar.reset')}</Button>
                ) : (
                  meta.roles.canAdminister && (
                    <Button tone="primary" onClick={() => setShowForm(true)}>
                      {t('mcp.empty.action')}
                    </Button>
                  )
                )
              }
            >
              {serverFiltering ? t('mcp.noServers.filtered') : t('mcp.noServers.body')}
            </Empty>
          ) : (
            <Table label={t('mcp.servers')}>
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
                {filteredServers.map((server) => (
                  <Fragment key={server.id}>
                    <tr className="focus-within:bg-raised hover:bg-raised">
                      <Td>
                        <Mono className="font-semibold">{server.name}</Mono>
                        {server.description != null && server.description.length > 0 && (
                          <p className="text-xs text-subtle">{server.description}</p>
                        )}
                      </Td>
                      <Td className="max-w-xs truncate">
                        <Mono className="text-xs">{server.endpoint}</Mono>
                      </Td>
                      <Td>
                        {server.oauthEnabled ? (
                          <Mono className="text-xs text-muted">
                            OAuth: {server.oauthClientId}
                          </Mono>
                        ) : credentialSummary(server) != null ? (
                          <Mono className="text-xs text-muted">{credentialSummary(server)}</Mono>
                        ) : (
                          <span className="text-xs text-subtle">{t('common.none')}</span>
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
                            aria-expanded={expanded === server.name}
                            aria-controls={`mcp-detail-${server.id}`}
                          >
                            {expanded === server.name ? t('audit.hide') : t('mcp.promptsAndResources')}
                          </Button>
                          {server.oauthEnabled && meta.roles.canAdminister && (
                            <Tooltip text={t('mcp.authorizeTitle')}>
                              <Button
                                busy={authorize.isPending && authorize.variables === server.name}
                                onClick={() => authorize.mutate(server.name)}
                              >
                                {t('mcp.authorize')}
                              </Button>
                            </Tooltip>
                          )}
                          {meta.roles.canAdminister && (
                            /* No confirmation step: §175.3 fails on both
                                counts. The row carries an endpoint and
                                configuration KEY NAMES (K-059), typed back
                                from this form or the API; its header maps are
                                set through the API and sent again from there. */
                            <Tooltip text={t('mcp.removeServerTitle')}>
                              <Button
                                tone="danger"
                                ariaLabel={t('mcp.removeServerTitle')}
                                busy={remove.isPending && remove.variables === server.name}
                                onClick={() => remove.mutate(server.name)}
                              >
                                <TrashIcon className="size-3.5" />
                              </Button>
                            </Tooltip>
                          )}
                        </div>
                      </Td>
                    </tr>
                    {expanded === server.name && (
                      <tr>
                        <td id={`mcp-detail-${server.id}`} colSpan={5} className="p-0">
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

      <Panel
        title={t('mcp.rememberedApprovals')}
        className="mb-4"
        actions={
          meta.roles.canAdminister && (
            <Button tone="primary" onClick={() => setShowRuleForm((current) => !current)}>
              <PlusIcon className="size-3.5" />
              {t('mcp.addRule')}
            </Button>
          )
        }
      >
        {showRuleForm && (
          <form
            className="grid gap-3 border-b border-line p-4 sm:grid-cols-2"
            onSubmit={(event) => {
              event.preventDefault();
              createRule.mutate();
            }}
          >
            <Field label={t('mcp.tool')} required>
              <TextInput
                value={ruleForm.toolName}
                required
                placeholder="refund_order"
                onChange={(event) => setRuleForm({ ...ruleForm, toolName: event.target.value })}
              />
            </Field>

            <Field label={t('common.agent')} hint={t('mcp.ruleAgentHint')}>
              <TextInput
                value={ruleForm.agentName}
                placeholder="support"
                onChange={(event) => setRuleForm({ ...ruleForm, agentName: event.target.value })}
              />
            </Field>

            <div className="sm:col-span-2">
              <Field label={t('mcp.conditions')} hint={t('mcp.conditionsHint')}>
                <div className="flex flex-col gap-2">
                  {ruleForm.conditions.length === 0 && (
                    <p className="text-sm text-subtle">{t('mcp.noConditions')}</p>
                  )}

                  {ruleForm.conditions.map((row, index) => (
                    <div key={index} className="flex items-center gap-2">
                      <TextInput
                        value={row.path}
                        required
                        placeholder="amount"
                        data-testid={`condition-path-${index}`}
                        onChange={(event) =>
                          setRuleForm({
                            ...ruleForm,
                            conditions: ruleForm.conditions.map((item, itemIndex) =>
                              itemIndex === index ? { ...item, path: event.target.value } : item,
                            ),
                          })
                        }
                      />
                      <Select
                        value={row.operator}
                        testId={`condition-operator-${index}`}
                        onChange={(value) =>
                          setRuleForm({
                            ...ruleForm,
                            conditions: ruleForm.conditions.map((item, itemIndex) =>
                              itemIndex === index
                                ? { ...item, operator: value as ToolArgumentOperator }
                                : item,
                            ),
                          })
                        }
                      >
                        {(Object.keys(OPERATOR_SYMBOLS) as ToolArgumentOperator[]).map((operator) => (
                          <option key={operator} value={operator}>
                            {t(`mcp.operator.${operator}`)} ({OPERATOR_SYMBOLS[operator]})
                          </option>
                        ))}
                      </Select>
                      <TextInput
                        value={row.value}
                        required
                        placeholder={row.operator === 'In' || row.operator === 'NotIn' ? 'eu, us' : '100'}
                        data-testid={`condition-value-${index}`}
                        onChange={(event) =>
                          setRuleForm({
                            ...ruleForm,
                            conditions: ruleForm.conditions.map((item, itemIndex) =>
                              itemIndex === index ? { ...item, value: event.target.value } : item,
                            ),
                          })
                        }
                      />
                      <Button
                        type="button"
                        tone="ghost"
                        testId={`remove-condition-${index}`}
                        onClick={() =>
                          setRuleForm({
                            ...ruleForm,
                            conditions: ruleForm.conditions.filter((_, itemIndex) => itemIndex !== index),
                          })
                        }
                      >
                        <TrashIcon className="size-3.5" />
                      </Button>
                    </div>
                  ))}

                  <Button
                    type="button"
                    testId="add-condition"
                    onClick={() =>
                      setRuleForm({ ...ruleForm, conditions: [...ruleForm.conditions, EMPTY_CONDITION_ROW] })
                    }
                  >
                    {t('mcp.addCondition')}
                  </Button>
                </div>
              </Field>
            </div>

            <div className="sm:col-span-2 flex items-center gap-2">
              <Button type="submit" tone="primary" busy={createRule.isPending}>
                {t('common.save')}
              </Button>
              <Button tone="ghost" onClick={() => setShowRuleForm(false)}>
                {t('common.cancel')}
              </Button>
              {createRule.isError && (
                <ErrorNote error={createRule.error} onRetry={() => createRule.mutate()} />
              )}
            </div>
          </form>
        )}

        {!meta.roles.canAdminister && <Unauthorized requires="administrator" />}
        {meta.roles.canAdminister && rules.isPending && <Loading rows={4} />}
        {meta.roles.canAdminister && rules.isError && (
          <div className="p-4">
            <ErrorNote error={rules.error} onRetry={() => void rules.refetch()} />
          </div>
        )}

        {rules.isSuccess &&
          (filteredRules.length === 0 ? (
            /*
              🚨 No fabricated "create the first rule" push. No standing rule is
              the SAFE state: every matching tool call then waits for a human.
              A rule is a standing pre-approval, so an empty state that urges
              one would be selling away the approval gate.
            */
            <Empty
              title={ruleFiltering ? t('common.noResults') : t('mcp.noRules.title')}
              action={
                ruleFiltering ? (
                  <Button onClick={() => setRuleQuery('')}>{t('toolbar.reset')}</Button>
                ) : undefined
              }
            >
              {ruleFiltering ? t('mcp.noRules.filtered') : t('mcp.noRules.body')}
            </Empty>
          ) : (
            <Table label={t('mcp.rules')}>
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
                {filteredRules.map((rule) => (
                  <tr key={rule.id} className="focus-within:bg-raised hover:bg-raised">
                    <Td>
                      <Mono className="font-semibold">{rule.toolName}</Mono>
                    </Td>
                    <Td>{rule.agentName ?? <span className="text-subtle">{t('runs.allAgents')}</span>}</Td>
                    <Td>
                      {rule.argumentConditions.length > 0 ? (
                        <div className="flex flex-wrap gap-1">
                          {rule.argumentConditions.map((condition, index) => (
                            <Badge key={index} description={t('mcp.conditionedRuleTitle')}>
                              <Mono className="text-xs">
                                {condition.path} {OPERATOR_SYMBOLS[condition.operator]}{' '}
                                {Array.isArray(condition.value)
                                  ? condition.value.join(', ')
                                  : String(condition.value)}
                              </Mono>
                            </Badge>
                          ))}
                        </div>
                      ) : rule.argumentsHash != null && rule.argumentsHash.length > 0 ? (
                        <Badge description={t('mcp.sameArgumentsTitle')}>{t('mcp.sameArguments')}</Badge>
                      ) : (
                        <Badge tone="warn" description={t('mcp.anyArgumentsTitle')}>
                          {t('mcp.anyArguments')}
                        </Badge>
                      )}
                    </Td>
                    <Td className="text-xs text-muted" title={absoluteTime(rule.createdAt)}>
                      {relativeTime(rule.createdAt)}
                    </Td>
                    <Td className="text-right">
                      {meta.roles.canAdminister && (
                        <Tooltip text={t('mcp.removeRuleEffect')}>
                          <Button
                            tone="danger"
                            ariaLabel={t('mcp.removeRule')}
                            busy={removeRule.isPending && removeRule.variables === rule.id}
                            onClick={() => removeRule.mutate(rule.id)}
                          >
                            <TrashIcon className="size-3.5" />
                          </Button>
                        </Tooltip>
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
