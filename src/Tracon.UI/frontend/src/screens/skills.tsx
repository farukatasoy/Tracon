import { useEffect, useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { relativeTime } from '../lib/format';
import { useT } from '../lib/i18n';
import { Link, useNavigate } from '../lib/router';
import type {
  TraconMetaResponse as Meta,
  AgentSkillScriptDefinition,
  SkillScriptGrant,
} from '@tracon/client';
import type { AgentSkillDefinition, AgentSkillResourceDefinition } from '../lib/server-types';
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
  Table,
  Td,
  TextArea,
  TextInput,
  Th,
} from '../components/ui';
import { PlusIcon } from '../components/icons';

// The generated request type makes every field but name/description/
// instructions optional (omission means "use the server default"), but this
// form always sends a fully-populated body — a local shape keeps the JSX's
// direct field reads (`form.resources.map(...)`, ...) free of `?? []` noise.
interface SkillForm {
  name: string;
  description: string;
  instructions: string;
  compatibility: string | null;
  license: string | null;
  allowedTools: string | null;
  metadata?: Record<string, never>;
  enabled: boolean;
  resources: AgentSkillResourceDefinition[];
  scripts: AgentSkillScriptDefinition[];
}

const emptyResource = (): AgentSkillResourceDefinition => ({
  name: '',
  description: '',
  mediaType: 'text/plain',
  content: '',
});

const emptyScript = (): AgentSkillScriptDefinition => ({
  name: '',
  description: '',
  extension: 'py',
  content: '',
  parametersSchema: null,
});

const emptyRequest = (): SkillForm => ({
  name: '',
  description: '',
  instructions: '',
  compatibility: null,
  license: null,
  allowedTools: null,
  enabled: true,
  resources: [],
  scripts: [],
});

export function SkillsScreen({ meta }: { meta: Meta }): ReactNode {
  const t = useT();
  const skills = useQuery({
    queryKey: ['skills'],
    queryFn: () => unwrap(client.GET('/api/skills')) as Promise<AgentSkillDefinition[]>,
  });

  return (
    <>
      <PageHeader
        title={t('nav.skills')}
        description={t('skills.description')}
        actions={
          meta.roles.canAdminister && (
            <Link to="skills/new">
              <Button tone="primary">
                <PlusIcon className="size-3.5" />
                {t('skills.new')}
              </Button>
            </Link>
          )
        }
      />
      <Panel>
        {skills.isPending && <Loading />}
        {skills.isError && <div className="p-4"><ErrorNote error={skills.error} /></div>}
        {skills.isSuccess && skills.data.length === 0 && (
          <Empty title={t('skills.empty.title')}>{t('skills.empty.body')}</Empty>
        )}
        {skills.isSuccess && skills.data.length > 0 && (
          <Table>
            <thead>
              <tr>
                <Th>{t('common.name')}</Th>
                <Th>{t('skills.resources')}</Th>
                <Th>{t('common.status')}</Th>
                <Th>{t('common.updated')}</Th>
                <Th />
              </tr>
            </thead>
            <tbody>
              {skills.data.map((skill) => (
                <tr key={skill.name} className="hover:bg-raised">
                  <Td><span className="font-medium">{skill.name}</span><span className="block text-[12px] text-muted">{skill.description}</span></Td>
                  <Td>{skill.resources.length}</Td>
                  <Td>
                    {skill.enabled ? (
                      <Badge tone="success">{t('common.enabled')}</Badge>
                    ) : (
                      <Badge tone="warn">{t('common.disabled')}</Badge>
                    )}
                  </Td>
                  <Td className="text-muted">{relativeTime(skill.updatedAt)}</Td>
                  <Td className="text-right"><Link to={`skills/${encodeURIComponent(skill.name)}/edit`}><Button tone="ghost">{t('common.edit')}</Button></Link></Td>
                </tr>
              ))}
            </tbody>
          </Table>
        )}
      </Panel>
      <div className="mt-4"><ScriptGrantsPanel meta={meta} /></div>
    </>
  );
}

/**
 * Grants let a tenant run skill scripts. Granting is a separate, admin-only act:
 * storing a script never implies permission to execute it.
 */
