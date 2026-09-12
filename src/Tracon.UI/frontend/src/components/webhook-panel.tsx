import { useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { relativeTime } from '../lib/format';
import { useT } from '../lib/i18n';
import {
  Badge,
  Button,
  Empty,
  ErrorNote,
  Field,
  Loading,
  Mono,
  Panel,
  Td,
  Th,
  Table,
  TextInput,
} from './ui';
import type { WebhookDeliveryStatus, WebhookTestResponse } from '@tracon/client';
import type { WebhookDelivery, WebhookSubscription } from '../lib/server-types';

/** Every event Tracon can publish. Mirrors `WebhookEvents` on the server. */
const EVENTS = [
  'run.completed',
  'run.failed',
  'approval.pending',
  'workflow.request.pending',
  'job.completed',
  'job.failed',
  'eval.completed',
  'quota.threshold',
] as const;

/**
 * Webhook subscriptions and their recent deliveries.
 *
 * No field on this screen holds a secret. A subscription carries only the NAME
 * of the configuration key its signing secret is read from; the value stays in
 * `IConfiguration` and never reaches the browser or the database.
 */
export function WebhookPanel(): ReactNode {
  const t = useT();
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(false);
  const [expanded, setExpanded] = useState<string | null>(null);

  const subscriptions = useQuery({
    queryKey: ['webhooks'],
    queryFn: () => unwrap(client.GET('/api/webhooks')) as Promise<WebhookSubscription[]>,
  });

  const remove = useMutation({
    mutationFn: (name: string) =>
      unwrap(client.DELETE('/api/webhooks/{name}', { params: { path: { name } } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['webhooks'] }),
  });

  return (
    <Panel
      title={t('webhooks.title')}
      className="lg:col-span-2"
      actions={
        <Button tone="default" onClick={() => setOpen((value) => !value)}>
          {open ? t('common.close') : t('webhooks.add')}
        </Button>
      }
    >
      {subscriptions.isPending && <Loading />}
      {subscriptions.isError && (
        <div className="p-4">
          <ErrorNote error={subscriptions.error} />
        </div>
      )}

      {open && (
        <WebhookForm
          onDone={() => {
            setOpen(false);
            void queryClient.invalidateQueries({ queryKey: ['webhooks'] });
          }}
        />
      )}

      {subscriptions.isSuccess &&
        (subscriptions.data.length === 0 ? (
          <Empty title={t('webhooks.empty.title')}>{t('webhooks.empty.body')}</Empty>
        ) : (
          <div className="divide-y divide-line">
            {subscriptions.data.map((subscription) => (
              <SubscriptionRow
                key={subscription.id}
                subscription={subscription}
                expanded={expanded === subscription.name}
                onToggle={() =>
                  setExpanded((current) =>
                    current === subscription.name ? null : subscription.name,
                  )
                }
                onDelete={() => remove.mutate(subscription.name)}
              />
            ))}
          </div>
        ))}

      <p className="border-t border-line px-4 py-2.5 text-[11px] text-subtle">
        {t('webhooks.noticeBefore')} <Mono>https</Mono>. {t('webhooks.noticeAfter')}{' '}
        <Mono>/api/runs/&#123;id&#125;</Mono>.
      </p>
    </Panel>
  );
}

function SubscriptionRow({
  subscription,
  expanded,
  onToggle,
  onDelete,
}: {
  subscription: WebhookSubscription;
  expanded: boolean;
  onToggle: () => void;
  onDelete: () => void;
}): ReactNode {
  const t = useT();
  const test = useMutation({
    mutationFn: () =>
      unwrap(
        client.POST('/api/webhooks/{name}/test', { params: { path: { name: subscription.name } } }),
      ) as Promise<WebhookTestResponse>,
  });

  return (
    <div className="px-4 py-3" data-testid="webhook-row">
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-[13px] font-medium">{subscription.name}</span>
        {subscription.enabled ? (
          <Badge tone="success">{t('common.enabled')}</Badge>
        ) : (
          <Badge tone="danger" title={t('webhooks.disabledTitle')}>
            {t('common.disabled')}
          </Badge>
        )}
        {subscription.consecutiveFailures > 0 && (
          <Badge tone="warn">
            {t('webhooks.failing', { count: subscription.consecutiveFailures })}
          </Badge>
        )}
        <div className="ml-auto flex items-center gap-1.5">
          <Button tone="ghost" testId="webhook-test" onClick={() => test.mutate()}>
            {test.isPending ? t('webhooks.sending') : t('webhooks.sendTest')}
          </Button>
          <Button tone="ghost" onClick={onToggle}>
            {expanded ? t('audit.hide') : t('webhooks.deliveries')}
          </Button>
          <Button tone="danger" onClick={onDelete}>
            {t('common.delete')}
          </Button>
        </div>
      </div>

      <div className="mt-1 text-[12px] text-muted">
        <Mono>{subscription.url}</Mono>
      </div>

      <div className="mt-1.5 flex flex-wrap gap-1">
        {subscription.events.map((event) => (
          <Badge key={event} tone="neutral">
            {event}
          </Badge>
        ))}
      </div>

      <div className="mt-1.5 text-[11px] text-subtle">
        {subscription.secretConfigurationKey != null ? (
          <>
            {t('webhooks.signedWith')} <Mono>{subscription.secretConfigurationKey}</Mono>.{' '}
            {t('webhooks.secretNeverStored')}
          </>
        ) : (
          t('webhooks.notSigned')
        )}
      </div>

      {test.isSuccess && (
        <p className="mt-2 text-[11px] text-muted" data-testid="webhook-test-result">
          {test.data.message}
        </p>
      )}
      {test.isError && (
        <div className="mt-2">
          <ErrorNote error={test.error} />
        </div>
      )}

      {expanded && <Deliveries name={subscription.name} />}
    </div>
  );
}

function Deliveries({ name }: { name: string }): ReactNode {
  const t = useT();
  const deliveries = useQuery({
    queryKey: ['webhook-deliveries', name],
    queryFn: () =>
      unwrap(
        client.GET('/api/webhooks/{name}/deliveries', {
          params: { path: { name }, query: { take: 20 } },
        }),
      ) as Promise<WebhookDelivery[]>,
  });

  if (deliveries.isPending) {
    return <Loading />;
  }

  if (deliveries.isError) {
    return <ErrorNote error={deliveries.error} />;
  }

  if (deliveries.data.length === 0) {
    return <p className="mt-3 text-[12px] text-subtle">{t('webhooks.noDeliveries')}</p>;
  }

  return (
    <div className="mt-3 rounded-md border border-line">
      <Table>
        <thead>
          <tr>
            <Th>{t('webhooks.event')}</Th>
            <Th>{t('common.status')}</Th>
            <Th>{t('webhooks.code')}</Th>
            <Th>{t('jobs.attempt')}</Th>
            <Th>{t('audit.when')}</Th>
          </tr>
        </thead>
        <tbody>
          {deliveries.data.map((delivery) => (
            <tr key={delivery.id}>
              <Td>
                <Mono>{delivery.eventType}</Mono>
              </Td>
              <Td>
                <Badge tone={deliveryTone(delivery.status)}>{delivery.status.toLowerCase()}</Badge>
              </Td>
              <Td>{delivery.responseCode ?? '—'}</Td>
              <Td>{delivery.attempt}</Td>
              <Td title={delivery.error ?? undefined}>{relativeTime(delivery.createdAt)}</Td>
            </tr>
          ))}
        </tbody>
      </Table>
    </div>
  );
}

function deliveryTone(status: WebhookDeliveryStatus): 'success' | 'danger' | 'warn' | 'neutral' {
  switch (status) {
    case 'Delivered':
      return 'success';
    case 'Failed':
      return 'danger';
    case 'Dropped':
      return 'warn';
    default:
      return 'neutral';
  }
}

function WebhookForm({ onDone }: { onDone: () => void }): ReactNode {
  const t = useT();
  const [name, setName] = useState('');
  const [url, setUrl] = useState('');
  const [secretKey, setSecretKey] = useState('');
  const [events, setEvents] = useState<string[]>(['run.completed', 'run.failed']);

  const save = useMutation({
    mutationFn: () =>
      unwrap(
        client.PUT('/api/webhooks/{name}', {
          params: { path: { name } },
          body: {
            url,
            events,
            secretConfigurationKey: secretKey.trim() === '' ? null : secretKey.trim(),
            enabled: true,
          },
        }),
      ) as Promise<WebhookSubscription>,
    onSuccess: onDone,
  });

  return (
    <div className="space-y-3 border-b border-line bg-raised/40 p-4">
      <div className="grid gap-3 sm:grid-cols-2">
        <Field label={t('common.name')} required>
          <TextInput
            value={name}
            placeholder="order-service"
            data-testid="webhook-name"
            onChange={(event) => setName(event.target.value)}
          />
        </Field>
        <Field label={t('webhooks.url')} required hint={t('webhooks.urlHint')}>
          <TextInput
            value={url}
            placeholder="https://example.com/hooks/tracon"
            data-testid="webhook-url"
            onChange={(event) => setUrl(event.target.value)}
          />
        </Field>
      </div>

      <Field
        label={t('webhooks.signingKey')}
        hint={t('webhooks.signingKeyHint')}
      >
        <TextInput
          value={secretKey}
          placeholder="Tracon:WebhookSecrets:order-service"
          data-testid="webhook-secret-key"
          onChange={(event) => setSecretKey(event.target.value)}
        />
      </Field>

      <Field label={t('webhooks.events')} required>
        <div className="flex flex-wrap gap-1.5">
          {EVENTS.map((event) => {
            const selected = events.includes(event);

            return (
              <button
                key={event}
                type="button"
                onClick={() =>
                  setEvents((current) =>
                    current.includes(event)
                      ? current.filter((value) => value !== event)
                      : [...current, event],
                  )
                }
                className={
                  selected
                    ? 'rounded border border-transparent bg-accent-soft px-1.5 py-0.5 text-[11px] font-medium text-accent'
                    : 'rounded border border-line bg-raised px-1.5 py-0.5 text-[11px] font-medium text-muted'
                }
              >
                {event}
              </button>
            );
          })}
        </div>
      </Field>

      {save.isError && <ErrorNote error={save.error} />}

      <div className="flex items-center gap-2">
        <Button
          tone="primary"
          disabled={save.isPending || name.trim() === '' || url.trim() === ''}
          testId="webhook-save"
          onClick={() => save.mutate()}
        >
          {save.isPending ? t('common.saving') : t('webhooks.save')}
        </Button>
        <Button tone="ghost" onClick={onDone}>
          {t('common.cancel')}
        </Button>
      </div>
    </div>
  );
}
