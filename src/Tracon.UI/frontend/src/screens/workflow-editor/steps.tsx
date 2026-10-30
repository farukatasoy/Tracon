import type { ReactNode } from 'react';
import type { WorkflowFunctionResponse } from '@tracon/client';
import { AgentVersionSelect } from '../../components/agent-version-select';
import { Badge, ErrorNote, Field, Select, TextArea, TextInput, cx, type FieldIds } from '../../components/ui';
import { Tooltip } from '../../components/tooltip';
import { useT } from '../../lib/i18n';
import {
  describeSchema,
  hasFunction,
  hasPins,
  parseSettings,
  parseTimeout,
  timeoutText,
  type Step,
} from '../../lib/workflow-steps';

/**
 * The functions the editor may offer, as the screen's query reports them.
 *
 * Passed only for a `Sequential` chain - the one pattern a function node can run in. The
 * three states are the query's: `items` once loaded, `error` when it failed, neither while it
 * loads (nothing is offered yet).
 */
export interface FunctionCatalog {
  items: readonly WorkflowFunctionResponse[] | undefined;
  error: unknown;
  retry: () => void;
}

/**
 * The ordered steps of a workflow: add, move, remove - and, for a function step, its settings.
 *
 * One list for agents and functions. An agent step is a name - and, for a stored agent, the version
 * it is pinned to; a function step also holds the JSON text of its node's settings. Neither kind of
 * step is validated here: the rules live in `WorkflowDefinitionValidator` and the function
 * registry, and a copy in the console would drift. The one exception is a settings text that is not
 * JSON at all, which the screen refuses to send.
 */
