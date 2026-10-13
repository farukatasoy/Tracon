import type { Dispatch, ReactNode, SetStateAction } from 'react';
import type { LoopCriterion, LoopCriterionCatalogResponse, LoopOutcome, LoopSettings } from '@tracon/client';
import { useT } from '../../../lib/i18n';
import { Badge, Button, ErrorNote, Field, Mono, Panel, Select, TextArea, TextInput, cx } from '../../../components/ui';
import { Tooltip } from '../../../components/tooltip';
import { toNumber, type FormState } from '../model';

/** The kinds the server builds itself; every other kind is registered in code. */
export const BUILT_IN_LOOP_KINDS = ['completionMarker', 'todoCompletion', 'aiJudge', 'backgroundTaskCompletion'] as const;
type BuiltInLoopKind = (typeof BUILT_IN_LOOP_KINDS)[number];

const OUTCOMES: LoopOutcome[] = ['ReturnLastResponse', 'FailRun'];

/** The built-in spelling of `kind`, or null. The server matches built-in names case-insensitively. */
export function builtInKind(kind: string): BuiltInLoopKind | null {
  return BUILT_IN_LOOP_KINDS.find((builtIn) => builtIn.toLowerCase() === kind.toLowerCase()) ?? null;
}

/**
 * The criterion with its kind replaced and every field the NEW kind does not
 * read cleared (open question 6-A). A field the new kind reads keeps its value.
 *
 * Only a kind change clears: loading and saving keep every field as it is, so a
 * definition written over the API with a field its kind ignores survives a
 * console save unchanged.
 */
export function withKind(criterion: LoopCriterion, kind: string): LoopCriterion {
  const builtIn = builtInKind(kind);

  return {
    kind,
    marker: builtIn === 'completionMarker' ? (criterion.marker ?? null) : null,
    judgeCriteria: builtIn === 'aiJudge' ? (criterion.judgeCriteria ?? []) : [],
    judgeInstructions: builtIn === 'aiJudge' ? (criterion.judgeInstructions ?? null) : null,
    modes: builtIn === 'todoCompletion' ? (criterion.modes ?? []) : [],
  };
}

/** A loop turned on from the console: one empty marker criterion and every setting at its default. */
export function newLoop(): LoopSettings {
  return {
    criteria: [{ kind: 'completionMarker', marker: null, judgeCriteria: [], judgeInstructions: null, modes: [] }],
    maxIterations: null,
    freshContextPerIteration: false,
    onExhausted: 'ReturnLastResponse',
    onCriterionError: 'ReturnLastResponse',
  };
}

function swap<T>(items: readonly T[], from: number, to: number): T[] {
  const next = [...items];
  const moved = next[from] as T;

  next[from] = next[to] as T;
  next[to] = moved;

  return next;
}

/**
 * The definition's harness loop (F-302): its criteria, ceiling and outcomes.
 *
 * Rendered only while harness features are on; the loop lives in
 * `form.harness.loop` and travels through `toRequest`/`fromDefinition` like the
 * rest of the harness. The kind list comes from `GET /api/loop-criteria`, and a
 * kind the list does not name — or every non-built-in kind, when the list could
 * not load — stays in the definition as it was written.
 */
