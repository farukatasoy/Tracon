import { useState, type ReactNode } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { TraconError, client, unwrap } from '../lib/api';
import { useT } from '../lib/i18n';
import { Link } from '../lib/router';
import { Button, ErrorNote, Mono, Panel, Select, TextInput } from './ui';
import type { ReplayToolMode } from '@tracon/client';
import type { AgentDefinition, RunInputResponse, RunReplayResponse } from '../lib/server-types';

const TOOL_MODES: ReplayToolMode[] = ['ReplayTools', 'NoTools', 'LiveTools'];

/**
 * Replays a finished run with its recorded input under changed conditions
 * (F-54, phase 47).
 *
 * The panel hides itself when the run has no recorded input: a run started
 * while input recording was off — or whose input retention already removed —
 * cannot be replayed, and offering a button that always fails is worse than
 * offering none.
 */
export function ReplayPanel({
  runId,
  agentName,
}: {
  runId: string;
  agentName: string;
}): ReactNode {
  const t = useT();
  const [toolMode, setToolMode] = useState<ReplayToolMode>('ReplayTools');
  const [version, setVersion] = useState('');
  const [modelId, setModelId] = useState('');

  // A 404 here is a normal outcome, not a failure — see the doc comment.
  const input = useQuery({
    queryKey: ['run-input', runId],
    queryFn: () =>
      unwrap(
        client.GET('/api/runs/{runId}/input', { params: { path: { runId } } }),
      ) as Promise<RunInputResponse>,
    retry: (_, error) => !(error instanceof TraconError && error.status === 404),
  });

  const versions = useQuery({
    queryKey: ['agent-versions', agentName],
    queryFn: () =>
      unwrap(
        client.GET('/api/agents/{name}/versions', { params: { path: { name: agentName } } }),
      ) as Promise<AgentDefinition[]>,
    // Code agents keep no version history; a 404 simply leaves the selector
    // showing "today's version" only.
    retry: (_, error) => !(error instanceof TraconError && error.status === 404),
  });

  const replay = useMutation<RunReplayResponse>({
    mutationFn: () =>
      unwrap(
        client.POST('/api/runs/{runId}/replay', {
          params: { path: { runId } },
          body: {
            agentVersion: version === '' ? null : Number(version),
            modelId: modelId.trim() === '' ? null : modelId.trim(),
            toolMode,
          },
        }),
      ) as Promise<RunReplayResponse>,
  });

  if (input.isError) {
    return null;
  }

  const list: AgentDefinition[] = versions.data ?? [];

  return (
    <Panel title={t('replay.title')}>
      <div className="flex flex-col gap-3 p-4">
        <p className="text-[11px] text-subtle">{t('replay.hint')}</p>

        <div className="grid gap-3 sm:grid-cols-3">
          <label className="flex flex-col gap-1 text-[11px] text-muted">
            {t('replay.toolMode')}
            <Select
              value={toolMode}
              onChange={(value) => setToolMode(value as ReplayToolMode)}
              disabled={replay.isPending}
              testId="replay-tool-mode"
            >
              {TOOL_MODES.map((mode) => (
                <option key={mode} value={mode}>
                  {t(`replay.toolMode.${mode}`)}
                </option>
              ))}
            </Select>
          </label>

          <label className="flex flex-col gap-1 text-[11px] text-muted">
            {t('replay.version')}
            <Select
              value={version}
              onChange={setVersion}
              disabled={replay.isPending || list.length === 0}
              testId="replay-version"
            >
              <option value="">{t('replay.versionCurrent')}</option>
              {list.map((definition) => (
                <option key={definition.version} value={String(definition.version)}>
                  v{definition.version}
                </option>
              ))}
            </Select>
          </label>

          <label className="flex flex-col gap-1 text-[11px] text-muted">
            {t('replay.model')}
            <TextInput
              value={modelId}
              placeholder={t('replay.modelDefault')}
              disabled={replay.isPending}
              onChange={(event) => setModelId(event.target.value)}
              data-testid="replay-model"
            />
          </label>
        </div>

        <p className="text-[11px] text-subtle">{t(`replay.toolModeHint.${toolMode}`)}</p>

        <div>
          <Button
            tone="primary"
            onClick={() => replay.mutate()}
            busy={replay.isPending}
            testId="replay-run"
          >
            {replay.isPending ? t('replay.running') : t('replay.button')}
          </Button>
        </div>

        {/*
          The server's 422/409/403 explanations are shown verbatim and are not
          translated (K-232): they name a tool and its arguments, and a
          translated copy would drift from the server's own wording.
        */}
        {replay.isError && <ErrorNote error={replay.error} />}

        {replay.data != null && (
          <div className="flex flex-col gap-1 text-[13px]" data-testid="replay-result">
            <span>
              {t('replay.result', { runId: '' })}{' '}
              <Link
                to={`runs/${encodeURIComponent(replay.data.runId)}`}
                className="text-accent underline"
              >
                <Mono>{replay.data.runId}</Mono>
              </Link>
            </span>
          </div>
        )}
      </div>
    </Panel>
  );
}
