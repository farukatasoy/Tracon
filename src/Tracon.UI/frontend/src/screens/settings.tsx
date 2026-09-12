import { useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { apiBase, uiBase } from '../lib/base';
import { setToken, useToken } from '../lib/auth';
import { Link } from '../lib/router';
import { setThemePreference, useThemePreference, type ThemePreference } from '../lib/theme';
import { count } from '../lib/format';
import { LOCALES, useLocale, useT, type Locale } from '../lib/i18n';
import {
  Badge,
  Button,
  ErrorNote,
  Field,
  Loading,
  Mono,
  PageHeader,
  Panel,
  Select,
} from '../components/ui';
import { QuotaPanel } from '../components/quota-panel';
import { WebhookPanel } from '../components/webhook-panel';
import { ApiKeyPanel } from '../components/api-key-panel';
import { TenantProviderPanel } from '../components/tenant-provider-panel';
import { RetentionPanel } from '../components/retention-panel';
import { readVoiceForLocale, voiceOptionMeta, writeVoiceForLocale } from '../lib/voice';
import type { TraconMetaResponse as Meta } from '@tracon/client';
import type { RunStatistics } from '../lib/server-types';

export function SettingsScreen({ meta }: { meta: Meta }): ReactNode {
  const t = useT();
  const { locale, setLocale } = useLocale();
  const token = useToken();
  const { preference } = useThemePreference();

  const stats = useQuery({
    queryKey: ['stats', ''],
    queryFn: () => unwrap(client.GET('/api/stats', { params: { query: {} } })) as Promise<RunStatistics>,
  });
  const tenant = useQuery({
    queryKey: ['current-tenant'],
    queryFn: () => unwrap(client.GET('/api/tenants/current')),
  });

  return (
    <>
      <PageHeader
        title={t('nav.settings')}
        description={t('settings.description')}
      />

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <Panel title={t('settings.instance')}>
          <dl className="divide-y divide-line">
            <Row label={t('agentDetail.version')}>
              <Mono>{meta.version}</Mono>
            </Row>
            <Row label={t('settings.prefix')}>
              <Mono>{meta.prefix}</Mono>
            </Row>
            <Row label={t('settings.uiBase')}>
              <Mono>{uiBase}</Mono>
            </Row>
            <Row label={t('settings.apiBase')}>
              <Mono>{apiBase}</Mono>
            </Row>
          </dl>
        </Panel>

        <Panel title={t('settings.access')}>
          <dl className="divide-y divide-line">
            <Row label={t('settings.remoteAccess')}>
              {meta.authentication.allowRemoteAccess ? (
                <Badge tone="warn">{t('common.enabled')}</Badge>
              ) : (
                <Badge tone="success" description={t('settings.loopbackTitle')}>
                  {t('settings.loopbackOnly')}
                </Badge>
              )}
            </Row>
            <Row label={t('settings.bearerToken')}>
              {meta.authentication.requiresBearerToken ? (
                <Badge tone="accent">{t('settings.required')}</Badge>
              ) : (
                <span className="text-subtle">{t('settings.notConfigured')}</span>
              )}
            </Row>
            <Row label={t('settings.authorizationPolicy')}>
              {meta.authentication.requiresAuthorizationPolicy ? (
                <Badge tone="accent">{t('settings.applied')}</Badge>
              ) : (
                <span className="text-subtle">{t('settings.notConfigured')}</span>
              )}
            </Row>
            {token !== null && (
              <Row label={t('settings.thisTab')}>
                <div className="flex items-center gap-2">
                  <Badge tone="success">{t('settings.tokenStored')}</Badge>
                  <Button tone="ghost" onClick={() => setToken(null)}>
                    {t('settings.forget')}
                  </Button>
                </div>
              </Row>
            )}
          </dl>
          <p className="border-t border-line px-4 py-2.5 text-xs text-subtle">
            {t('settings.tokenNotice')}
          </p>
        </Panel>

        <Panel title={t('settings.storage')}>
          <dl className="divide-y divide-line">
            <Row label={t('settings.mode')}>
              {meta.storage.persistent ? (
                <Badge tone="success">{t('settings.persistent')}</Badge>
              ) : (
                <Badge tone="warn">{t('settings.inMemory')}</Badge>
              )}
            </Row>
            <Row label={t('settings.agentDefinitions')}>
              <Mono>{meta.storage.agentDefinitionStore}</Mono>
            </Row>
            <Row label={t('nav.runs')}>
              <Mono>{meta.storage.runStore}</Mono>
            </Row>
            <Row label={t('nav.sessions')}>
              <Mono>{meta.storage.sessionStore}</Mono>
            </Row>
          </dl>
          {!meta.storage.persistent && (
            <p className="border-t border-line px-4 py-2.5 text-xs text-subtle">
              {t('settings.inMemoryNotice')} <Mono>UsePostgreSql(connectionString)</Mono>.
            </p>
          )}
        </Panel>

        <Panel title={t('settings.console')}>
          <div className="grid gap-4 p-4 sm:grid-cols-2">
            {/*
              `Field`'s render-prop form rather than a hand-rolled label: the id
              it generates is what binds the caption to the `<select>`, and the
              hint explains why "follow the system" is not the default (K-757 —
              a browser cannot report "no preference", so following the system
              means defaulting to light).
            */}
            <Field label={t('settings.theme')} hint={t('settings.themeHint')}>
              {(ids) => (
                <Select
                  {...ids}
                  value={preference}
                  onChange={(value) => setThemePreference(value as ThemePreference)}
                >
                  <option value="system">{t('settings.followSystem')}</option>
                  <option value="light">{t('settings.light')}</option>
                  <option value="dark">{t('settings.dark')}</option>
                </Select>
              )}
            </Field>

            <Field label={t('shell.language')}>
              {(ids) => (
                <Select
                  {...ids}
                  value={locale}
                  testId="language-select"
                  onChange={(value) => setLocale(value as Locale)}
                >
                  {LOCALES.map((candidate) => (
                    <option key={candidate} value={candidate}>
                      {t(`shell.language.${candidate}`)}
                    </option>
                  ))}
                </Select>
              )}
            </Field>
          </div>

          <p className="border-t border-line px-4 py-2.5 text-xs text-subtle">
            {t('settings.languageNotice')}
          </p>
        </Panel>

        <VoicePreferencePanel />

        <Panel title={t('settings.activity')}>
          {stats.isPending && <Loading rows={6} />}
          {stats.isError && (
            <div className="p-4">
              <ErrorNote error={stats.error} onRetry={() => void stats.refetch()} />
            </div>
          )}
          {stats.isSuccess && (
            <dl className="divide-y divide-line">
              <Row label={t('nav.runs')}>{count(stats.data.totalRuns as number)}</Row>
              <Row label={t('runs.filter.completed')}>{count(stats.data.completedRuns as number)}</Row>
              <Row label={t('runs.stat.failed')}>{count(stats.data.failedRuns as number)}</Row>
              <Row label={t('runs.filter.running')}>{count(stats.data.runningRuns as number)}</Row>
              <Row label={t('experiments.totalTokens')}>{count(stats.data.totalTokens as number)}</Row>
              <Row label={t('settings.tenant')}>
                {/* An em dash used to cover two different facts: "no tenant" and
                    "the lookup failed". The second one now says so. */}
                {tenant.isError ? (
                  <Badge tone="warn" description={String(tenant.error)}>
                    {t('settings.tenantUnknown')}
                  </Badge>
                ) : (
                  <Mono>{tenant.data?.tenantId ?? '—'}</Mono>
                )}
              </Row>
            </dl>
          )}
          <p className="border-t border-line px-4 py-2.5 text-xs text-subtle">
            {t('settings.tokenCountNotice')}
          </p>
        </Panel>

        <Panel title={t('settings.tokenByModel')}>
          {stats.isPending && <Loading rows={4} />}
          {stats.isError && (
            <div className="p-4">
              <ErrorNote error={stats.error} onRetry={() => void stats.refetch()} />
            </div>
          )}
          {stats.isSuccess &&
            (stats.data.byModel.length === 0 ? (
              <p className="px-4 py-4 text-sm text-subtle">
                {t('settings.noModelRecorded')}
              </p>
            ) : (
              <dl className="divide-y divide-line">
                {stats.data.byModel.map((model) => (
                  <Row key={model.modelId} label={model.modelId}>
                    {t('settings.modelTokens', { tokens: count(model.totalTokens as number | undefined) })}
                    <span className="ml-2 text-xs text-subtle">
                      {t('settings.modelBreakdown', {
                        input: count(model.inputTokens as number | undefined),
                        output: count(model.outputTokens as number | undefined),
                        runs: count(model.totalRuns as number),
                      })}
                    </span>
                  </Row>
                ))}
              </dl>
            ))}
          <p className="border-t border-line px-4 py-2.5 text-xs text-subtle">
            {t('settings.costOnDashboardBefore')}{' '}
            <Link to="dashboard">{t('nav.dashboard')}</Link>
            . {t('settings.costOnDashboardAfter')}
          </p>
        </Panel>

        <QuotaPanel />

        <WebhookPanel />

        <ApiKeyPanel />

        <TenantProviderPanel />

        <RetentionPanel />
      </div>
    </>
  );
}

/**
 * Which voice speaks each language.
 *
 * 🚨 A Turkish answer read out by an English voice is unintelligible. Phase 138
 * added `VoiceDescriptor.attributes`, and the option label below shows the
 * `language`/`gender` values a provider reports through it — but not every
 * provider reports them, so the mapping still cannot be derived reliably and
 * an operator sets it here once. The conversation panel then sends the chosen
 * id in the `start` frame, which the protocol already accepts; the server
 * never learns about languages.
 *
 * The panel hides itself when no speech provider is configured: an empty
 * dropdown would only raise a question it cannot answer.
 */
function VoicePreferencePanel(): ReactNode {
  const t = useT();
  const [, setVersion] = useState(0);

  const voices = useQuery({
    queryKey: ['voices'],
    queryFn: () => unwrap(client.GET('/api/voice/voices')),
    retry: false,
  });

  if (!voices.isSuccess || voices.data.length === 0) {
    return null;
  }

  return (
    <Panel title={t('settings.voices')}>
      <div className="grid gap-4 p-4 sm:grid-cols-2">
        {LOCALES.map((candidate) => (
          <Field
            key={candidate}
            label={t('settings.voiceFor', { language: t(`shell.language.${candidate}`) })}
          >
            {(ids) => (
              <Select
                {...ids}
                value={readVoiceForLocale(candidate) ?? ''}
                testId={`voice-select-${candidate}`}
                onChange={(value) => {
                  writeVoiceForLocale(candidate, value.length === 0 ? null : value);
                  setVersion((current) => current + 1);
                }}
              >
                <option value="">{t('settings.serverDefaultVoice')}</option>
                {voices.data.map((voice) => {
                  const meta = voiceOptionMeta(voice.attributes);

                  return (
                    <option key={voice.voiceId} value={voice.voiceId}>
                      {meta === null ? voice.name : `${voice.name} (${meta})`}
                    </option>
                  );
                })}
              </Select>
            )}
          </Field>
        ))}
      </div>

      <p className="border-t border-line px-4 py-2.5 text-xs text-subtle">
        {t('settings.voiceNotice')}
      </p>
    </Panel>
  );
}

function Row({ label, children }: { label: string; children: ReactNode }): ReactNode {
  return (
    <div className="flex items-center gap-4 px-4 py-2">
      <dt className="w-40 shrink-0 text-sm text-subtle">{label}</dt>
      <dd className="min-w-0 flex-1 text-base break-words">{children}</dd>
    </div>
  );
}
