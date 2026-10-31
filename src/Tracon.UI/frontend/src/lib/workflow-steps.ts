import type { WorkflowHandoff, WorkflowNodeReference } from '@tracon/client';
import type { WorkflowDefinition, WorkflowKind } from './server-types';

/**
 * The chain of a `Sequential` workflow - and the participant list of every other
 * pattern - as the editor holds it.
 *
 * One list for both server shapes. A definition stores its steps either as
 * `agentNames` (agents only, every pattern) or as `nodes` (agents and functions,
 * `Sequential` only); the editor reads either into `Step[]` and writes the
 * canonical one back (`chainBody`), so the screen never branches on which form it
 * loaded.
 */
export interface Step {
  kind: 'Agent' | 'Function';
  name: string;
  /**
   * What the operator typed: JSON text. Empty means the node carries no settings.
   * Agent steps never carry any (the server refuses them).
   */
  settings: string;
  /**
   * The node's own timeout as the operator typed it: seconds (`90`, `2.5`) or a
   * `hh:mm:ss` value. Empty means none - the function's registered timeout, or the
   * installation's `ChildDeadline` for an agent step.
   */
  timeout: string;
  /**
   * The timeout the server sent, kept so an untouched field goes back exactly as stored
   * The field shows seconds, and `00:00:02.5000000` would otherwise come back as
   * `00:00:02.500`.
   */
  storedTimeout?: string | null;
  /**
   * The version of the agent this step runs. Empty (`null` or absent) runs the agent's current
   * version, resolved each time the step runs. Only a stored agent keeps versions, and only a
   * node can carry the pin: a chain with one is saved as a node list (`canonicalForm`).
   *
   * 🚨 A pin the server sent goes back as it is, whatever the step looks like: the save is
   * a full replace, and dropping one silently would move the step to the current version.
   */
  agentVersion?: number | null;
}

/** Reads a definition's steps from whichever form it was stored in. */
export function stepsOf(definition: Pick<WorkflowDefinition, 'agentNames' | 'nodes'>): Step[] {
  if (definition.nodes.length > 0) {
    return definition.nodes.map((node) => ({
      kind: node.kind === 'Function' ? 'Function' : 'Agent',
      name: node.name,
      settings: settingsText(node.settings),
      timeout: timeoutText(node.timeout),
      storedTimeout: node.timeout ?? undefined,
      agentVersion: node.agentVersion ?? undefined,
    }));
  }

  return definition.agentNames.map((name) => ({ kind: 'Agent', name, settings: '', timeout: '' }));
}

/** Whether the chain has a function node. */
export function hasFunction(steps: readonly Step[]): boolean {
  return steps.some((step) => step.kind === 'Function');
}

/** Whether a step of the chain pins its agent to a version. */
export function hasPins(steps: readonly Step[]): boolean {
  return steps.some((step) => step.agentVersion !== null && step.agentVersion !== undefined);
}

/** Whether a step of the chain carries a timeout of its own. */
export function hasStepTimeouts(steps: readonly Step[]): boolean {
  return steps.some((step) => step.timeout.trim().length > 0);
}

/**
 * Whether a step carries a timeout that the form the chain is saved in cannot keep: a plain
 * agent list has nowhere to put one.
 *
 * 🚨 A step timeout does NOT turn an agent list into a node list:
 * the change of form would make a run waiting to be resumed unresumable and stop
 * the agents' live token stream. Such a chain loaded from a node list written over HTTP, or
 * one whose last function step was removed, is held back instead of losing its timeouts on a
 * full-replace save. A pinned agent version is the other thing that makes a node list
 * The operator asked for a node there, so a timeout beside it is kept.
 */
export function hasStrandedTimeouts(kind: WorkflowKind, steps: readonly Step[]): boolean {
  return canonicalForm(kind, steps) === 'agentNames' && hasStepTimeouts(steps);
}

/** The two ways a definition keeps its steps. */
export type StoredForm = 'agentNames' | 'nodes';

