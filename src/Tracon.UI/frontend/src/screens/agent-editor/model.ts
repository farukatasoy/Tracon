import type {
  AgentDefinitionRequest,
  AgentResponseFormat,
  CompactionSettings,
  CompactionStrategyKind,
  HarnessSettings,
  MemorySettings,
  ModelBinding,
  ModelFallback,
} from '@tracon/client';
// The definition READ back from the server, not the generated shape: the
// generated one marks fields optional that a response always carries.
import type { AgentDefinition } from '../../lib/server-types';

export const REASONING_EFFORTS = ['', 'None', 'Low', 'Medium', 'High', 'ExtraHigh'] as const;

export const RESPONSE_FORMAT_KINDS = ['', 'Text', 'Json', 'JsonSchema'] as const;
export type ResponseFormatKindOption = (typeof RESPONSE_FORMAT_KINDS)[number];

export const DEFAULT_SCHEMA_TEXT = '{\n  "type": "object",\n  "properties": {}\n}';

export function isValidJson(text: string): boolean {
  try {
    JSON.parse(text);

    return true;
  } catch {
    return false;
  }
}

export const COMPACTION_STRATEGIES: CompactionStrategyKind[] = [
  'None',
  'SlidingWindow',
  'Truncation',
  'ToolResult',
  'Summarization',
  'ContextWindow',
  'Pipeline',
];

// `strategy` is optional on the generated (request-shaped) type, but the form
// always carries one — narrowed locally so the strategy switch below and the
// `<Select>` binding don't need an `?? 'None'` at every read.
export type CompactionForm = Omit<CompactionSettings, 'strategy'> & { strategy: CompactionStrategyKind };

export const emptyCompaction: CompactionForm = { strategy: 'None' };
export const emptyMemory: MemorySettings = {};

export interface CultureInstructions {
  culture: string;
  text: string;
}

/**
 * Definition fields the console stores but offers no control for.
 *
 * `PUT /api/agents/{name}` is a full replace, so anything the form does not
 * carry is erased rather than left alone — a definition written from code or
 * from the HTTP API lost its parameters, its shared instructions block and
 * three of its model settings the first time somebody edited its description
 * in the console (B01).
 *
 * Preserving a field is deliberately separate from editing it: these travel
 * through the form untouched. Adding a real control for one of them means
 * moving it out of here into `FormState` proper, not widening this type.
 */
export interface PreservedFields {
  subAgents: AgentDefinitionRequest['subAgents'];
  mcpResourceUris: AgentDefinitionRequest['mcpResourceUris'];
  metadata: AgentDefinitionRequest['metadata'];
  parameters: AgentDefinitionRequest['parameters'];
  sharedInstructionsName: AgentDefinitionRequest['sharedInstructionsName'];
  providerSettings: ModelBinding['providerSettings'];
  responseCache: ModelBinding['responseCache'];
  allowConcurrentToolCalls: ModelBinding['allowConcurrentToolCalls'];
}

export const emptyPreserved: PreservedFields = {
  subAgents: null,
  mcpResourceUris: [],
  metadata: {},
  parameters: [],
  sharedInstructionsName: null,
  providerSettings: {},
  responseCache: null,
  allowConcurrentToolCalls: false,
};

export interface FormState {
  name: string;
  displayName: string;
  description: string;
  instructions: string;
  instructionsByCulture: CultureInstructions[];
  provider: string;
  model: string;
  temperature: string;
  maxOutputTokens: string;
  topP: string;
  reasoningEffort: string;
  responseFormatKind: ResponseFormatKindOption;
  responseFormatSchema: string;
  responseFormatSchemaName: string;
  responseFormatSchemaDescription: string;
  fallbacks: ModelFallback[];
  toolNames: string[];
  skillNames: string[];
  callableAgentNames: string[];
  harnessEnabled: boolean;
  harness: HarnessSettings;
  compaction: CompactionForm;
  memory: MemorySettings;
  preserved: PreservedFields;
}

export const emptyForm: FormState = {
  name: '',
  displayName: '',
  description: '',
  instructions: '',
  instructionsByCulture: [],
  provider: '',
  model: '',
  temperature: '',
  maxOutputTokens: '',
  topP: '',
  reasoningEffort: '',
  responseFormatKind: '',
  responseFormatSchema: DEFAULT_SCHEMA_TEXT,
  responseFormatSchemaName: '',
  responseFormatSchemaDescription: '',
  fallbacks: [],
  toolNames: [],
  skillNames: [],
  callableAgentNames: [],
  harnessEnabled: false,
  harness: {},
  compaction: emptyCompaction,
  memory: emptyMemory,
  preserved: emptyPreserved,
};

export function toNumber(value: string): number | null {
  if (value.trim().length === 0) {
    return null;
  }

  const parsed = Number(value);

  return Number.isFinite(parsed) ? parsed : null;
}

function toResponseFormat(form: FormState): AgentResponseFormat | null {
  if (form.responseFormatKind === '') {
    return null;
  }

  if (form.responseFormatKind !== 'JsonSchema') {
    return { kind: form.responseFormatKind };
  }

  return {
    kind: 'JsonSchema',
    // Invalid JSON is left out here; the schema textbox shows its own error
    // and the save/validate buttons are disabled until it parses.
    schema: isValidJson(form.responseFormatSchema) ? JSON.parse(form.responseFormatSchema) : undefined,
    schemaName: form.responseFormatSchemaName.trim().length > 0 ? form.responseFormatSchemaName.trim() : null,
    schemaDescription:
      form.responseFormatSchemaDescription.trim().length > 0 ? form.responseFormatSchemaDescription.trim() : null,
  };
}

