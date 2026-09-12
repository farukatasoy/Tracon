import { useState, type ChangeEvent, type DragEvent, type ReactNode } from 'react';
import { useNavigate } from '../lib/router';
import { useT } from '../lib/i18n';
import { Link } from '../lib/router';
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
  TextInput,
} from '../components/ui';
import { MicIcon, PaperclipIcon, PlusIcon, SendIcon, SpinnerIcon } from '../components/icons';
import { TranscriptView } from '../components/transcript';
import { VoicePanel } from '../components/voice-panel';
import { BranchButton } from '../components/branch-button';
import { shortId } from '../lib/format';
import { AttachmentChip } from './playground/attachment-chip';
import { TurnView } from './playground/turn-view';
import { useAttachments } from './playground/use-attachments';
import { usePlaygroundRun } from './playground/use-playground-run';

/**
 * Streaming chat against a single agent.
 *
 * The stream comes from `POST api/agents/{name}/run` as Server-Sent Events. The
 * first frame is `run` and carries the run id, so the turn can link straight to
 * its recorded run while the answer is still arriving.
 *
 * This screen is a route facade only: `usePlaygroundRun` owns the run/session
 * lifecycle and `useAttachments` owns the pending-upload lifecycle. Everything
 * below composes their state into markup.
 */