/** How a definition that was loaded from the server keeps its steps. */
export function storedFormOf(definition: Pick<WorkflowDefinition, 'nodes'>): StoredForm {
  return definition.nodes.length > 0 ? 'nodes' : 'agentNames';
}

/**
 * The form the console writes: `nodes` when a Sequential chain has a function or an agent step
 * pinned to a version, `agentNames` otherwise. One canonical form per chain; the server
 * accepts both, so a definition written over HTTP in the other form keeps loading. A step
 * timeout does not change the form; see `hasStrandedTimeouts`. A pin does: only a node can
 * carry one, and a plain agent list has nowhere to put it.
 */
export function canonicalForm(kind: WorkflowKind, steps: readonly Step[]): StoredForm {
  return kind === 'Sequential' && (hasFunction(steps) || hasPins(steps)) ? 'nodes' : 'agentNames';
}

/**
 * The step fields of a save request.
 *
 * 🚨 Both fields are always written - the other one empty. `PUT /api/workflows/{name}`
 * replaces the whole definition, and the server refuses a request that sets both.
 * A node that carries no settings, timeout or pin writes an explicit `null`.
 */
export function chainBody(
  kind: WorkflowKind,
  steps: readonly Step[],
): { agentNames: string[]; nodes: WorkflowNodeReference[] } {
  if (canonicalForm(kind, steps) === 'agentNames') {
    // 🚨 The last line behind the screen's hold: an agent list would drop the timeouts, and
    // the full-replace save would erase what the server stores.
    if (hasStepTimeouts(steps)) {
      throw new Error('A step holds a timeout that a plain agent list cannot keep, so no request was built.');
    }

    if (hasPins(steps)) {
      throw new Error('A step pins its agent to a version, which a plain agent list cannot keep, so no request was built.');
    }

    return { agentNames: steps.map((step) => step.name), nodes: [] };
  }

  return {
    agentNames: [],
    nodes: steps.map(
      (step): WorkflowNodeReference => ({
        name: step.name,
        kind: step.kind,
        settings: step.kind === 'Function' ? settingsValue(step.settings) : null,
        timeout: timeoutValue(step.timeout, step.storedTimeout),
        agentVersion: step.agentVersion ?? null,
      }),
    ),
  };
}

/**
 * The settings as the request carries them: parsed JSON, or `null` for none.
 *
 * 🚨 Text that is not JSON is neither: a full-replace save would take `null` for "no
 * settings" and erase what the node stores. The screen holds Save and Retry back while
 * `hasInvalidSettings` is true; this is the last line, so no future caller can turn a typing
 * slip into a silent deletion.
 */
function settingsValue(text: string): unknown {
  const parsed = parseSettings(text);

  if (!parsed.ok) {
    throw new Error('A function step holds settings that are not valid JSON, so no request was built.');
  }

  return parsed.value;
}

export type SettingsParse = { ok: true; value: unknown } | { ok: false };

/**
 * Reads the settings text. Empty is "no settings". Anything that is not JSON is the one
 * thing the console refuses to send; whether the object fits the function's settings type is
 * the server's answer (the validator lives there, and a copy here would drift).
 */
export function parseSettings(text: string): SettingsParse {
  if (text.trim().length === 0) {
    return { ok: true, value: null };
  }

  try {
    return { ok: true, value: JSON.parse(text) as unknown };
  } catch {
    return { ok: false };
  }
}

/** The settings of a loaded node as editable text. */
export function settingsText(value: unknown): string {
  return value === null || value === undefined ? '' : JSON.stringify(value, null, 2);
}

/** Whether any function step holds text that is not JSON. */
export function hasInvalidSettings(steps: readonly Step[]): boolean {
  return steps.some((step) => step.kind === 'Function' && !parseSettings(step.settings).ok);
}

export type TimeoutParse = { ok: true; value: string | null } | { ok: false };

const TIMESPAN = /^(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})(?:\.(\d{1,7}))?$/;
const SECONDS = /^\d+(?:\.\d{1,3})?$/;