export function LoopSection({
  form,
  setForm,
  catalog,
}: {
  form: FormState;
  setForm: Dispatch<SetStateAction<FormState>>;
  catalog: {
    data: LoopCriterionCatalogResponse | undefined;
    isError: boolean;
    error: unknown;
    refetch: () => unknown;
  };
}): ReactNode {
  const t = useT();
  const loop = form.harness.loop ?? null;

  const setLoop = (next: LoopSettings | null): void => {
    setForm({ ...form, harness: { ...form.harness, loop: next } });
  };

  const codeKinds = (catalog.data?.kinds ?? []).filter((kind) => !kind.builtIn);
  const judge = catalog.data?.judgeModel ?? null;

  return (
    <Panel title={t('agentEditor.loop.title')}>
      <div className="p-4" data-testid="loop-section">
        <label className="flex cursor-pointer items-center gap-2 text-base">
          <input
            type="checkbox"
            className="accent-[var(--tracon-accent)]"
            data-testid="loop-enabled"
            checked={loop !== null}
            onChange={(event) => setLoop(event.target.checked ? newLoop() : null)}
          />
          {t('agentEditor.loop.enable')}
        </label>
        <p className="mt-1 text-sm text-muted">{t('agentEditor.loop.notice')}</p>

        {loop !== null && (
          <div className="mt-4 flex flex-col gap-4">
            {catalog.isError && (
              <div>
                <p className="mb-2 text-sm text-muted">{t('agentEditor.loop.catalogError')}</p>
                <ErrorNote error={catalog.error} onRetry={() => void catalog.refetch()} />
              </div>
            )}

            <p className="text-sm text-subtle">{t('agentEditor.loop.firstWins')}</p>

            {loop.criteria.length === 0 && (
              <p className="text-sm text-subtle">{t('agentEditor.loop.noCriteria')}</p>
            )}

            <ol className="flex flex-col gap-2">
              {loop.criteria.map((criterion, index) => (
                <CriterionRow
                  // The position is the identity: two criteria may share a kind.
                  key={index}
                  index={index}
                  count={loop.criteria.length}
                  criterion={criterion}
                  codeKinds={codeKinds}
                  catalogLoaded={catalog.data !== undefined}
                  judge={judge}
                  onChange={(next) =>
                    setLoop({ ...loop, criteria: loop.criteria.map((item, position) => (position === index ? next : item)) })
                  }
                  onMove={(to) => setLoop({ ...loop, criteria: swap(loop.criteria, index, to) })}
                  onRemove={() => setLoop({ ...loop, criteria: loop.criteria.filter((_, position) => position !== index) })}
                />
              ))}
            </ol>

            <div>
              <Button
                testId="loop-add-criterion"
                onClick={() => setLoop({ ...loop, criteria: [...loop.criteria, ...newLoop().criteria] })}
              >
                {t('agentEditor.loop.addCriterion')}
              </Button>
            </div>

            <div className="grid gap-4 sm:grid-cols-2">
              <Field label={t('agentEditor.loop.maxIterations')} hint={t('agentEditor.loop.maxIterationsHint')}>
                <TextInput
                  inputMode="numeric"
                  data-testid="loop-max-iterations"
                  value={loop.maxIterations?.toString() ?? ''}
                  placeholder="10"
                  onChange={(event) => setLoop({ ...loop, maxIterations: toNumber(event.target.value) })}
                />
              </Field>
              <label className="flex cursor-pointer items-center gap-2 self-center text-sm">
                <input
                  type="checkbox"
                  className="accent-[var(--tracon-accent)]"
                  data-testid="loop-fresh-context"
                  checked={loop.freshContextPerIteration === true}
                  onChange={(event) => setLoop({ ...loop, freshContextPerIteration: event.target.checked })}
                />
                {t('agentEditor.loop.freshContext')}
              </label>
              <Field label={t('agentEditor.loop.onExhausted')} hint={t('agentEditor.loop.failRunHint')}>
                {(ids) => (
                  <Select
                    {...ids}
                    testId="loop-on-exhausted"
                    value={loop.onExhausted ?? 'ReturnLastResponse'}
                    onChange={(value) => setLoop({ ...loop, onExhausted: value as LoopOutcome })}
                  >
                    {OUTCOMES.map((outcome) => (
                      <option key={outcome} value={outcome}>
                        {t(`agentEditor.loop.outcome.${outcome}`)}
                      </option>
                    ))}
                  </Select>
                )}
              </Field>
              <Field label={t('agentEditor.loop.onCriterionError')}>
                {(ids) => (
                  <Select
                    {...ids}
                    testId="loop-on-criterion-error"
                    value={loop.onCriterionError ?? 'ReturnLastResponse'}
                    onChange={(value) => setLoop({ ...loop, onCriterionError: value as LoopOutcome })}
                  >
                    {OUTCOMES.map((outcome) => (
                      <option key={outcome} value={outcome}>
                        {t(`agentEditor.loop.outcome.${outcome}`)}
                      </option>
                    ))}
                  </Select>
                )}
              </Field>
            </div>
          </div>
        )}
      </div>
    </Panel>
  );
}

