import { useEffect, useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { client, unwrap } from '../../lib/api';
import { useT } from '../../lib/i18n';
import { useNavigate } from '../../lib/router';
import {
  Button,
  ErrorNote,
  Field,
  Loading,
  Mono,
  PageHeader,
  Panel,
  TextArea,
  TextInput,
} from '../../components/ui';
import { Tooltip } from '../../components/tooltip';
import { ConfirmDialog } from '../../components/confirm-dialog';
import { emptyRequest, emptyResource, emptyScript, type SkillForm } from './model';
import { SKILL_PIN_QUERY_KEY } from './script-grants';
import type { AgentSkillScriptDefinition } from '@tracon/client';
import type { AgentSkillDefinition, AgentSkillResourceDefinition } from '../../lib/server-types';

/**
 * Create and edit a stored skill.
 *
 * Split out of the skills list in phase 165: the list is the list pattern and
 * this is the form pattern, and one file carrying both meant neither read like
 * its canonical version (`runs.tsx`, `agent-editor/`).
 */
export function SkillEditorScreen({ name }: { name?: string }): ReactNode {
  const t = useT();
  const editing = name !== undefined && name.length > 0;
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [form, setForm] = useState<SkillForm>(emptyRequest);
  const existing = useQuery({
    queryKey: ['skill', name],
    queryFn: () =>
      unwrap(
        client.GET('/api/skills/{name}', { params: { path: { name: name as string } } }),
      ) as Promise<AgentSkillDefinition>,
    enabled: editing,
  });

  useEffect(() => {
    if (!existing.isSuccess) return;
    const skill = existing.data;
    setForm({
      name: skill.name,
      description: skill.description,
      instructions: skill.instructions,
      compatibility: skill.compatibility ?? null,
      license: skill.license ?? null,
      allowedTools: skill.allowedTools ?? null,
      metadata: skill.metadata,
      enabled: skill.enabled,
      resources: skill.resources,
      scripts: skill.scripts ?? [],
    });
  }, [existing.isSuccess, existing.data]);

  const save = useMutation({
    mutationFn: () =>
      unwrap(
        client.PUT('/api/skills/{name}', { params: { path: { name: form.name } }, body: form }),
      ) as Promise<AgentSkillDefinition>,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['skills'] });
      // The grants panel compares each grant with the content it reads per skill.
      await queryClient.invalidateQueries({ queryKey: [SKILL_PIN_QUERY_KEY] });
      navigate('skills');
    },
  });
  const remove = useMutation({
    mutationFn: () =>
      unwrap(client.DELETE('/api/skills/{name}', { params: { path: { name: form.name } } })),
    onSuccess: async () => {
      setConfirmingDelete(false);
      await queryClient.invalidateQueries({ queryKey: ['skills'] });
      // The grants panel compares each grant with the content it reads per skill.
      await queryClient.invalidateQueries({ queryKey: [SKILL_PIN_QUERY_KEY] });
      navigate('skills');
    },
  });

  /*
    🚨 §175.3 criterion (a): `skill_scripts` and the agent bindings both cascade
    off `agent_skills`. The script bodies live nowhere else in this console, so
    re-creating the skill by name gives back an empty shell.
  */
  const [confirmingDelete, setConfirmingDelete] = useState(false);

  const title = editing ? t('skills.editTitle', { name: name ?? '' }) : t('skills.newTitle');

  if (editing && existing.isPending) {
    return (
      <>
        <PageHeader title={title} description={t('skills.editorDescription')} />
        <Panel>
          <Loading rows={8} />
        </Panel>
      </>
    );
  }

  // 🚨 A separate path from the loading one. `isPending` goes false the moment
  // the request fails, and nothing below renders anything for that case — the
  // screen used to show an empty form as if the skill had loaded blank, and
  // saving it would have overwritten the stored one with nothing.
  if (editing && existing.isError) {
    return (
      <>
        <PageHeader title={title} description={t('skills.editorDescription')} />
        <Panel>
          <div className="p-4">
            <ErrorNote error={existing.error} onRetry={() => void existing.refetch()} />
          </div>
        </Panel>
      </>
    );
  }

  const valid = form.name.trim().length > 0 && form.description.trim().length > 0;
  /*
    🚨 The read resolves the name the way the runtime does, so a name registered
    in code returns the CODE skill. Saving that form would write the code content
    over the stored row of the same name — the server now refuses the save (409),
    and the form says why before anyone tries. The only action left is deleting
    the stored copy, which never runs.
  */
  const fromCode = editing && existing.data?.origin === 'Code';

  return (
    <>
      <PageHeader
        title={title}
        description={t('skills.editorDescription')}
        actions={
          <>
            <Button onClick={() => navigate('skills')}>{t('common.cancel')}</Button>
            {editing && (
              <Tooltip text={fromCode ? t('skills.deleteStoredCopyEffect') : t('skills.deleteEffect')}>
                <Button
                  tone="danger"
                  busy={remove.isPending}
                  testId="skill-delete"
                  onClick={() => setConfirmingDelete(true)}
                >
                  {fromCode ? t('skills.deleteStoredCopy') : t('common.delete')}
                </Button>
              </Tooltip>
            )}
            {!fromCode && (
              <Button
                tone="primary"
                busy={save.isPending}
                disabled={!valid}
                onClick={() => save.mutate()}
              >
                {t('common.save')}
              </Button>
            )}
          </>
        }
      />

      <ConfirmDialog
        open={confirmingDelete}
        onClose={() => setConfirmingDelete(false)}
        onConfirm={() => remove.mutate()}
        title={
          fromCode
            ? t('skills.deleteStoredCopyTitle', { name: form.name })
            : t('skills.deleteTitle', { name: form.name })
        }
        consequence={fromCode ? t('skills.deleteStoredCopyEffect') : t('skills.deleteEffect')}
        confirmLabel={fromCode ? t('skills.deleteStoredCopy') : t('common.delete')}
        busy={remove.isPending}
        error={remove.error}
        testId="confirm-delete-skill"
      />

      {save.isError && (
        <div className="mb-4">
          <ErrorNote error={save.error} onRetry={() => save.mutate()} />
        </div>
      )}
      {remove.isError && (
        <div className="mb-4">
          {/* Reopens the confirmation rather than firing the delete — see sessions.tsx. */}
          <ErrorNote error={remove.error} onRetry={() => setConfirmingDelete(true)} />
        </div>
      )}

      {fromCode && (
        <div
          className="mb-4 rounded-md border border-line bg-info-soft px-3 py-2 text-sm text-info"
          data-testid="skill-code-notice"
        >
          {t('skills.codeNotice')}
        </div>
      )}

      {/* A disabled fieldset disables every control inside it, the add and
          remove buttons included, without threading a flag through each one. */}
      <fieldset disabled={fromCode} className="flex min-w-0 flex-col gap-4">
        <Panel title={t('skills.frontmatter')}>
          <div className="grid gap-4 p-4 sm:grid-cols-2">
            <Field label={t('common.name')} required>
              <TextInput
                value={form.name}
                readOnly={editing}
                placeholder="invoice-analysis"
                onChange={(event) => setForm({ ...form, name: event.target.value })}
              />
            </Field>
            <Field label={t('common.description')} required>
              <TextInput
                value={form.description}
                onChange={(event) => setForm({ ...form, description: event.target.value })}
              />
            </Field>
            <Field label={t('skills.compatibility')}>
              <TextInput
                value={form.compatibility ?? ''}
                onChange={(event) =>
                  setForm({ ...form, compatibility: event.target.value || null })
                }
              />
            </Field>
            <Field label={t('skills.license')}>
              <TextInput
                value={form.license ?? ''}
                onChange={(event) => setForm({ ...form, license: event.target.value || null })}
              />
            </Field>
            <div className="sm:col-span-2">
              <Field label={t('skills.allowedTools')}>
                <TextInput
                  value={form.allowedTools ?? ''}
                  onChange={(event) =>
                    setForm({ ...form, allowedTools: event.target.value || null })
                  }
                />
              </Field>
            </div>
            <label className="flex cursor-pointer items-center gap-2 text-base">
              <input
                type="checkbox"
                className="accent-[var(--tracon-accent)]"
                checked={form.enabled}
                onChange={(event) => setForm({ ...form, enabled: event.target.checked })}
              />
              {t('common.enabled')}
            </label>
          </div>
        </Panel>

        <Panel title={t('agentDetail.instructions')}>
          <div className="p-4">
            <TextArea
              rows={14}
              value={form.instructions}
              aria-label={t('agentDetail.instructions')}
              placeholder={t('skills.instructionsPlaceholder')}
              onChange={(event) => setForm({ ...form, instructions: event.target.value })}
            />
          </div>
        </Panel>

        <Panel title={t('skills.resources')}>
          <div className="flex flex-col gap-3 p-4">
            {form.resources.map((resource, index) => (
              <ResourceEditor
                key={`${resource.name}-${index}`}
                resource={resource}
                onChange={(value) =>
                  setForm({
                    ...form,
                    resources: form.resources.map((item, itemIndex) =>
                      itemIndex === index ? value : item,
                    ),
                  })
                }
                onRemove={() =>
                  setForm({
                    ...form,
                    resources: form.resources.filter((_, itemIndex) => itemIndex !== index),
                  })
                }
              />
            ))}
            <Button
              onClick={() => setForm({ ...form, resources: [...form.resources, emptyResource()] })}
            >
              {t('skills.addResource')}
            </Button>
          </div>
        </Panel>

        <Panel title={t('skills.scripts')}>
          <div className="flex flex-col gap-3 p-4">
            {/* 🚨 Danger tone from the token set, not `red-500`: a warning painted
                in a colour the rest of the console never uses reads as a stray
                element rather than as this product's loudest state. */}
            <div
              data-testid="script-execution-warning"
              className="rounded border border-danger bg-danger-soft px-3 py-2 text-base font-medium text-danger"
            >
              {t('skills.scriptWarning')}
            </div>
            <p className="text-sm text-muted">{t('skills.scriptNotice')}</p>
            {form.scripts.map((script, index) => (
              <ScriptEditor
                key={`${script.name}-${index}`}
                script={script}
                onChange={(value) =>
                  setForm({
                    ...form,
                    scripts: form.scripts.map((item, itemIndex) =>
                      itemIndex === index ? value : item,
                    ),
                  })
                }
                onRemove={() =>
                  setForm({
                    ...form,
                    scripts: form.scripts.filter((_, itemIndex) => itemIndex !== index),
                  })
                }
              />
            ))}
            <Button
              onClick={() => setForm({ ...form, scripts: [...form.scripts, emptyScript()] })}
            >
              {t('skills.addScript')}
            </Button>
          </div>
        </Panel>
      </fieldset>
    </>
  );
}

