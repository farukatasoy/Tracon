import type { WorkflowNodeReference } from '@tracon/client';
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
}

/** Reads a definition's steps from whichever form it was stored in. */
export function stepsOf(definition: Pick<WorkflowDefinition, 'agentNames' | 'nodes'>): Step[] {
  if (definition.nodes.length > 0) {
    return definition.nodes.map((node) => ({
      kind: node.kind === 'Function' ? 'Function' : 'Agent',
      name: node.name,
      settings: settingsText(node.settings),
    }));
  }

  return definition.agentNames.map((name) => ({ kind: 'Agent', name, settings: '' }));
}

/** Whether the chain has a function node - the one thing that decides the stored form. */
export function hasFunction(steps: readonly Step[]): boolean {
  return steps.some((step) => step.kind === 'Function');
}

/** The two ways a definition keeps its steps. */
export type StoredForm = 'agentNames' | 'nodes';

/** How a definition that was loaded from the server keeps its steps. */
export function storedFormOf(definition: Pick<WorkflowDefinition, 'nodes'>): StoredForm {
  return definition.nodes.length > 0 ? 'nodes' : 'agentNames';
}

/**
 * The form the console writes: `nodes` when the chain has a function, `agentNames`
 * otherwise. One canonical form per chain (decision of phase 195); the server accepts both,
 * so a definition written over HTTP in the other form keeps loading.
 */
export function canonicalForm(kind: WorkflowKind, steps: readonly Step[]): StoredForm {
  return kind === 'Sequential' && hasFunction(steps) ? 'nodes' : 'agentNames';
}

/**
 * The step fields of a save request.
 *
 * 🚨 Both fields are always written - the other one empty. `PUT /api/workflows/{name}`
 * replaces the whole definition (K-725), and the server refuses a request that sets both.
 * A node that carries no settings writes an explicit `null`.
 */
export function chainBody(
  kind: WorkflowKind,
  steps: readonly Step[],
): { agentNames: string[]; nodes: WorkflowNodeReference[] } {
  if (canonicalForm(kind, steps) === 'agentNames') {
    return { agentNames: steps.map((step) => step.name), nodes: [] };
  }

  return {
    agentNames: [],
    nodes: steps.map(
      (step): WorkflowNodeReference => ({
        name: step.name,
        kind: step.kind,
        settings: step.kind === 'Function' ? settingsValue(step.settings) : null,
      }),
    ),
  };
}

/** The settings as the request carries them: parsed JSON, or `null` for none. */
function settingsValue(text: string): unknown {
  const parsed = parseSettings(text);

  return parsed.ok ? parsed.value : null;
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

/**
 * What decides whether a run that is waiting to be resumed still can be: the pattern and the
 * steps, in order. A description or a settings value is not part of it (measured, phase 195).
 */
export function shapeOf(kind: WorkflowKind, steps: readonly Step[]): string {
  return JSON.stringify([kind, steps.map((step) => [step.kind, step.name])]);
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
