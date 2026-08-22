import { Fragment, useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
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
import type {
  AgentPrismMetaResponse as Meta,
  McpServerRequest,
  McpTransportMode,
  ToolArgumentCondition,
  ToolArgumentOperator,
} from '@agentprism/client';
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
        // (docs/63-ARGUMAN-DUZEYINDE-ONAY-POLITIKASI.md, 63.2) — a "true"/"false"
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

  const servers = useQuery({
    queryKey: ['mcp-servers'],
    queryFn: () => unwrap(client.GET('/api/mcp-servers')) as Promise<McpServerDefinition[]>,
  });
  const rules = useQuery({
    queryKey: ['approval-rules'],
    queryFn: () => unwrap(client.GET('/api/approvals/rules')) as Promise<ToolApprovalRule[]>,
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
                placeholder="AgentPrism:McpSecrets:GithubToken"
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
                    placeholder="AgentPrism:McpSecrets:GithubClientSecret"
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
                    <p className="text-[12px] text-subtle">{t('mcp.noConditions')}</p>
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
              {createRule.isError && <ErrorNote error={createRule.error} />}
            </div>
          </form>
        )}

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
                      {rule.argumentConditions.length > 0 ? (
                        <div className="flex flex-wrap gap-1">
                          {rule.argumentConditions.map((condition, index) => (
                            <Badge key={index} title={t('mcp.conditionedRuleTitle')}>
                              <Mono className="text-[11px]">
                                {condition.path} {OPERATOR_SYMBOLS[condition.operator]}{' '}
                                {Array.isArray(condition.value)
                                  ? condition.value.join(', ')
                                  : String(condition.value)}
                              </Mono>
                            </Badge>
                          ))}
                        </div>
                      ) : rule.argumentsHash != null && rule.argumentsHash.length > 0 ? (
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