function ScriptEditor({
  script,
  onChange,
  onRemove,
}: {
  script: AgentSkillScriptDefinition;
  onChange: (value: AgentSkillScriptDefinition) => void;
  onRemove: () => void;
}): ReactNode {
  const t = useT();

  return (
    <div className="grid gap-3 border-t border-line pt-3 sm:grid-cols-2">
      <Field label={t('common.name')}>
        <TextInput
          value={script.name}
          placeholder="total"
          onChange={(event) => onChange({ ...script, name: event.target.value })}
        />
      </Field>
      <Field label={t('common.description')}>
        <TextInput
          value={script.description ?? ''}
          onChange={(event) => onChange({ ...script, description: event.target.value || null })}
        />
      </Field>
      <Field label={t('skills.extension')}>
        <TextInput
          value={script.extension}
          placeholder="py"
          onChange={(event) => onChange({ ...script, extension: event.target.value })}
        />
      </Field>
      <Field label={t('skills.parametersSchema')}>
        <TextInput
          value={script.parametersSchema ?? ''}
          onChange={(event) =>
            onChange({ ...script, parametersSchema: event.target.value || null })
          }
        />
      </Field>
      <div className="sm:col-span-2">
        <Field label={t('skills.content')}>
          <TextArea
            rows={8}
            value={script.content}
            onChange={(event) => onChange({ ...script, content: event.target.value })}
          />
        </Field>
      </div>
      <div className="flex items-center gap-3">
        <Button tone="danger" onClick={onRemove}>
          {t('skills.removeScript')}
        </Button>
        <Mono className="text-subtle">.{script.extension}</Mono>
      </div>
    </div>
  );
}