export function StepList({
  ids,
  steps,
  onChange,
  agents,
  storedAgents,
  functions,
}: {
  /**
   * The field bindings, put on the one real control this composite offers for ADDING an agent.
   * A `<div>` cannot carry `aria-invalid`, and the ordered list of chosen steps is output rather
   * than an input.
   */
  ids?: FieldIds;
  steps: readonly Step[];
  onChange: (steps: Step[]) => void;
  agents: readonly string[];
  /**
   * The agents that keep versions (stored in the database; one defined in code keeps none). Passed
   * only for a `Sequential` chain, the one pattern whose steps are nodes and so can carry a pin. A
   * step that already carries a pin keeps its selector whatever this says, so the operator can
   * clear it.
   */
  storedAgents?: ReadonlySet<string>;
  functions?: FunctionCatalog;
}): ReactNode {
  const t = useT();
  const used = new Set(steps.map((step) => step.name));
  const freeAgents = agents.filter((agent) => !used.has(agent));
  const freeFunctions = (functions?.items ?? []).filter((candidate) => !used.has(candidate.name));
  const catalog = functions?.items;
  const withFunction = hasFunction(steps);
  const withPin = hasPins(steps);

  const replace = (index: number, step: Step): void => {
    onChange(steps.map((existing, position) => (position === index ? step : existing)));
  };

  return (
    <div className="flex flex-col gap-2">
      <ol className="flex flex-col gap-1.5" data-testid="workflow-participants">
        {steps.map((step, index) => {
          const isFunction = step.kind === 'Function';
          const last = index === steps.length - 1;

          return (
            <li
              key={`${step.kind}:${step.name}`}
              data-testid="workflow-step"
              data-kind={step.kind}
              data-name={step.name}
              className="rounded-md border border-line bg-panel px-2.5 py-1.5 text-base"
            >
              <div className="flex items-center gap-2">
                <span className="w-5 text-xs text-subtle">{index + 1}</span>
                <span className="flex-1">{step.name}</span>

                {functions !== undefined && (
                  <Badge tone={isFunction ? 'info' : 'neutral'}>
                    {isFunction ? t('graph.legend.function') : t('graph.legend.agent')}
                  </Badge>
                )}

                <Tooltip text={t('workflowEditor.moveUp')}>
                  <button
                    type="button"
                    aria-label={
                      isFunction
                        ? t('workflowEditor.moveUpFunction', { name: step.name })
                        : t('workflowEditor.moveUpAgent', { agent: step.name })
                    }
                    disabled={index === 0}
                    className={cx('px-1 text-muted hover:text-fg', index === 0 && 'opacity-30')}
                    onClick={() => onChange(swap(steps, index, index - 1))}
                  >
                    ↑
                  </button>
                </Tooltip>
                <Tooltip text={t('workflowEditor.moveDown')}>
                  <button
                    type="button"
                    aria-label={
                      isFunction
                        ? t('workflowEditor.moveDownFunction', { name: step.name })
                        : t('workflowEditor.moveDownAgent', { agent: step.name })
                    }
                    disabled={last}
                    className={cx('px-1 text-muted hover:text-fg', last && 'opacity-30')}
                    onClick={() => onChange(swap(steps, index, index + 1))}
                  >
                    ↓
                  </button>
                </Tooltip>
                <button
                  type="button"
                  aria-label={
                    isFunction
                      ? t('workflowEditor.removeFunction', { name: step.name })
                      : t('workflowEditor.removeAgent', { agent: step.name })
                  }
                  className="px-1 text-muted hover:text-danger"
                  onClick={() => onChange(steps.filter((_, position) => position !== index))}
                >
                  ✕
                </button>
              </div>

              {isFunction && (
                <FunctionSettings
                  step={step}
                  descriptor={catalog?.find((candidate) => candidate.name === step.name)}
                  catalogLoaded={catalog !== undefined}
                  onChange={(settings) => replace(index, { ...step, settings })}
                />
              )}

              {step.kind === 'Agent' &&
                storedAgents !== undefined &&
                (storedAgents.has(step.name) || (step.agentVersion ?? null) !== null) && (
                  <AgentVersionSelect
                    name={step.name}
                    pinned={step.agentVersion ?? null}
                    label={t('workflowEditor.agentVersionOf', { name: step.name })}
                    hint={t('workflowEditor.agentVersionHint')}
                    testId={`workflow-version-${step.name}`}
                    onChange={(agentVersion) => replace(index, { ...step, agentVersion })}
                  />
                )}

              {/* A step's own timeout lives on a node: only a Sequential chain with a function
                  step or a pinned agent is saved as a node list. A step that
                  still carries one in an agent list keeps its field, so the operator can clear it. */}
              {functions !== undefined && (withFunction || withPin || step.timeout.trim().length > 0) && (
                <StepTimeout
                  step={step}
                  registered={isFunction ? catalog?.find((candidate) => candidate.name === step.name)?.timeout : undefined}
                  onChange={(timeout) => replace(index, { ...step, timeout })}
                />
              )}
            </li>
          );
        })}
      </ol>

      {freeAgents.length > 0 && (
        <Select
          {...(ids ?? {})}
          testId="workflow-add-agent"
          value=""
          onChange={(value) => {
            if (value.length > 0) {
              onChange([...steps, { kind: 'Agent', name: value, settings: '', timeout: '' }]);
            }
          }}
        >
          <option value="">{t('workflowEditor.addAgent')}</option>
          {freeAgents.map((agent) => (
            <option key={agent} value={agent}>
              {agent}
            </option>
          ))}
        </Select>
      )}

      {functions !== undefined &&
        (functions.error !== null && functions.error !== undefined ? (
          <div className="flex flex-col gap-1">
            <p className="text-xs text-muted">{t('workflowEditor.functionsFailed')}</p>
            <ErrorNote error={functions.error} onRetry={functions.retry} />
          </div>
        ) : (
          freeFunctions.length > 0 && (
            <Select
              ariaLabel={t('workflowEditor.addFunction')}
              testId="workflow-add-function"
              value=""
              onChange={(value) => {
                if (value.length > 0) {
                  onChange([...steps, { kind: 'Function', name: value, settings: '', timeout: '' }]);
                }
              }}
            >
              <option value="">{t('workflowEditor.addFunction')}</option>
              {freeFunctions.map((candidate) => (
                <option key={candidate.name} value={candidate.name}>
                  {candidate.name}
                </option>
              ))}
            </Select>
          )
        ))}
    </div>
  );
}

/**
 * The settings of one function step: a JSON text field with the function's fields listed under it.
 *
 * The server's schema is read for display only (`describeSchema`); the answer to "is this valid"
 * is the save endpoint's, which reads the object into the function's settings type and names the
 * node and the member. The one local rule is that the text must be JSON.
 */
