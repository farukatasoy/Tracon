import { useEffect, useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { relativeTime } from '../lib/format';
import { useT } from '../lib/i18n';
import { Link, useNavigate } from '../lib/router';
import type {
  TraconMetaResponse as Meta,
  CurrentTenantResponse,
  InboundTriggerPayloadMode,
  InboundTriggerResponse,
  InboundTriggerTargetKind,
} from '@tracon/client';

// The generated request type makes every field optional (omission means "use
// the server default"), but this form always sends a fully-populated body —
// a local shape keeps the JSX's direct field reads (`form.targetKind`, ...)
// free of `?? default` noise.
interface TriggerForm {
  targetKind: InboundTriggerTargetKind;
  targetName: string;
  signingSecretConfigurationName: string;
  payloadMode: InboundTriggerPayloadMode;
  payloadPath: string | null;
  enabled: boolean;
}
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
import { PlusIcon } from '../components/icons';

const emptyForm = (): TriggerForm => ({
  targetKind: 'Agent',
  targetName: '',
  signingSecretConfigurationName: '',
  payloadMode: 'WholeBody',
  payloadPath: null,
  enabled: true,
});

export function TriggersScreen({ meta }: { meta: Meta }): ReactNode {
  const t = useT();
  const triggers = useQuery({
    queryKey: ['triggers'],
    queryFn: () => unwrap(client.GET('/api/triggers')) as Promise<InboundTriggerResponse[]>,
  });

  return (
    <>
      <PageHeader
        title={t('nav.triggers')}
        description={t('triggers.description')}
        actions={
          meta.roles.canAdminister && (
            <Link to="triggers/new">
              <Button tone="primary">
                <PlusIcon className="size-3.5" />
                {t('triggers.new')}
              </Button>
            </Link>
          )
        }
      />
      <Panel>
        {triggers.isPending && <Loading />}
        {triggers.isError && <div className="p-4"><ErrorNote error={triggers.error} /></div>}
        {triggers.isSuccess && triggers.data.length === 0 && (
          <Empty title={t('triggers.empty.title')}>{t('triggers.empty.body')}</Empty>
        )}
        {triggers.isSuccess && triggers.data.length > 0 && (
          <Table>
            <thead>
              <tr>
                <Th>{t('common.name')}</Th>
                <Th>{t('triggers.target')}</Th>
                <Th>{t('triggers.secret')}</Th>
                <Th>{t('common.status')}</Th>
                <Th>{t('common.updated')}</Th>
                <Th />
              </tr>
            </thead>
            <tbody>
              {triggers.data.map((trigger) => (
                <tr key={trigger.name} className="hover:bg-raised">
                  <Td><span className="font-medium">{trigger.name}</span></Td>
                  <Td><Mono>{trigger.targetKind}</Mono> {trigger.targetName}</Td>
                  <Td>
                    {trigger.resolved ? (
                      <Badge tone="success">{t('triggers.resolved')}</Badge>
                    ) : (
                      <Badge tone="warn">{t('triggers.unresolved')}</Badge>
                    )}
                  </Td>
                  <Td>
                    {trigger.enabled ? (
                      <Badge tone="success">{t('common.enabled')}</Badge>
                    ) : (
                      <Badge tone="warn">{t('common.disabled')}</Badge>
                    )}
                  </Td>
                  <Td className="text-muted">{relativeTime(trigger.updatedAt)}</Td>
                  <Td className="text-right"><Link to={`triggers/${encodeURIComponent(trigger.name)}/edit`}><Button tone="ghost">{t('common.edit')}</Button></Link></Td>
                </tr>
              ))}
            </tbody>
          </Table>
        )}
      </Panel>
    </>
  );
}

export function TriggerEditorScreen({ name, meta }: { name?: string; meta: Meta }): ReactNode {
  const t = useT();
  const editing = name !== undefined && name.length > 0;
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [triggerName, setTriggerName] = useState(name ?? '');
  const [form, setForm] = useState<TriggerForm>(emptyForm);
  const existing = useQuery({
    queryKey: ['trigger', name],
    queryFn: () =>
      unwrap(
        client.GET('/api/triggers/{name}', { params: { path: { name: name as string } } }),
      ) as Promise<InboundTriggerResponse>,
    enabled: editing,
  });
  const tenant = useQuery({
    queryKey: ['current-tenant'],
    queryFn: () => unwrap(client.GET('/api/tenants/current')) as Promise<CurrentTenantResponse>,
  });

  useEffect(() => {
    if (!existing.isSuccess) return;
    const trigger = existing.data;
    setForm({
      targetKind: trigger.targetKind,
      targetName: trigger.targetName,
      signingSecretConfigurationName: trigger.signingSecretConfigurationName,
      payloadMode: trigger.payloadMode,
      payloadPath: trigger.payloadPath ?? null,
      enabled: trigger.enabled,
    });
  }, [existing.isSuccess, existing.data]);

  const save = useMutation({
    mutationFn: () =>
      unwrap(
        client.PUT('/api/triggers/{name}', {
          params: { path: { name: triggerName } },
          body: form,
        }),
      ) as Promise<InboundTriggerResponse>,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['triggers'] });
      navigate('triggers');
    },
  });
  const remove = useMutation({
    mutationFn: () =>
      unwrap(client.DELETE('/api/triggers/{name}', { params: { path: { name: triggerName } } })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['triggers'] });
      navigate('triggers');
    },
  });

  if (editing && existing.isPending) return <Loading />;

  const valid =
    triggerName.trim().length > 0 &&
    form.targetName.trim().length > 0 &&
    form.signingSecretConfigurationName.trim().length > 0 &&
    (form.payloadMode !== 'Path' || (form.payloadPath ?? '').trim().length > 0);

  const acceptUrl = `${window.location.origin}${meta.prefix}/api/triggers/${encodeURIComponent(tenant.data?.tenantId ?? '…')}/${encodeURIComponent(triggerName || '…')}`;

  return (
    <>
      <PageHeader
        title={editing ? t('triggers.editTitle', { name: name ?? '' }) : t('triggers.newTitle')}
        description={t('triggers.editorDescription')}
        actions={
          <>
            <Button onClick={() => navigate('triggers')}>{t('common.cancel')}</Button>
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
        <Panel title={t('triggers.definition')}>
          <div className="grid gap-4 p-4 sm:grid-cols-2">
            <Field label={t('common.name')} required>
              <TextInput
                value={triggerName}
                readOnly={editing}
                placeholder="slack"
                onChange={(event) => setTriggerName(event.target.value)}
              />
            </Field>
            <Field label={t('triggers.targetKind')}>
              <Select
                value={form.targetKind}
                onChange={(value) => setForm({ ...form, targetKind: value as InboundTriggerTargetKind })}
              >
                <option value="Agent">{t('triggers.targetKind.agent')}</option>
                <option value="Workflow">{t('triggers.targetKind.workflow')}</option>
              </Select>
            </Field>
            <Field label={t('triggers.targetName')} required>
              <TextInput
                value={form.targetName}
                placeholder="demo"
                onChange={(event) => setForm({ ...form, targetName: event.target.value })}
              />
            </Field>
            <Field label={t('triggers.secretKey')} hint={t('triggers.secretKeyHint')}>
              <TextInput
                value={form.signingSecretConfigurationName}
                placeholder="Tracon:TriggerSecrets:Slack"
                onChange={(event) => setForm({ ...form, signingSecretConfigurationName: event.target.value })}
              />
            </Field>
            <Field label={t('triggers.payloadMode')}>
              <Select
                value={form.payloadMode}
                onChange={(value) =>
                  setForm({ ...form, payloadMode: value as InboundTriggerPayloadMode, payloadPath: value === 'Path' ? form.payloadPath : null })
                }
              >
                <option value="WholeBody">{t('triggers.payloadMode.wholeBody')}</option>
                <option value="Path">{t('triggers.payloadMode.path')}</option>
              </Select>
            </Field>
            {form.payloadMode === 'Path' && (
              <Field label={t('triggers.payloadPath')} required hint={t('triggers.payloadPathHint')}>
                <TextInput
                  value={form.payloadPath ?? ''}
                  placeholder="event.text"
                  onChange={(event) => setForm({ ...form, payloadPath: event.target.value })}
                />
              </Field>
            )}
            <label className="flex cursor-pointer items-center gap-2 text-[13px]">
              <input
                type="checkbox"
                className="accent-[var(--ap-accent)]"
                checked={form.enabled}
                onChange={(event) => setForm({ ...form, enabled: event.target.checked })}
              />
              {t('common.enabled')}
            </label>
            <div className="sm:col-span-2">
              <Field label={t('triggers.acceptUrl')} hint={t('triggers.acceptUrlHint')}>
                <Mono className="block break-all rounded border border-line bg-raised px-2 py-1.5 text-[12px]">{acceptUrl}</Mono>
              </Field>
            </div>
          </div>
        </Panel>
      </div>
    </>
  );
}