function ScriptGrantsPanel({ meta }: { meta: Meta }): ReactNode {
  const t = useT();
  const queryClient = useQueryClient();
  const grants = useQuery({
    queryKey: ['skill-script-grants'],
    queryFn: () =>
      unwrap(client.GET('/api/skill-script-grants')) as Promise<SkillScriptGrant[]>,
  });
  const [skillName, setSkillName] = useState('');
  const [scriptName, setScriptName] = useState('');

  const invalidate = async (): Promise<void> => {
    await queryClient.invalidateQueries({ queryKey: ['skill-script-grants'] });
  };

  const grant = useMutation({
    mutationFn: () =>
      unwrap(
        client.POST('/api/skill-script-grants', {
          body: { skillName, scriptName: scriptName || null },
        }),
      ) as Promise<SkillScriptGrant>,
    onSuccess: async () => {
      setSkillName('');
      setScriptName('');
      await invalidate();
    },
  });
  const revoke = useMutation({
    mutationFn: (target: { skillName: string; scriptName?: string | null }) =>
      unwrap(
        client.DELETE('/api/skill-script-grants/{skillName}', {
          params: {
            path: { skillName: target.skillName },
            query: { scriptName: target.scriptName ?? undefined },
          },
        }),
      ),
    onSuccess: invalidate,
  });

  const active = grants.isSuccess ? grants.data.filter((item) => item.revokedAt === null) : [];

  return (
    <Panel title={t('skills.grants.title')}>
      <div className="flex flex-col gap-3 p-4">
        <div className="rounded border border-red-500 bg-red-500/10 px-3 py-2 text-[13px] font-medium text-red-500">
          {t('skills.grants.warning')}
        </div>
        {grants.isError && <ErrorNote error={grants.error} />}
        {grant.isError && <ErrorNote error={grant.error} />}
        {grants.isSuccess && active.length === 0 && <p className="text-[13px] text-muted">{t('skills.grants.empty')}</p>}
        {active.length > 0 && (
          <Table>
            <thead>
              <tr>
                <Th>{t('skills.grants.skill')}</Th>
                <Th>{t('skills.grants.script')}</Th>
                <Th>{t('skills.grants.by')}</Th>
                <Th>{t('skills.grants.at')}</Th>
                <Th />
              </tr>
            </thead>
            <tbody>
              {active.map((item) => (
                <tr key={item.id} className="hover:bg-raised">
                  <Td>{item.skillName}</Td>
                  <Td><Mono>{item.scriptName ?? '*'}</Mono></Td>
                  <Td className="text-muted">{item.grantedBy ?? t('skills.grants.unknownBy')}</Td>
                  <Td className="text-muted">{relativeTime(item.grantedAt)}</Td>
                  <Td className="text-right">
                    {meta.roles.canAdminister && (
                      <Button tone="danger" busy={revoke.isPending} onClick={() => revoke.mutate({ skillName: item.skillName, scriptName: item.scriptName })}>
                        {t('skills.grants.revoke')}
                      </Button>
                    )}
                  </Td>
                </tr>
              ))}
            </tbody>
          </Table>
        )}
        {meta.roles.canAdminister && (
          <div className="grid gap-3 border-t border-line pt-3 sm:grid-cols-3">
            <Field label={t('skills.grants.skill')}><TextInput value={skillName} placeholder="invoice-analysis" onChange={(event) => setSkillName(event.target.value)} /></Field>
            <Field label={t('skills.grants.scriptField')}><TextInput value={scriptName} placeholder="total" onChange={(event) => setScriptName(event.target.value)} /></Field>
            <div className="flex items-end"><Button tone="primary" busy={grant.isPending} disabled={skillName.trim().length === 0} onClick={() => grant.mutate()}>
                {t('skills.grants.grant')}
              </Button></div>
          </div>
        )}
      </div>
    </Panel>
  );
}

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
      navigate('skills');
    },
  });
  const remove = useMutation({
    mutationFn: () =>
      unwrap(client.DELETE('/api/skills/{name}', { params: { path: { name: form.name } } })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['skills'] });
      navigate('skills');
    },
  });

  if (editing && existing.isPending) return <Loading />;
  const valid = form.name.trim().length > 0 && form.description.trim().length > 0;

  return (
    <>
      <PageHeader
        title={editing ? t('skills.editTitle', { name: name ?? '' }) : t('skills.newTitle')}
        description={t('skills.editorDescription')}
        actions={
          <>
            <Button onClick={() => navigate('skills')}>{t('common.cancel')}</Button>
            {editing && (
              <Button tone="danger" busy={remove.isPending} onClick={() => remove.mutate()}>
                {t('common.delete')}
              </Button>
            )}
            <Button tone="primary" busy={save.isPending} disabled={!valid} onClick={() => save.mutate()}>
              {t('common.save')}
            </Button>
          </>
        }
      />
      {save.isError && <div className="mb-4"><ErrorNote error={save.error} /></div>}
      <div className="flex flex-col gap-4">
        <Panel title={t('skills.frontmatter')}><div className="grid gap-4 p-4 sm:grid-cols-2">
          <Field label={t('common.name')} required><TextInput value={form.name} readOnly={editing} placeholder="invoice-analysis" onChange={(event) => setForm({ ...form, name: event.target.value })} /></Field>
          <Field label={t('common.description')} required><TextInput value={form.description} onChange={(event) => setForm({ ...form, description: event.target.value })} /></Field>
          <Field label={t('skills.compatibility')}><TextInput value={form.compatibility ?? ''} onChange={(event) => setForm({ ...form, compatibility: event.target.value || null })} /></Field>
          <Field label={t('skills.license')}><TextInput value={form.license ?? ''} onChange={(event) => setForm({ ...form, license: event.target.value || null })} /></Field>
          <div className="sm:col-span-2"><Field label={t('skills.allowedTools')}><TextInput value={form.allowedTools ?? ''} onChange={(event) => setForm({ ...form, allowedTools: event.target.value || null })} /></Field></div>
          <label className="flex cursor-pointer items-center gap-2 text-[13px]"><input type="checkbox" className="accent-[var(--ap-accent)]" checked={form.enabled} onChange={(event) => setForm({ ...form, enabled: event.target.checked })} />
            {t('common.enabled')}
          </label>
        </div></Panel>
        <Panel title={t('agentDetail.instructions')}><div className="p-4"><TextArea rows={14} value={form.instructions} placeholder={t('skills.instructionsPlaceholder')} onChange={(event) => setForm({ ...form, instructions: event.target.value })} /></div></Panel>
        <Panel title={t('skills.resources')}><div className="flex flex-col gap-3 p-4">
          {form.resources.map((resource, index) => <ResourceEditor key={`${resource.name}-${index}`} resource={resource} onChange={(value) => setForm({ ...form, resources: form.resources.map((item, itemIndex) => itemIndex === index ? value : item) })} onRemove={() => setForm({ ...form, resources: form.resources.filter((_, itemIndex) => itemIndex !== index) })} />)}
          <Button onClick={() => setForm({ ...form, resources: [...form.resources, emptyResource()] })}>
            {t('skills.addResource')}
          </Button>
        </div></Panel>
        <Panel title={t('skills.scripts')}><div className="flex flex-col gap-3 p-4">
          <div
            data-testid="script-execution-warning"
            className="rounded border border-red-500 bg-red-500/10 px-3 py-2 text-[13px] font-medium text-red-500"
          >
            {t('skills.scriptWarning')}
          </div>
          <p className="text-[12px] text-muted">
            {t('skills.scriptNotice')}
          </p>
          {form.scripts.map((script, index) => <ScriptEditor key={`${script.name}-${index}`} script={script} onChange={(value) => setForm({ ...form, scripts: form.scripts.map((item, itemIndex) => itemIndex === index ? value : item) })} onRemove={() => setForm({ ...form, scripts: form.scripts.filter((_, itemIndex) => itemIndex !== index) })} />)}
          <Button onClick={() => setForm({ ...form, scripts: [...form.scripts, emptyScript()] })}>
            {t('skills.addScript')}
          </Button>
        </div></Panel>
      </div>
    </>
  );
}