function ResourceEditor({
  resource,
  onChange,
  onRemove,
}: {
  resource: AgentSkillResourceDefinition;
  onChange: (value: AgentSkillResourceDefinition) => void;
  onRemove: () => void;
}): ReactNode {
  const t = useT();

  return (
    <div className="grid gap-3 border-t border-line pt-3 sm:grid-cols-2">
      <Field label={t('common.name')}>
        <TextInput
          value={resource.name}
          placeholder="policy.md"
          onChange={(event) => onChange({ ...resource, name: event.target.value })}
        />
      </Field>
      <Field label={t('common.description')}>
        <TextInput
          value={resource.description ?? ''}
          onChange={(event) => onChange({ ...resource, description: event.target.value || null })}
        />
      </Field>
      <Field label={t('skills.mediaType')}>
        <TextInput
          value={resource.mediaType}
          placeholder="text/plain"
          onChange={(event) => onChange({ ...resource, mediaType: event.target.value })}
        />
      </Field>
      <div className="sm:col-span-2">
        <Field label={t('skills.content')}>
          <TextArea
            rows={5}
            value={resource.content}
            onChange={(event) => onChange({ ...resource, content: event.target.value })}
          />
        </Field>
      </div>
      <div className="flex items-center gap-3">
        <Button tone="danger" onClick={onRemove}>
          {t('skills.removeResource')}
        </Button>
        <Mono className="text-subtle">{resource.mediaType}</Mono>
      </div>
    </div>
  );
}
