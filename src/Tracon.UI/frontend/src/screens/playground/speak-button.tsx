import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { client, unwrap } from '../../lib/api';
import { useT } from '../../lib/i18n';
import { count } from '../../lib/format';
import type { SpeakResponse } from '../../lib/server-types';
import { SpeakerIcon, SpinnerIcon } from '../../components/icons';

/**
 * Speaks an assistant reply and plays it inline.
 *
 * The audio element is fed an object URL, not the attachment endpoint: browsers
 * do not attach the bearer token to resource loads, so a plain
 * `<audio src="api/attachments/{id}">` answers 401 whenever token auth is on.
 * Same reason as the image preview in `attachment-chip.tsx`.
 *
 * This is an operator action and runs outside an agent run, so its cost is not
 * written to `tool_invocations`; the endpoint returns the measured characters
 * and it is shown next to the player.
 */
export function SpeakButton({ text, sessionId }: { text: string; sessionId: string | null }): ReactNode {
  const t = useT();
  const [state, setState] = useState<'idle' | 'working' | 'ready' | 'failed'>('idle');
  const [url, setUrl] = useState<string | null>(null);
  const [note, setNote] = useState<string | null>(null);

  // The object URL owns memory until it is revoked.
  useEffect(
    () => () => {
      if (url !== null) {
        URL.revokeObjectURL(url);
      }
    },
    [url],
  );

  const speak = useCallback(async () => {
    setState('working');
    setNote(null);

    try {
      const result = (await unwrap(client.POST('/api/voice/speak', { body: { text, sessionId } }))) as SpeakResponse;
      const blob = (await unwrap(
        client.GET('/api/attachments/{id}', {
          params: { path: { id: result.attachment.id } },
          parseAs: 'blob',
        }),
      )) as Blob;

      setUrl(URL.createObjectURL(blob));
      setState('ready');
      setNote(
        result.cost != null
          ? t('playground.speechCost', {
              characters: count(result.characters),
              cost: result.cost.toFixed(4),
              currency: result.currency ?? '',
            }).trim()
          : t(result.isEstimated ? 'playground.speechCharsEstimated' : 'playground.speechChars', {
              characters: count(result.characters),
            }),
      );
    } catch (error) {
      setState('failed');
      setNote(error instanceof Error ? error.message : t('playground.speechFailed'));
    }
  }, [sessionId, t, text]);

  if (state === 'ready' && url !== null) {
    return (
      <span className="flex items-center gap-2">
        <audio data-testid="playground-audio" src={url} controls className="h-7 max-w-[16rem]" />
        {note !== null && <span className="text-xs text-subtle">{note}</span>}
      </span>
    );
  }

  return (
    <span className="flex items-center gap-2">
      <button
        type="button"
        data-testid="playground-speak"
        onClick={() => void speak()}
        disabled={state === 'working'}
        title={t('playground.speakTitle')}
        className="inline-flex items-center gap-1 rounded-md border border-line px-1.5 py-0.5 text-xs text-subtle hover:text-fg disabled:opacity-50"
      >
        {state === 'working' ? <SpinnerIcon className="size-3" /> : <SpeakerIcon className="size-3" />}
        {t('playground.speak')}
      </button>
      {state === 'failed' && note !== null && <span className="text-xs text-danger">{note}</span>}
    </span>
  );
}