function FunctionSettings({
  step,
  descriptor,
  catalogLoaded,
  onChange,
}: {
  step: Step;
  descriptor: WorkflowFunctionResponse | undefined;
  catalogLoaded: boolean;
  onChange: (settings: string) => void;
}): ReactNode {
  const t = useT();

  // The catalog answered and this name is not in it: the function was removed from the code,
  // or the definition was written by hand. Say so at the step, not only in the 400 at the end.
  if (catalogLoaded && descriptor === undefined) {
    return (
      <p className="mt-1.5 text-xs text-danger" role="alert" data-testid="workflow-function-missing">
        {t('workflowEditor.functionMissing')}
      </p>
    );
  }

  const takesNone = descriptor !== undefined && (descriptor.settingsSchema === null || descriptor.settingsSchema === undefined);
  const hasText = step.settings.trim().length > 0;

  // A function that takes none gets no field - unless the step already carries some, from a
  // time the function did take them. Then the field stays, so the operator can clear it.
  if (takesNone && !hasText) {
    return <p className="mt-1.5 text-xs text-subtle">{t('workflowEditor.settingsNone')}</p>;
  }

  const fields = describeSchema(descriptor?.settingsSchema);
  const parsed = parseSettings(step.settings);
  const error = takesNone
    ? t('workflowEditor.settingsNone')
    : parsed.ok
      ? undefined
      : t('workflowEditor.settingsInvalid');

  return (
    <div className="mt-1.5">
      <Field
        label={t('workflowEditor.settingsOf', { name: step.name })}
        error={error}
        hint={
          <>
            <span className="block">{t('workflowEditor.settingsHint')}</span>
            {fields !== null && fields.length > 0 && (
              <span className="mt-1 block" data-testid={`workflow-settings-fields-${step.name}`}>
                {t('workflowEditor.settingsFields')}:{' '}
                {fields.map((field, position) => (
                  <span key={field.name}>
                    {position > 0 && '; '}
                    <code className="font-mono text-fg">{field.name}</code> ({field.type}
                    {field.required && `, ${t('workflowEditor.settingsRequired')}`})
                  </span>
                ))}
              </span>
            )}
          </>
        }
      >
        {(ids) => (
          <TextArea
            {...ids}
            data-testid={`workflow-settings-${step.name}`}
            rows={5}
            spellCheck={false}
            value={step.settings}
            onChange={(event) => onChange(event.target.value)}
          />
        )}
      </Field>
    </div>
  );
}

/**
 * The timeout of one step, in seconds.
 *
 * Empty means the step's default, which the placeholder names: the timeout its function was
 * registered with, or the installation's sub-agent deadline for an agent. Seconds and a
 * `hh:mm:ss` value are both read; whether the value is in range is the server's answer.
 */
function StepTimeout({
  step,
  registered,
  onChange,
}: {
  step: Step;
  registered: string | null | undefined;
  onChange: (timeout: string) => void;
}): ReactNode {
  const t = useT();
  const invalid = !parseTimeout(step.timeout).ok;
  const placeholder =
    step.kind === 'Agent'
      ? t('workflowEditor.timeoutAgentDefault')
      : registered === null || registered === undefined
        ? t('workflowEditor.timeoutNone')
        : t('workflowEditor.timeoutRegistered', { seconds: timeoutText(registered) });

  return (
    <div className="mt-1.5 max-w-xs">
      <Field
        label={t('workflowEditor.timeoutOf', { name: step.name })}
        hint={step.kind === 'Agent' ? t('workflowEditor.timeoutAgentHint') : t('workflowEditor.timeoutFunctionHint')}
        error={invalid ? t('workflowEditor.timeoutInvalid') : undefined}
      >
        {(ids) => (
          <TextInput
            {...ids}
            data-testid={`workflow-timeout-${step.name}`}
            inputMode="decimal"
            placeholder={placeholder}
            value={step.timeout}
            onChange={(event) => onChange(event.target.value)}
          />
        )}
      </Field>
    </div>
  );
}

function swap(steps: readonly Step[], from: number, to: number): Step[] {
  const next = [...steps];
  const moved = next[from];
  const replaced = next[to];

  if (moved === undefined || replaced === undefined) {
    return next;
  }

  next[from] = replaced;
  next[to] = moved;

  return next;
}