function CriterionRow({
  index,
  count,
  criterion,
  codeKinds,
  catalogLoaded,
  judge,
  onChange,
  onMove,
  onRemove,
}: {
  index: number;
  count: number;
  criterion: LoopCriterion;
  codeKinds: LoopCriterionCatalogResponse['kinds'];
  catalogLoaded: boolean;
  judge: LoopCriterionCatalogResponse['judgeModel'];
  onChange: (criterion: LoopCriterion) => void;
  onMove: (to: number) => void;
  onRemove: () => void;
}): ReactNode {
  const t = useT();
  const position = index + 1;
  const builtIn = builtInKind(criterion.kind);
  const codeKind = codeKinds.find((kind) => kind.kind.toLowerCase() === criterion.kind.toLowerCase());

  // A kind nobody lists is kept, never dropped: it may be registered on the host
  // that runs the agent, or the list may simply have failed to load.
  const listed = builtIn !== null || codeKind !== undefined;
  const listedOptions: string[] = [...BUILT_IN_LOOP_KINDS, ...codeKinds.map((kind) => kind.kind)];

  // The server matches kinds case-insensitively, so "aijudge" is a valid aiJudge.
  // The spelling as written stays an option of its own: without it the select
  // would show its first option, and choosing the kind it really has would fire
  // no change. A save keeps the spelling until the operator picks another one.
  const options = listedOptions.includes(criterion.kind) ? listedOptions : [...listedOptions, criterion.kind];

  return (
    <li
      data-testid={`loop-criterion-${index}`}
      data-kind={criterion.kind}
      className="rounded-md border border-line bg-panel px-3 py-2"
    >
      <div className="flex flex-wrap items-center gap-2">
        <span className="w-5 text-xs text-subtle">{position}</span>
        <div className="min-w-0 flex-1">
          <Select
            testId={`loop-criterion-kind-${index}`}
            ariaLabel={t('agentEditor.loop.kindOf', { index: position })}
            value={criterion.kind}
            onChange={(value) => onChange(withKind(criterion, value))}
          >
            {options.map((kind) => (
              <option key={kind} value={kind}>
                {kind}
              </option>
            ))}
          </Select>
        </div>

        {!listed && catalogLoaded && (
          <Badge tone="warn" description={t('agentEditor.loop.unregisteredHint')}>
            {t('agentEditor.loop.unregistered')}
          </Badge>
        )}

        <Tooltip text={t('agentEditor.loop.moveUpHint')}>
          <button
            type="button"
            aria-label={t('agentEditor.loop.moveUp', { index: position })}
            data-testid={`loop-criterion-up-${index}`}
            disabled={index === 0}
            className={cx('px-1 text-muted hover:text-fg', index === 0 && 'opacity-30')}
            onClick={() => onMove(index - 1)}
          >
            ↑
          </button>
        </Tooltip>
        <Tooltip text={t('agentEditor.loop.moveDownHint')}>
          <button
            type="button"
            aria-label={t('agentEditor.loop.moveDown', { index: position })}
            data-testid={`loop-criterion-down-${index}`}
            disabled={index === count - 1}
            className={cx('px-1 text-muted hover:text-fg', index === count - 1 && 'opacity-30')}
            onClick={() => onMove(index + 1)}
          >
            ↓
          </button>
        </Tooltip>
        <button
          type="button"
          aria-label={t('agentEditor.loop.remove', { index: position })}
          data-testid={`loop-criterion-remove-${index}`}
          className="px-1 text-muted hover:text-danger"
          onClick={onRemove}
        >
          ✕
        </button>
      </div>

      <div className="mt-2 flex flex-col gap-3 pl-7">
        {builtIn !== null && <p className="text-sm text-muted">{t(`agentEditor.loop.describe.${builtIn}`)}</p>}
        {builtIn === null && codeKind !== undefined && (
          <p className="text-sm text-muted" data-testid={`loop-criterion-description-${index}`}>
            {codeKind.description ?? t('agentEditor.loop.codeKind')}
            {codeKind.description != null && <span className="text-subtle"> {t('agentEditor.loop.codeKind')}</span>}
          </p>
        )}
        {/* Only once the list has loaded: while it loads or after it failed, a
            registered code kind is missing from it too, and the sentence would be false. */}
        {!listed && catalogLoaded && <p className="text-sm text-muted">{t('agentEditor.loop.unregisteredHint')}</p>}

        {builtIn === 'completionMarker' && (
          <Field label={t('agentEditor.loop.marker')} hint={t('agentEditor.loop.markerHint')}>
            <TextInput
              data-testid={`loop-criterion-marker-${index}`}
              value={criterion.marker ?? ''}
              placeholder="DONE"
              onChange={(event) => onChange({ ...criterion, marker: event.target.value })}
            />
          </Field>
        )}

        {builtIn === 'aiJudge' && (
          <>
            <Field label={t('agentEditor.loop.judgeCriteria')} hint={t('agentEditor.loop.judgeCriteriaHint')}>
              <TextArea
                rows={3}
                data-testid={`loop-criterion-judge-criteria-${index}`}
                value={(criterion.judgeCriteria ?? []).join('\n')}
                onChange={(event) => onChange({ ...criterion, judgeCriteria: event.target.value.split('\n') })}
                // Blank lines are dropped once the field is left: while typing, the
                // line being written is still empty.
                onBlur={() =>
                  onChange({
                    ...criterion,
                    judgeCriteria: (criterion.judgeCriteria ?? []).filter((entry) => entry.trim().length > 0),
                  })
                }
              />
            </Field>
            <Field label={t('agentEditor.loop.judgeInstructions')}>
              <TextArea
                rows={2}
                data-testid={`loop-criterion-judge-instructions-${index}`}
                value={criterion.judgeInstructions ?? ''}
                onChange={(event) =>
                  onChange({ ...criterion, judgeInstructions: event.target.value.length > 0 ? event.target.value : null })
                }
              />
            </Field>
            <p className="text-sm" data-testid={`loop-criterion-judge-${index}`}>
              {judge != null ? (
                <>
                  {t('agentEditor.loop.judgeModel')}{' '}
                  <Mono>
                    {judge.provider} / {judge.model}
                  </Mono>
                  <span className="text-subtle"> {t('agentEditor.loop.judgeCost')}</span>
                </>
              ) : catalogLoaded ? (
                <span className="text-danger">{t('agentEditor.loop.judgeUnconfigured')}</span>
              ) : null}
            </p>
          </>
        )}

        {builtIn === 'todoCompletion' && (criterion.modes ?? []).length > 0 && (
          <p className="text-sm text-subtle">
            {t('agentEditor.loop.modesKept', { modes: (criterion.modes ?? []).join(', ') })}
          </p>
        )}
      </div>
    </li>
  );
}