/**
 * Reads a timeout field: seconds (`90`, `2.5`, at most milliseconds) or a `hh:mm:ss`
 * value as the server writes it. Empty is "none". Whether the value is in range (one
 * millisecond to about 49.7 days, less for an agent step) is the server's answer: its
 * validator names the node and the field, and a copy here would drift.
 */
export function parseTimeout(text: string): TimeoutParse {
  const trimmed = text.trim();

  if (trimmed.length === 0) {
    return { ok: true, value: null };
  }

  if (SECONDS.test(trimmed)) {
    return { ok: true, value: formatTimeSpan(Math.round(Number(trimmed) * 1000)) };
  }

  return TIMESPAN.test(trimmed) ? { ok: true, value: trimmed } : { ok: false };
}

/**
 * A stored timeout as the field shows it: whole or decimal seconds when the value is a whole
 * number of milliseconds, the stored text itself otherwise (so nothing is rounded away).
 */
export function timeoutText(value: string | null | undefined): string {
  if (value === null || value === undefined) {
    return '';
  }

  const milliseconds = timeSpanMilliseconds(value);

  return milliseconds === null ? value : String(milliseconds / 1000);
}

/**
 * The timeout a request carries. A field the operator did not touch goes back exactly as the
 * server stored it; a changed one is written as `hh:mm:ss`.
 *
 * 🚨 Text that is neither seconds nor `hh:mm:ss` throws, like settings text that is not JSON:
 * a full-replace save would otherwise take it for "none" and erase the stored value. The
 * screen holds Save back while `hasInvalidTimeouts` is true.
 */
export function timeoutValue(text: string, stored?: string | null): string | null {
  if (stored !== null && stored !== undefined && text === timeoutText(stored)) {
    return stored;
  }

  const parsed = parseTimeout(text);

  if (!parsed.ok) {
    throw new Error('A timeout field holds text that is neither seconds nor hh:mm:ss, so no request was built.');
  }

  return parsed.value;
}

/** Whether any step holds a timeout that cannot be read. */
export function hasInvalidTimeouts(steps: readonly Step[]): boolean {
  return steps.some((step) => !parseTimeout(step.timeout).ok);
}

function timeSpanMilliseconds(value: string): number | null {
  const match = TIMESPAN.exec(value.trim());

  if (match === null) {
    return null;
  }

  const [, days, hours, minutes, seconds, fraction] = match;
  const ticks = (fraction ?? '').padEnd(7, '0');

  // Below a millisecond the seconds field cannot say it; the caller shows the text instead.
  if (ticks.slice(3) !== '0000') {
    return null;
  }

  return (
    ((Number(days ?? '0') * 24 + Number(hours)) * 60 + Number(minutes)) * 60_000 +
    Number(seconds) * 1000 +
    Number(ticks.slice(0, 3))
  );
}

function formatTimeSpan(totalMilliseconds: number): string {
  const days = Math.floor(totalMilliseconds / 86_400_000);
  const hours = Math.floor(totalMilliseconds / 3_600_000) % 24;
  const minutes = Math.floor(totalMilliseconds / 60_000) % 60;
  const seconds = Math.floor(totalMilliseconds / 1000) % 60;
  const milliseconds = totalMilliseconds % 1000;
  const pad = (value: number): string => String(value).padStart(2, '0');

  return (
    (days > 0 ? `${days}.` : '') +
    `${pad(hours)}:${pad(minutes)}:${pad(seconds)}` +
    (milliseconds > 0 ? `.${String(milliseconds).padStart(3, '0')}` : '')
  );
}

/**
 * What decides whether a run that is waiting to be resumed WITH THE NEW GRAPH still can be: the
 * pattern and the steps, in order. A description, a settings value, a timeout or an agent pin is
 * not part of it (measured). A run that is waiting keeps resuming the
 * definition version it recorded, so a save does not take the old graph away from it.
 */
