import { useState, type ReactNode } from 'react';
import { useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query';
import { TraconError, type TraconMetaResponse as Meta } from '@tracon/client';
import { client, unwrap } from '../../lib/api';
import { absoluteTime, relativeTime } from '../../lib/format';
import { useT } from '../../lib/i18n';
import {
  Badge,
  Button,
  CodeBlock,
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
import type { AgentSkillScriptDefinition } from '@tracon/client';
import type { AgentSkillDefinition, SkillScriptGrant } from '../../lib/server-types';
import { currentHash, pinOf } from './model';

/** The query key prefix of the skill reads this panel compares grants against. */
export const SKILL_PIN_QUERY_KEY = 'skill-pin';

/** What an administrator read before granting: the names and the content behind them. */
interface Review {
  skillName: string;
  scriptName: string | null;
  /** `null`: no stored or code skill carries the name, so the grant covers scripts on disk. */
  skill: AgentSkillDefinition | null;
}

/**
 * Reads a skill the way the grant endpoint resolves it — a code skill wins over a
 * stored one with the same name — or `null` when neither source has the name.
 */
async function readSkill(name: string): Promise<AgentSkillDefinition | null> {
  try {
    return (await unwrap(
      client.GET('/api/skills/{name}', { params: { path: { name } } }),
    )) as AgentSkillDefinition;
  } catch (error) {
    if (error instanceof TraconError && error.status === 404) {
      return null;
    }

    throw error;
  }
}

/** What a review shows: the scripts the grant would cover and the hash it would pin. */
function summarize(reviewed: Review): {
  scripts: AgentSkillScriptDefinition[];
  hash: string | null;
  missingScript: boolean;
} {
  const skill = reviewed.skill;

  if (skill === null) {
    return { scripts: [], hash: null, missingScript: false };
  }

  const hash = currentHash(skill, reviewed.scriptName);
  const scripts =
    reviewed.scriptName === null
      ? skill.scripts
      : skill.scripts.filter((script) => script.name === reviewed.scriptName);

  return { scripts, hash, missingScript: hash === null };
}

/**
 * Grants let a tenant run skill scripts.
 *
 * Granting is a separate, admin-only act: storing a script never implies
 * permission to execute it. A grant pins the content it was given for, so the
 * form grants only what the administrator has just read.
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
    await queryClient.invalidateQueries({ queryKey: [SKILL_PIN_QUERY_KEY] });
  };

  /*
    🚨 The hash comes from reading the skill HERE, at review time, and never
    from the list: the list shows stored skills only, while a code skill with
    the same name is the one that runs. Reading it again at grant time instead
    would pin whatever happened to be stored at that moment — content nobody
    looked at, which is exactly what the pin exists to refuse.
  */
  const review = useMutation({
    mutationFn: async (): Promise<Review> => {
      const name = skillName.trim();

      return { skillName: name, scriptName: scriptName.trim() || null, skill: await readSkill(name) };
    },
  });
  const grant = useMutation({
    mutationFn: (target: Review) =>
      unwrap(
        client.POST('/api/skill-script-grants', {
          body: {
            skillName: target.skillName,
            scriptName: target.scriptName,
            expectedContentHash:
              target.skill === null ? null : currentHash(target.skill, target.scriptName),
          },
        }),
      ) as Promise<SkillScriptGrant>,
    onSuccess: async () => {
      setSkillName('');
      setScriptName('');
      review.reset();
      await invalidate();
    },
    onError: (error) => {
      // 409: the content changed after it was read (or script execution is off).
      // The hash in hand is spent either way, so the administrator reads the
      // content again before a second attempt.
      if (error instanceof TraconError && error.status === 409) {
        review.reset();
      }
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
  const names = [...new Set(active.map((item) => item.skillName))];
  const skills = useQueries({
    queries: names.map((name) => ({
      queryKey: [SKILL_PIN_QUERY_KEY, name],
      queryFn: () => readSkill(name),
    })),
  });
  const skillOf = (name: string): AgentSkillDefinition | null | undefined => {
    const query = skills[names.indexOf(name)];

    return query?.isSuccess ? query.data : undefined;
  };

  const edit = (update: () => void): void => {
    update();
    // A review describes the names it was made for; new names need a new read.
    review.reset();
    grant.reset();
  };

  const reviewed = review.data;
  const summary = reviewed === undefined ? undefined : summarize(reviewed);
  const failedGrant = grant.variables;

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

      {(grant.isError || revoke.isError || review.isError) && (
        <div className="flex flex-col gap-2 p-4">
          {review.isError && <ErrorNote error={review.error} onRetry={() => review.mutate()} />}
          {grant.isError && (
            <ErrorNote
              error={grant.error}
              onRetry={
                failedGrant === undefined ||
                (grant.error instanceof TraconError && grant.error.status === 409)
                  ? undefined
                  : () => grant.mutate(failedGrant)
              }
            />
          )}
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
              <Th description={t('skills.grants.pinColumnHint')}>{t('skills.grants.pin')}</Th>
              <Th>{t('skills.grants.by')}</Th>
              <Th>{t('skills.grants.at')}</Th>
              <Th />
            </tr>
          </thead>
          <tbody>
            {active.map((item) => {
              const skill = skillOf(item.skillName);
              const pin = skill === undefined ? undefined : pinOf(item, skill);

              return (
                <tr key={item.id} className="focus-within:bg-raised hover:bg-raised">
                  <Td>{item.skillName}</Td>
                  <Td>
                    <Mono>{item.scriptName ?? '*'}</Mono>
                  </Td>
                  <Td>
                    {pin === 'current' && (
                      <Badge tone="success" description={t('skills.grants.pinCurrentHint')}>
                        {t('skills.grants.pinCurrent')}
                      </Badge>
                    )}
                    {pin === 'stale' && (
                      <Badge tone="warn" description={t('skills.grants.pinStaleHint')}>
                        {t('skills.grants.pinStale')}
                      </Badge>
                    )}
                    {pin === 'disk' && (
                      <Badge description={t('skills.grants.pinDiskHint')}>
                        {t('skills.grants.pinDisk')}
                      </Badge>
                    )}
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
              );
            })}
          </tbody>
        </Table>
      )}

      {meta.roles.canAdminister ? (
        <>
          <div className="grid gap-3 border-t border-line p-4 sm:grid-cols-3">
            <Field label={t('skills.grants.skill')} required>
              <TextInput
                value={skillName}
                placeholder="invoice-analysis"
                onChange={(event) => edit(() => setSkillName(event.target.value))}
              />
            </Field>
            <Field label={t('skills.grants.scriptField')} hint={t('skills.grants.scriptFieldHint')}>
              <TextInput
                value={scriptName}
                placeholder="total"
                onChange={(event) => edit(() => setScriptName(event.target.value))}
              />
            </Field>
            <div className="flex items-end">
              <Button
                busy={review.isPending}
                disabled={skillName.trim().length === 0}
                onClick={() => review.mutate()}
              >
                {t('skills.grants.review')}
              </Button>
            </div>
          </div>

          {reviewed !== undefined && summary !== undefined && (
            <section
              aria-label={t('skills.grants.reviewTitle')}
              className="flex flex-col gap-3 border-t border-line p-4"
            >
              <h3 className="text-base font-medium">{t('skills.grants.reviewTitle')}</h3>

              {reviewed.skill === null && (
                <p className="text-base text-muted">{t('skills.grants.reviewDisk')}</p>
              )}

              {summary.missingScript && (
                <p className="text-base text-danger">
                  {t('skills.grants.reviewMissingScript', {
                    skill: reviewed.skillName,
                    script: reviewed.scriptName ?? '',
                  })}
                </p>
              )}

              {reviewed.skill !== null && !summary.missingScript && (
                <>
                  <p className="text-base text-muted">
                    {reviewed.skill.origin === 'Code'
                      ? t('skills.grants.reviewFromCode')
                      : t('skills.grants.reviewFromStore')}
                  </p>
                  {summary.scripts.length === 0 && (
                    <p className="text-base text-muted">{t('skills.grants.reviewNoScripts')}</p>
                  )}
                  {/* Everything the hash covers is shown: extension, content, and the
                      argument schema, which also shapes what a call may pass. */}
                  {summary.scripts.map((script) => (
                    <div key={script.name} className="flex flex-col gap-1">
                      <Mono>{`${script.name}.${script.extension}`}</Mono>
                      <CodeBlock code={script.content} maxHeight="max-h-64" />
                      {script.parametersSchema ? (
                        <>
                          <span className="text-xs text-muted">{t('skills.grants.reviewSchema')}</span>
                          <CodeBlock code={script.parametersSchema} maxHeight="max-h-40" />
                        </>
                      ) : (
                        <span className="text-xs text-muted">{t('skills.grants.reviewNoSchema')}</span>
                      )}
                    </div>
                  ))}
                  <p className="text-xs text-muted">
                    {t('skills.grants.reviewHash')} <Mono>{summary.hash}</Mono>
                  </p>
                </>
              )}

              <div className="flex gap-2">
                {!summary.missingScript && (
                  <Button tone="primary" busy={grant.isPending} onClick={() => grant.mutate(reviewed)}>
                    {t('skills.grants.grant')}
                  </Button>
                )}
                <Button tone="ghost" onClick={() => review.reset()}>
                  {t('common.cancel')}
                </Button>
              </div>
            </section>
          )}
        </>
      ) : (
        <div className="border-t border-line">
          <Unauthorized requires="administrator" />
        </div>
      )}
    </Panel>
  );
}