function ScriptEditor({ script, onChange, onRemove }: { script: AgentSkillScriptDefinition; onChange: (value: AgentSkillScriptDefinition) => void; onRemove: () => void }): ReactNode {
  const t = useT();

  return <div className="grid gap-3 border-t border-line pt-3 sm:grid-cols-2">
    <Field label={t('common.name')}><TextInput value={script.name} placeholder="total" onChange={(event) => onChange({ ...script, name: event.target.value })} /></Field>
    <Field label={t('common.description')}><TextInput value={script.description ?? ''} onChange={(event) => onChange({ ...script, description: event.target.value || null })} /></Field>
    <Field label={t('skills.extension')}><TextInput value={script.extension} placeholder="py" onChange={(event) => onChange({ ...script, extension: event.target.value })} /></Field>
    <Field label={t('skills.parametersSchema')}><TextInput value={script.parametersSchema ?? ''} onChange={(event) => onChange({ ...script, parametersSchema: event.target.value || null })} /></Field>
    <div className="sm:col-span-2"><Field label={t('skills.content')}><TextArea rows={8} value={script.content} onChange={(event) => onChange({ ...script, content: event.target.value })} /></Field></div>
    <div><Button tone="danger" onClick={onRemove}>{t('skills.removeScript')}</Button><Mono className="ml-3 text-subtle">.{script.extension}</Mono></div>
  </div>;
}

function ResourceEditor({ resource, onChange, onRemove }: { resource: AgentSkillResourceDefinition; onChange: (value: AgentSkillResourceDefinition) => void; onRemove: () => void }): ReactNode {
  const t = useT();

  return <div className="grid gap-3 border-t border-line pt-3 sm:grid-cols-2">
    <Field label={t('common.name')}><TextInput value={resource.name} placeholder="policy.md" onChange={(event) => onChange({ ...resource, name: event.target.value })} /></Field>
    <Field label={t('common.description')}><TextInput value={resource.description ?? ''} onChange={(event) => onChange({ ...resource, description: event.target.value || null })} /></Field>
    <Field label={t('skills.mediaType')}><TextInput value={resource.mediaType} placeholder="text/plain" onChange={(event) => onChange({ ...resource, mediaType: event.target.value })} /></Field>
    <div className="sm:col-span-2"><Field label={t('skills.content')}><TextArea rows={5} value={resource.content} onChange={(event) => onChange({ ...resource, content: event.target.value })} /></Field></div>
    <div><Button tone="danger" onClick={onRemove}>{t('skills.removeResource')}</Button><Mono className="ml-3 text-subtle">{resource.mediaType}</Mono></div>
  </div>;
}