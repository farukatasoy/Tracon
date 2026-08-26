import type {
  AgentDefinitionRequest,
  AgentResponseFormat,
  CompactionSettings,
  CompactionStrategyKind,
  HarnessSettings,
  MemorySettings,
  ModelBinding,
  ModelFallback,
} from '@agentprism/client';

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

function memoryHasAnything(memory: MemorySettings): boolean {
  return memory.enableFileMemory === true || memory.enableTodo === true || memory.enableTextSearch === true;
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
