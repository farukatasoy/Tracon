import { useEffect, useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
import { relativeTime } from '../lib/format';
import { Link, useNavigate } from '../lib/router';
import type { AgentSkillRequest, AgentSkillResourceDefinition, Meta } from '../lib/types';
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

const emptyResource = (): AgentSkillResourceDefinition => ({
  name: '',
  description: '',
  mediaType: 'text/plain',
  content: '',
});

const emptyRequest = (): AgentSkillRequest => ({
  name: '',
  description: '',
  instructions: '',
  compatibility: null,
  license: null,
  allowedTools: null,
  enabled: true,
  resources: [],
});

export function SkillsScreen({ meta }: { meta: Meta }): ReactNode {
  const skills = useQuery({ queryKey: ['skills'], queryFn: api.skills });

  return (
    <>
      <PageHeader
        title="Skills"
        description="Markdown instructions and read-only resources that agents load with approval at run time."
        actions={
          meta.roles.canAdminister && (
            <Link to="skills/new">
              <Button tone="primary"><PlusIcon className="size-3.5" />New skill</Button>
            </Link>
          )
        }
      />
      <Panel>
        {skills.isPending && <Loading />}
        {skills.isError && <div className="p-4"><ErrorNote error={skills.error} /></div>}
        {skills.isSuccess && skills.data.length === 0 && (
          <Empty title="No skills yet">Create a markdown skill, then attach it to an agent definition.</Empty>
        )}
        {skills.isSuccess && skills.data.length > 0 && (
          <Table>
            <thead><tr><Th>Name</Th><Th>Resources</Th><Th>Status</Th><Th>Updated</Th><Th /></tr></thead>
            <tbody>
              {skills.data.map((skill) => (
                <tr key={skill.name} className="hover:bg-raised">
                  <Td><span className="font-medium">{skill.name}</span><span className="block text-[12px] text-muted">{skill.description}</span></Td>
                  <Td>{skill.resources.length}</Td>
                  <Td>{skill.enabled ? <Badge tone="success">enabled</Badge> : <Badge tone="warn">disabled</Badge>}</Td>
                  <Td className="text-muted">{relativeTime(skill.updatedAt)}</Td>
                  <Td className="text-right"><Link to={`skills/${encodeURIComponent(skill.name)}/edit`}><Button tone="ghost">Edit</Button></Link></Td>
                </tr>
              ))}
            </tbody>
          </Table>
        )}
      </Panel>
    </>
  );
}

export function SkillEditorScreen({ name }: { name?: string }): ReactNode {
  const editing = name !== undefined && name.length > 0;
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [form, setForm] = useState<AgentSkillRequest>(emptyRequest);
  const existing = useQuery({ queryKey: ['skill', name], queryFn: () => api.skill(name as string), enabled: editing });

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
    });
  }, [existing.isSuccess, existing.data]);

  const save = useMutation({
    mutationFn: () => api.saveSkill(form.name, form),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['skills'] });
      navigate('skills');
    },
  });
  const remove = useMutation({
    mutationFn: () => api.deleteSkill(form.name),
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
        title={editing ? `Edit ${name}` : 'New skill'}
        description="Markdown is stored as source text. It is not rendered in the console."
        actions={<><Button onClick={() => navigate('skills')}>Cancel</Button>{editing && <Button tone="danger" busy={remove.isPending} onClick={() => remove.mutate()}>Delete</Button>}<Button tone="primary" busy={save.isPending} disabled={!valid} onClick={() => save.mutate()}>Save</Button></>}
      />
      {save.isError && <div className="mb-4"><ErrorNote error={save.error} /></div>}
      <div className="flex flex-col gap-4">
        <Panel title="Frontmatter"><div className="grid gap-4 p-4 sm:grid-cols-2">
          <Field label="Name" required><TextInput value={form.name} readOnly={editing} placeholder="invoice-analysis" onChange={(event) => setForm({ ...form, name: event.target.value })} /></Field>
          <Field label="Description" required><TextInput value={form.description} onChange={(event) => setForm({ ...form, description: event.target.value })} /></Field>
          <Field label="Compatibility"><TextInput value={form.compatibility ?? ''} onChange={(event) => setForm({ ...form, compatibility: event.target.value || null })} /></Field>
          <Field label="License"><TextInput value={form.license ?? ''} onChange={(event) => setForm({ ...form, license: event.target.value || null })} /></Field>
          <div className="sm:col-span-2"><Field label="Allowed tools"><TextInput value={form.allowedTools ?? ''} onChange={(event) => setForm({ ...form, allowedTools: event.target.value || null })} /></Field></div>
          <label className="flex cursor-pointer items-center gap-2 text-[13px]"><input type="checkbox" className="accent-[var(--ap-accent)]" checked={form.enabled} onChange={(event) => setForm({ ...form, enabled: event.target.checked })} />Enabled</label>
        </div></Panel>
        <Panel title="Instructions"><div className="p-4"><TextArea rows={14} value={form.instructions} placeholder="Describe the procedure the agent should follow." onChange={(event) => setForm({ ...form, instructions: event.target.value })} /></div></Panel>
        <Panel title="Resources"><div className="flex flex-col gap-3 p-4">
          {form.resources.map((resource, index) => <ResourceEditor key={`${resource.name}-${index}`} resource={resource} onChange={(value) => setForm({ ...form, resources: form.resources.map((item, itemIndex) => itemIndex === index ? value : item) })} onRemove={() => setForm({ ...form, resources: form.resources.filter((_, itemIndex) => itemIndex !== index) })} />)}
          <Button onClick={() => setForm({ ...form, resources: [...form.resources, emptyResource()] })}>Add resource</Button>
        </div></Panel>
      </div>
    </>
  );
}

function ResourceEditor({ resource, onChange, onRemove }: { resource: AgentSkillResourceDefinition; onChange: (value: AgentSkillResourceDefinition) => void; onRemove: () => void }): ReactNode {
  return <div className="grid gap-3 border-t border-line pt-3 sm:grid-cols-2">
    <Field label="Name"><TextInput value={resource.name} placeholder="policy.md" onChange={(event) => onChange({ ...resource, name: event.target.value })} /></Field>
    <Field label="Description"><TextInput value={resource.description ?? ''} onChange={(event) => onChange({ ...resource, description: event.target.value || null })} /></Field>
    <Field label="Media type"><TextInput value={resource.mediaType} placeholder="text/plain" onChange={(event) => onChange({ ...resource, mediaType: event.target.value })} /></Field>
    <div className="sm:col-span-2"><Field label="Content"><TextArea rows={5} value={resource.content} onChange={(event) => onChange({ ...resource, content: event.target.value })} /></Field></div>
    <div><Button tone="danger" onClick={onRemove}>Remove resource</Button><Mono className="ml-3 text-subtle">{resource.mediaType}</Mono></div>
  </div>;
}