export function PlaygroundScreen({ name }: { name?: string }): ReactNode {
  const t = useT();
  const navigate = useNavigate();

  const [sessionId, setSessionId] = useState<string | null>(null);
  const attachments = useAttachments(sessionId);
  const play = usePlaygroundRun(name, sessionId, setSessionId, attachments);

  if (play.agents.isPending) {
    return <Loading />;
  }

  if (play.agents.isError) {
    return <ErrorNote error={play.agents.error} />;
  }

  if (play.agents.data.length === 0) {
    return (
      <>
        <PageHeader title={t('nav.playground')} />
        <Panel>
          <Empty title={t('playground.noAgents.title')}>
            {t('playground.noAgents.body')}{' '}
            <Link to="agents" className="text-accent underline">
              {t('nav.agents')}
            </Link>
            .
          </Empty>
        </Panel>
      </>
    );
  }

  return (
    <>
      <PageHeader
        title={t('nav.playground')}
        description={t('playground.description')}
        actions={
          <>
            <Select
              value={play.selected}
              onChange={(value) => {
                play.reset();
                navigate(`playground/${encodeURIComponent(value)}`);
              }}
            >
              {play.agents.data.map((agent) => (
                <option key={agent.name} value={agent.name}>
                  {agent.displayName ?? agent.name}
                </option>
              ))}
            </Select>
            <Button onClick={play.reset} disabled={play.turns.length === 0 && play.sessionId === null}>
              <PlusIcon className="size-3.5" />
              {t('playground.newChat')}
            </Button>
          </>
        }
      />

      {play.sessionId !== null && (
        <p className="mb-3 text-sm text-subtle">
          {t('common.session')}{' '}
          <Link to={`sessions/${encodeURIComponent(play.sessionId)}`} className="text-accent underline">
            <Mono>{shortId(play.sessionId, 14, 6)}</Mono>
          </Link>{' '}
          — {t('playground.historyCarried')}
          {/*
            The playground branches the WHOLE conversation: its transcript is
            folded from a live SSE stream and carries no `seq`. Branching at one
            message needs the exact sequence number and lives on the session
            screen, where the history arrives in sequence order.
          */}
          <span className="ml-2 inline-block align-middle">
            <BranchButton sessionId={play.sessionId} />
          </span>
        </p>
      )}

      {play.error !== null && <div className="mb-3"><ErrorNote error={play.error} /></div>}

      <Panel className="flex min-h-[26rem] flex-col">
        <div className="flex-1 overflow-y-auto p-4">
          {play.history !== null && play.history.length > 0 && (
            <div className="mb-6 flex flex-col divide-y divide-line border-b border-line pb-4">
              <p className="pb-2 text-xs font-medium text-subtle uppercase">{t('playground.priorMessages')}</p>
              {play.history.map(({ message, folded }, index) => {
                const role = ((message.role as string | undefined) ?? 'unknown').toLowerCase();

                return (
                  <div key={message.messageId ?? index} className="pt-3">
                    <div className="mb-1.5 flex items-center gap-2">
                      <Badge tone={role === 'user' ? 'accent' : role === 'system' ? 'warn' : 'neutral'}>{role}</Badge>
                      {message.authorName != null && (
                        <span className="text-xs text-subtle">{message.authorName}</span>
                      )}
                    </div>
                    {folded.items.length === 0 ? (
                      <p className="text-sm text-subtle">{t('sessionDetail.noContent')}</p>
                    ) : (
                      <TranscriptView items={folded.items} />
                    )}
                  </div>
                );
              })}
            </div>
          )}

          {play.turns.length === 0 ? (
            (play.history === null || play.history.length === 0) && (
              <Empty title={t('playground.empty.title')}>{t('playground.empty.body')}</Empty>
            )
          ) : (
            <div className="flex flex-col gap-6">
              {play.turns.map((turn) => (
                <TurnView key={turn.id} turn={turn} onDecide={play.decide} sessionId={play.sessionId} />
              ))}
            </div>
          )}
          <div ref={play.bottomRef} />
        </div>

        <form
          className="flex flex-col gap-2 border-t border-line p-3"
          onSubmit={(event) => {
            event.preventDefault();
            play.send();
          }}
          onDragOver={(event: DragEvent<HTMLFormElement>) => event.preventDefault()}
          onDrop={(event: DragEvent<HTMLFormElement>) => {
            event.preventDefault();

            if (event.dataTransfer.files.length > 0) {
              void attachments.upload(event.dataTransfer.files);
            }
          }}
        >
          {attachments.error !== null && <ErrorNote error={attachments.error} />}

          {play.parameterSchema.length > 0 && (
            <div data-testid="playground-parameters" className="flex flex-col gap-2">
              <p className="text-xs font-medium text-subtle uppercase">{t('playground.parameters')}</p>
              <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
                {play.parameterSchema.map((parameter) => (
                  <Field key={parameter.name} label={parameter.name} required={parameter.required} hint={parameter.description}>
                    {parameter.kind === 'Boolean' ? (
                      <input
                        type="checkbox"
                        checked={(play.paramValues[parameter.name] ?? parameter.defaultValue ?? 'false') === 'true'}
                        onChange={(event) =>
                          play.setParamValues((current) => ({
                            ...current,
                            [parameter.name]: event.target.checked ? 'true' : 'false',
                          }))
                        }
                        className="size-4"
                      />
                    ) : (
                      <TextInput
                        type={parameter.kind === 'Number' ? 'number' : 'text'}
                        value={play.paramValues[parameter.name] ?? parameter.defaultValue ?? ''}
                        placeholder={parameter.defaultValue ?? undefined}
                        onChange={(event) =>
                          play.setParamValues((current) => ({ ...current, [parameter.name]: event.target.value }))
                        }
                      />
                    )}
                  </Field>
                ))}
              </div>
            </div>
          )}

          {attachments.pending.length > 0 && (
            <div className="flex flex-wrap gap-1.5">
              {attachments.pending.map((attachment) => (
                <AttachmentChip
                  key={attachment.id}
                  attachment={attachment}
                  onRemove={() => attachments.remove(attachment.id)}
                />
              ))}
            </div>
          )}

          <div className="flex items-end gap-2">
            <input
              ref={attachments.fileInputRef}
              type="file"
              multiple
              data-testid="attachment-input"
              className="hidden"
              onChange={(event: ChangeEvent<HTMLInputElement>) => {
                if (event.target.files !== null && event.target.files.length > 0) {
                  void attachments.upload(event.target.files);
                }

                event.target.value = '';
              }}
            />
            <Button
              type="button"
              tone="default"
              disabled={play.busy || attachments.uploading}
              onClick={() => attachments.fileInputRef.current?.click()}
              title={t('playground.attachFile')}
            >
              {attachments.uploading ? <SpinnerIcon className="size-3.5" /> : <PaperclipIcon className="size-3.5" />}
            </Button>
            <textarea
              rows={1}
              value={play.prompt}
              disabled={play.busy}
              data-testid="playground-input"
              placeholder={t('playground.placeholder')}
              className="max-h-40 min-h-9 flex-1 resize-y rounded-md border border-line bg-panel px-3 py-1.5 text-base placeholder:text-subtle focus:border-accent focus:outline-none disabled:opacity-60"
              onChange={(event) => play.setPrompt(event.target.value)}
              onKeyDown={(event) => {
                // Enter sends, Shift+Enter adds a line. Ctrl/Cmd+Enter sends too,
                // so the habit from every other console works here as well. The
                // binding lives on the textarea because submitting belongs to the
                // form that holds the caret, not to a global handler.
                if (event.key === 'Enter' && (!event.shiftKey || event.ctrlKey || event.metaKey)) {
                  event.preventDefault();
                  play.send();
                }
              }}
            />
            <Button
              type="button"
              tone={play.conversation ? 'primary' : 'default'}
              disabled={play.busy}
              testId="voice-mode"
              title={t('playground.conversationMode')}
              onClick={() => void play.openConversation()}
            >
              <MicIcon className="size-3.5" />
            </Button>
            {play.busy ? (
              <Button tone="default" onClick={play.abortRun}>
                {t('workflowDetail.stop')}
              </Button>
            ) : (
              <Button
                type="submit"
                tone="primary"
                testId="playground-send"
                disabled={(play.prompt.trim().length === 0 && attachments.pending.length === 0) || play.missingRequiredParameter}
              >
                <SendIcon className="size-3.5" />
                {t('playground.send')}
              </Button>
            )}
          </div>
        </form>

        {play.conversation && play.sessionId !== null && <VoicePanel agent={play.selected} sessionId={play.sessionId} />}
      </Panel>
    </>
  );
}