export function toRequest(form: FormState): AgentDefinitionRequest {
  const model: ModelBinding = {
    provider: form.provider.trim(),
    model: form.model.trim(),
    temperature: toNumber(form.temperature),
    maxOutputTokens: toNumber(form.maxOutputTokens),
    topP: toNumber(form.topP),
    reasoningEffort: form.reasoningEffort.length > 0 ? form.reasoningEffort : null,
    responseFormat: toResponseFormat(form),
    fallbacks: form.fallbacks.filter(
      (fallback) => fallback.provider.trim().length > 0 && fallback.model.trim().length > 0,
    ),
    providerSettings: form.preserved.providerSettings,
    responseCache: form.preserved.responseCache,
    allowConcurrentToolCalls: form.preserved.allowConcurrentToolCalls,
  };

  return {
    name: form.name.trim(),
    displayName: form.displayName.trim().length > 0 ? form.displayName.trim() : null,
    description: form.description.trim().length > 0 ? form.description.trim() : null,
    instructions: form.instructions.trim().length > 0 ? form.instructions.trim() : null,
    instructionsByCulture: toInstructionsByCulture(form.instructionsByCulture),
    model,
    toolNames: form.toolNames,
    skillNames: form.skillNames,
    callableAgentNames: form.callableAgentNames,
    harness: form.harnessEnabled ? form.harness : null,
    compaction: form.compaction.strategy === 'None' ? null : form.compaction,
    memory: memoryHasAnything(form.memory) ? form.memory : null,
    subAgents: form.preserved.subAgents,
    mcpResourceUris: form.preserved.mcpResourceUris,
    metadata: form.preserved.metadata,
    parameters: form.preserved.parameters,
    sharedInstructionsName: form.preserved.sharedInstructionsName,
  };
}

/**
 * Builds the form state for an existing definition.
 *
 * The inverse of {@link toRequest}, and deliberately in the same file: the two
 * are only correct as a pair, and while the load half lived in the editor hook
 * nothing could test that a definition survived a trip through the form.
 * Everything the form has no control for is carried in `preserved`.
 */
export function fromDefinition(definition: AgentDefinition): FormState {
  return {
    name: definition.name,
    displayName: definition.displayName ?? '',
    description: definition.description ?? '',
    instructions: definition.instructions ?? '',
    instructionsByCulture: Object.entries(definition.instructionsByCulture ?? {}).map(([culture, text]) => ({
      culture,
      text,
    })),
    provider: definition.model.provider,
    model: definition.model.model,
    temperature: definition.model.temperature?.toString() ?? '',
    maxOutputTokens: definition.model.maxOutputTokens?.toString() ?? '',
    topP: definition.model.topP?.toString() ?? '',
    reasoningEffort: definition.model.reasoningEffort ?? '',
    responseFormatKind: definition.model.responseFormat?.kind ?? '',
    responseFormatSchema:
      definition.model.responseFormat?.schema !== undefined && definition.model.responseFormat?.schema !== null
        ? JSON.stringify(definition.model.responseFormat.schema, null, 2)
        : emptyForm.responseFormatSchema,
    responseFormatSchemaName: definition.model.responseFormat?.schemaName ?? '',
    responseFormatSchemaDescription: definition.model.responseFormat?.schemaDescription ?? '',
    fallbacks: definition.model.fallbacks ?? [],
    toolNames: [...definition.toolNames],
    skillNames: [...definition.skillNames],
    callableAgentNames: [...(definition.callableAgentNames ?? [])],
    harnessEnabled: definition.harness !== null && definition.harness !== undefined,
    harness: definition.harness ?? {},
    compaction: definition.compaction ?? emptyCompaction,
    memory: definition.memory ?? emptyMemory,
    preserved: {
      subAgents: definition.subAgents ?? null,
      mcpResourceUris: definition.mcpResourceUris ?? [],
      metadata: definition.metadata ?? {},
      parameters: definition.parameters ?? [],
      sharedInstructionsName: definition.sharedInstructionsName ?? null,
      providerSettings: definition.model.providerSettings ?? {},
      responseCache: definition.model.responseCache ?? null,
      allowConcurrentToolCalls: definition.model.allowConcurrentToolCalls ?? false,
    },
  };
}

/** Rows with an empty culture code or empty text are dropped; a later row wins on a duplicate code. */
function toInstructionsByCulture(rows: CultureInstructions[]): Record<string, string> | null {
  const entries = rows
    .map((row) => ({ culture: row.culture.trim(), text: row.text.trim() }))
    .filter((row) => row.culture.length > 0 && row.text.length > 0);

  if (entries.length === 0) {
    return null;
  }

  return Object.fromEntries(entries.map((row) => [row.culture, row.text]));
}

/**
 * True when any memory setting is actually turned on.
 *
 * Deliberately generic rather than a list of the flags that existed when this
 * was written: the previous version named file/todo/text search only, so a
 * definition whose sole memory feature was vector search serialized as
 * `memory: null` and lost the block on save. A predicate that enumerates
 * fields goes stale every time one is added; this one cannot.
 */
function memoryHasAnything(memory: MemorySettings): boolean {
  return Object.values(memory).some((value) =>
    typeof value === 'string' ? value.trim().length > 0 : value !== null && value !== undefined && value !== false,
  );
}

/**
 * Defaults `provider` to the first catalog entry, but only if the form still
 * has none. Takes `current` (the live state at apply time) rather than a
 * value captured earlier, so a concurrent update that already set a provider
 * (e.g. loading an existing definition) is never stomped — see
 * `model.test.ts` for the race this guards against (HATA-S4-009).
 */
export function withDefaultProvider(current: FormState, providerNames: readonly string[]): FormState {
  if (current.provider.length > 0 || providerNames.length === 0) {
    return current;
  }

  return { ...current, provider: providerNames[0] ?? '' };
}
