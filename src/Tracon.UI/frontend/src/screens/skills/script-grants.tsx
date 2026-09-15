import { useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { client, unwrap } from '../../lib/api';
import { absoluteTime, relativeTime } from '../../lib/format';
import { useT } from '../../lib/i18n';
import {
  Button,
  Empty,
  ErrorNote,
  Field,
  Loading,
  Mono,
  Panel,
  Table,
  Td,
  TextInput,
  Th,
  Unauthorized,
} from '../../components/ui';
import { Tooltip } from '../../components/tooltip';
import type { TraconMetaResponse as Meta, SkillScriptGrant } from '@tracon/client';

/**
 * Grants let a tenant run skill scripts.
 *
 * Granting is a separate, admin-only act: storing a script never implies
 * permission to execute it.
 */
export function ScriptGrantsPanel({ meta }: { meta: Meta }): ReactNode {
  const t = useT();
  const queryClient = useQueryClient();
  const grants = useQuery({
    queryKey: ['skill-script-grants'],
    queryFn: () => unwrap(client.GET('/api/skill-script-grants')) as Promise<SkillScriptGrant[]>,
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
      {/*
        🚨 The colours come from the token set. This banner used to be written
        with `border-red-500 bg-red-500/10 text-red-500` — raw Tailwind palette
        values, which do not follow the theme and are not the console's danger
        tone, so the loudest warning in the product was the one colour nothing
        else in it used.
      */}
      <div className="border-b border-line p-4">
        <div className="rounded border border-danger bg-danger-soft px-3 py-2 text-base font-medium text-danger">
          {t('skills.grants.warning')}
        </div>
      </div>

      {grants.isPending && <Loading rows={3} />}
      {grants.isError && (
        <div className="p-4">
          <ErrorNote error={grants.error} onRetry={() => void grants.refetch()} />
        </div>
      )}

      {(grant.isError || revoke.isError) && (
        <div className="flex flex-col gap-2 p-4">
          {grant.isError && <ErrorNote error={grant.error} onRetry={() => grant.mutate()} />}
          {revoke.isError && (
            <ErrorNote
              error={revoke.error}
              onRetry={
                revoke.variables === undefined ? undefined : () => revoke.mutate(revoke.variables)
              }
            />
          )}
        </div>
      )}

      {grants.isSuccess && active.length === 0 && (
        /*
          🚨 No primary action here even though one exists below, and that is the
          point: no grant is the SAFE state. An empty state that urges the
          operator to grant script execution would be advertising the most
          dangerous switch in the console.
        */
        <Empty title={t('skills.grants.emptyTitle')}>{t('skills.grants.empty')}</Empty>
      )}

      {active.length > 0 && (
        <Table label={t('skills.grants.title')}>
          <thead>
            <tr>
              <Th>{t('skills.grants.skill')}</Th>
              <Th description={t('skills.grants.scriptColumnHint')}>{t('skills.grants.script')}</Th>
              <Th>{t('skills.grants.by')}</Th>
              <Th>{t('skills.grants.at')}</Th>
              <Th />
            </tr>
          </thead>
          <tbody>
            {active.map((item) => (
              <tr key={item.id} className="focus-within:bg-raised hover:bg-raised">
                <Td>{item.skillName}</Td>
                <Td>
                  <Mono>{item.scriptName ?? '*'}</Mono>
                </Td>
                <Td className="text-muted">{item.grantedBy ?? t('skills.grants.unknownBy')}</Td>
                <Td className="text-muted" title={absoluteTime(item.grantedAt)}>
                  {relativeTime(item.grantedAt)}
                </Td>
                <Td className="text-right">
                  {meta.roles.canAdminister && (
                    /* No confirmation step: §175.3 fails on both counts — an
                        administrator grants the script again. It had no
                        consequence sentence at all before, which is the gap
                        this phase closes here. */
                    <Tooltip text={t('skills.grants.revokeEffect')}>
                      <Button
                        tone="danger"
                        busy={revoke.isPending && revoke.variables?.skillName === item.skillName}
                        onClick={() =>
                          revoke.mutate({ skillName: item.skillName, scriptName: item.scriptName })
                        }
                      >
                        {t('skills.grants.revoke')}
                      </Button>
                    </Tooltip>
                  )}
                </Td>
              </tr>
            ))}
          </tbody>
        </Table>
      )}

      {meta.roles.canAdminister ? (
        <div className="grid gap-3 border-t border-line p-4 sm:grid-cols-3">
          <Field label={t('skills.grants.skill')} required>
            <TextInput
              value={skillName}
              placeholder="invoice-analysis"
              onChange={(event) => setSkillName(event.target.value)}
            />
          </Field>
          <Field label={t('skills.grants.scriptField')} hint={t('skills.grants.scriptFieldHint')}>
            <TextInput
              value={scriptName}
              placeholder="total"
              onChange={(event) => setScriptName(event.target.value)}
            />
          </Field>
          <div className="flex items-end">
            <Button
              tone="primary"
              busy={grant.isPending}
              disabled={skillName.trim().length === 0}
              onClick={() => grant.mutate()}
            >
              {t('skills.grants.grant')}
            </Button>
          </div>
        </div>
      ) : (
        <div className="border-t border-line">
          <Unauthorized requires="administrator" />
        </div>
      )}
    </Panel>
  );
}