export function shapeOf(kind: WorkflowKind, steps: readonly Step[], handoffs: readonly HandoffEdge[] = []): string {
  const shape: unknown[] = [kind, steps.map((step) => [step.kind, step.name])];

  if (kind === 'Handoff') {
    shape.push(graphEdges(steps, handoffs));
  }

  return JSON.stringify(shape);
}

/**
 * One edge of a `Handoff` workflow as the draft holds it: who may hand off to whom, and the reason
 * the model reads (empty when there is none).
 */
export interface HandoffEdge {
  from: string;
  to: string;
  reason: string;
}

/** The edges a definition stores. A definition saved before the field existed has none. */
export function handoffsOf(definition: Pick<WorkflowDefinition, 'handoffs'>): HandoffEdge[] {
  return (definition.handoffs ?? []).map((edge) => ({ from: edge.from, to: edge.to, reason: edge.reason ?? '' }));
}

/**
 * The edges of a save request. Only a `Handoff` workflow has any, so the other patterns send `null`
 * (the server refuses a field the pattern does not use). An edge with no reason sends
 * `null`, so the target agent's own description is what the model reads.
 */
export function handoffsBody(kind: WorkflowKind, edges: readonly HandoffEdge[]): WorkflowHandoff[] | null {
  return kind === 'Handoff'
    ? edges.map((edge) => ({ from: edge.from, to: edge.to, reason: edge.reason.trim().length === 0 ? null : edge.reason.trim() }))
    : null;
}

/**
 * Whether an edge still has an end to choose. The one rule the screen keeps for itself: a row with a
 * blank end would be sent as an empty name, which the server answers with a message about a name nobody typed.
 */
export function hasUnfinishedHandoff(kind: WorkflowKind, edges: readonly HandoffEdge[]): boolean {
  return kind === 'Handoff' && edges.some((edge) => edge.from.length === 0 || edge.to.length === 0);
}

/**
 * The edges a `Handoff` graph has, as sorted `from>to` pairs. An empty list is the star the server
 * compiles for it: the first step hands off to every other one, so writing the star out is no change
 * of graph (measured) and adding an edge to it is.
 */
function graphEdges(steps: readonly Step[], edges: readonly HandoffEdge[]): string[] {
  const pairs =
    edges.length === 0
      ? steps.slice(1).map((step) => `${steps[0]?.name ?? ''}>${step.name}`)
      : edges.map((edge) => `${edge.from}>${edge.to}`);

  return [...new Set(pairs)].sort();
}

/** One member of a function's settings object, as the console lists it. */
export interface SchemaField {
  name: string;
  type: string;
  required: boolean;
}

/**
 * Lists the members a function's settings schema names, for display.
 *
 * Reads only what the server's exporter writes for a plain settings class (`properties`,
 * `required`, `type`, `enum`, `items`); it is not a schema interpreter and does not validate
 * anything. Returns `null` for a schema that names no members - the bare object schema of a
 * settings type that could not be described - so the screen can leave the list out.
 */
export function describeSchema(schema: unknown): SchemaField[] | null {
  if (!isRecord(schema) || !isRecord(schema.properties)) {
    return null;
  }

  const required = new Set(
    Array.isArray(schema.required) ? schema.required.filter((name): name is string => typeof name === 'string') : [],
  );

  return Object.entries(schema.properties).map(([name, property]) => ({
    name,
    type: typeOf(property),
    required: required.has(name),
  }));
}

function typeOf(property: unknown): string {
  if (!isRecord(property)) {
    return 'value';
  }

  if (Array.isArray(property.enum)) {
    return property.enum.map((option) => JSON.stringify(option)).join(' | ');
  }

  const names = (Array.isArray(property.type) ? property.type : [property.type]).filter(
    (name): name is string => typeof name === 'string',
  );
  const nullable = names.includes('null');
  const kinds = names.filter((name) => name !== 'null');

  if (kinds.length === 0) {
    return 'value';
  }

  const base =
    kinds.length === 1 && kinds[0] === 'array' && isRecord(property.items)
      ? `${typeOf(property.items)}[]`
      : kinds.join(' | ');

  return nullable ? `${base}?` : base;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
