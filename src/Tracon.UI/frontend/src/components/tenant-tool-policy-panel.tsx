import { useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { useT } from '../lib/i18n';
import type { ToolDescriptor } from '../lib/server-types';
import { Badge, Button, ErrorNote, Loading, TextInput } from './ui';

/**
 * Per-tenant tool policy: the tools a tenant may run, as two closed lists.
 *
 * A call passes when its tool name is in the allowed list, or when its tool declares a permission that is
 * in the granted list. A tenant with no policy is unrestricted, unless the host requires one
 * (`requirePolicy` in the response), and two empty lists allow no tool: "no policy" and "an empty policy"
 * are different states and the panel keeps them apart.
 *
 * The choices are the registered tools. Tools the registry does not list (a tool of an MCP server, the
 * workspace file tools, the knowledge search) are added by name; a name only selects a tool that already
 * exists and defines nothing.
 */
export function TenantToolPolicySection({ tenantId }: { tenantId: string }): ReactNode {
  const t = useT();
  const queryClient = useQueryClient();

  const tools = useQuery({
    queryKey: ['tools'],
    queryFn: () => unwrap(client.GET('/api/tools')) as Promise<ToolDescriptor[]>,
  });
  const policy = useQuery({
    queryKey: ['tenant-tool-policy', tenantId],
    queryFn: () => unwrap(client.GET('/api/tenants/{tenantId}/tools', { params: { path: { tenantId } } })),
  });

  const [draft, setDraft] = useState<{ tools: string[]; permissions: string[] } | null>(null);
  const [newTool, setNewTool] = useState('');
  const [newPermission, setNewPermission] = useState('');

  const finish = () => {
    setDraft(null);
    setNewTool('');
    setNewPermission('');
    void queryClient.invalidateQueries({ queryKey: ['tenant-tool-policy', tenantId] });
  };

  const save = useMutation({
    mutationFn: (value: { tools: string[]; permissions: string[] }) =>
      unwrap(
        client.PUT('/api/tenants/{tenantId}/tools', {
          params: { path: { tenantId } },
          body: { allowedTools: value.tools, grantedPermissions: value.permissions },
        }),
      ),
    onSuccess: finish,
  });

  const clear = useMutation({
    mutationFn: () =>
      unwrap(client.DELETE('/api/tenants/{tenantId}/tools', { params: { path: { tenantId } } })),
    onSuccess: finish,
  });

  const saved =
    policy.data?.allowedTools != null || policy.data?.grantedPermissions != null
      ? { tools: policy.data?.allowedTools ?? [], permissions: policy.data?.grantedPermissions ?? [] }
      : null;
  const current = draft ?? saved;
  const requirePolicy = policy.data?.requirePolicy === true;

  // Saved names stay visible even when no registered tool carries them any more: hidden, they would still
  // be sent back on save and could never be removed.
  const toolChoices = mergeNames(
    (tools.data ?? []).map((tool) => tool.name),
    current?.tools ?? [],
  );
  const permissionChoices = mergeNames(
    (tools.data ?? []).flatMap((tool) => (tool.requiredPermission ? [tool.requiredPermission] : [])),
    current?.permissions ?? [],
  );

  const toggle = (kind: 'tools' | 'permissions', name: string) => {
    const base = current ?? { tools: [], permissions: [] };
    const list = base[kind];

    setDraft({
      ...base,
      [kind]: list.includes(name) ? list.filter((value) => value !== name) : [...list, name],
    });
  };

  const add = (kind: 'tools' | 'permissions', raw: string, reset: () => void) => {
    const name = raw.trim();

    if (name === '') {
      return;
    }

    const base = current ?? { tools: [], permissions: [] };

    if (!base[kind].includes(name)) {
      setDraft({ ...base, [kind]: [...base[kind], name] });
    }

    reset();
  };

  return (
    <div className="border-t border-line" data-testid="tenant-tool-policy">
      <div className="px-4 py-2.5">
        <h3 className="text-sm font-semibold text-muted">{t('toolPolicy.title')}</h3>
      </div>

      {policy.isPending && <Loading rows={3} />}
      {policy.isError && (
        <div className="px-4 pb-3">
          <ErrorNote error={policy.error} onRetry={() => void policy.refetch()} />
        </div>
      )}

      {policy.isSuccess && (
        <div className="space-y-3 px-4 pb-4">
          {current === null ? (
            <div className="flex flex-wrap items-center gap-2">
              <span data-testid="tenant-tool-policy-state">
                <Badge tone={requirePolicy ? 'danger' : 'neutral'}>
                  {requirePolicy ? t('toolPolicy.none.closed') : t('toolPolicy.none.open')}
                </Badge>
              </span>
              <Button tone="default" testId="tenant-tool-restrict" onClick={() => setDraft({ tools: [], permissions: [] })}>
                {t('toolPolicy.restrict')}
              </Button>
            </div>
          ) : (
            <>
              <ChoiceList
                label={t('toolPolicy.tools')}
                emptyLabel={t('toolPolicy.listEmpty')}
                kind="tool"
                choices={toolChoices}
                selected={current.tools}
                onToggle={(name) => toggle('tools', name)}
              />
              <NameInput
                placeholder={t('toolPolicy.addToolPlaceholder')}
                testId="tenant-tool-add-name"
                value={newTool}
                onChange={setNewTool}
                onAdd={() => add('tools', newTool, () => setNewTool(''))}
              />

              <ChoiceList
                label={t('toolPolicy.permissions')}
                emptyLabel={t('toolPolicy.listEmpty')}
                kind="permission"
                choices={permissionChoices}
                selected={current.permissions}
                onToggle={(name) => toggle('permissions', name)}
              />
              <NameInput
                placeholder={t('toolPolicy.addPermissionPlaceholder')}
                testId="tenant-permission-add-name"
                value={newPermission}
                onChange={setNewPermission}
                onAdd={() => add('permissions', newPermission, () => setNewPermission(''))}
              />

              {current.tools.length === 0 && current.permissions.length === 0 && (
                <p className="text-xs text-subtle" data-testid="tenant-tool-policy-empty">
                  {t('toolPolicy.emptyWarning')}
                </p>
              )}

              <div className="flex items-center gap-2">
                <Button
                  tone="primary"
                  disabled={save.isPending}
                  testId="tenant-tool-save"
                  onClick={() => save.mutate(current)}
                >
                  {save.isPending ? t('common.saving') : t('common.save')}
                </Button>
                <Button tone="ghost" onClick={() => setDraft(null)}>
                  {t('common.cancel')}
                </Button>
                {saved !== null && (
                  <Button tone="danger" disabled={clear.isPending} testId="tenant-tool-clear" onClick={() => clear.mutate()}>
                    {t('toolPolicy.clear')}
                  </Button>
                )}
              </div>
            </>
          )}

          {save.isError && (
            <ErrorNote
              error={save.error}
              onRetry={save.variables === undefined ? undefined : () => save.mutate(save.variables)}
            />
          )}
          {clear.isError && <ErrorNote error={clear.error} onRetry={() => clear.mutate()} />}
        </div>
      )}

      <p className="border-t border-line px-4 py-2.5 text-xs text-subtle">{t('toolPolicy.notice')}</p>
    </div>
  );
}

function ChoiceList({
  label,
  emptyLabel,
  kind,
  choices,
  selected,
  onToggle,
}: {
  label: string;
  emptyLabel: string;
  kind: 'tool' | 'permission';
  choices: string[];
  selected: string[];
  onToggle: (name: string) => void;
}): ReactNode {
  return (
    <div>
      <div className="mb-1 text-xs font-medium text-muted">{label}</div>
      {choices.length === 0 ? (
        <span className="text-xs text-subtle">{emptyLabel}</span>
      ) : (
        <div className="flex flex-wrap gap-1.5">
          {choices.map((name) => {
            const isSelected = selected.includes(name);

            return (
              <button
                key={name}
                type="button"
                data-testid={`tenant-${kind}-option-${name}`}
                aria-pressed={isSelected}
                onClick={() => onToggle(name)}
                className={
                  isSelected
                    ? 'rounded border border-transparent bg-accent-soft px-1.5 py-0.5 text-xs font-medium text-accent'
                    : 'rounded border border-line bg-raised px-1.5 py-0.5 text-xs font-medium text-muted'
                }
              >
                {name}
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}

function NameInput({
  placeholder,
  testId,
  value,
  onChange,
  onAdd,
}: {
  placeholder: string;
  testId: string;
  value: string;
  onChange: (value: string) => void;
  onAdd: () => void;
}): ReactNode {
  const t = useT();

  return (
    <div className="flex items-center gap-2">
      <TextInput
        value={value}
        placeholder={placeholder}
        data-testid={testId}
        onChange={(event) => onChange(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === 'Enter') {
            event.preventDefault();
            onAdd();
          }
        }}
      />
      <Button tone="default" disabled={value.trim() === ''} testId={`${testId}-button`} onClick={onAdd}>
        {t('toolPolicy.add')}
      </Button>
    </div>
  );
}

/** Joins name lists without duplicates, keeping the first spelling, sorted. Names are compared exactly. */
function mergeNames(...lists: readonly string[][]): string[] {
  const seen = new Set<string>();

  for (const list of lists) {
    for (const name of list) {
      if (name.trim() !== '') {
        seen.add(name);
      }
    }
  }

  return [...seen].sort((left, right) => left.localeCompare(right));
}
