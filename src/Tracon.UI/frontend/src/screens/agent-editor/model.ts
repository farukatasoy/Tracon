import type {
  AgentDefinitionRequest,
  AgentResponseFormat,
  CompactionSettings,
  CompactionStrategyKind,
  HarnessSettings,
  MemorySettings,
  ModelBinding,
  ModelFallback,
  SkillSettings,
  WorkspaceSettings,
} from '@tracon/client';
// The definition READ back from the server, not the generated shape: the
// generated one marks fields optional that a response always carries.
import type { AgentDefinition, CallableAgentReference } from '../../lib/server-types';

export const REASONING_EFFORTS = ['', 'None', 'Low', 'Medium', 'High', 'ExtraHigh'] as const;

export const REASONING_OUTPUTS = ['', 'None', 'Summary', 'Full'] as const;

/** The tool modes that take no tool name. `required:<tool>` is built from the definition's tool list. */
export const TOOL_MODES = ['', 'auto', 'none', 'required'] as const;

/** A setting that is on, off, or left to the provider (an unset `boolean | null`). */
export const TRI_STATES = ['', 'true', 'false'] as const;
export type TriState = (typeof TRI_STATES)[number];

export function toTriState(value: boolean | null | undefined): TriState {
  return value === true ? 'true' : value === false ? 'false' : '';
}

function fromTriState(value: TriState): boolean | null {
  return value === 'true' ? true : value === 'false' ? false : null;
}

/**
 * One stop sequence per line; empty lines are dropped and a form with none sends `null`. A line is
 * kept exactly as typed: a stop sequence may start or end with a space.
 */
function toStopSequences(text: string): string[] | null {
  const sequences = text
    .split('\n')
    .map((line) => line.replace(/\r$/, ''))
    .filter((line) => line.length > 0);

  return sequences.length > 0 ? sequences : null;
}

/**
 * The stop sequences to send. A list the person did not touch is sent as it was loaded: the text box
 * holds one sequence per line, so a stored sequence that contains a line break, or that is only
 * whitespace, would come back changed if it were rebuilt from the text.
 */
function stopSequencesToSend(form: FormState): string[] | null {
  const source = form.stopSequencesSource;

  return source !== null && form.stopSequences === source.join('\n') ? source : toStopSequences(form.stopSequences);
}

export const RESPONSE_FORMAT_KINDS = ['', 'Text', 'Json', 'JsonSchema'] as const;
export type ResponseFormatKindOption = (typeof RESPONSE_FORMAT_KINDS)[number];

/** How an agent calls its callable agents: framework background tasks, or one tool for each agent. */
export const SUB_AGENT_CALL_MODES = ['Background', 'Tool'] as const;
export type SubAgentCallModeOption = (typeof SUB_AGENT_CALL_MODES)[number];

/** When an agent searches its conversation memory: before every turn, or when the model asks. */
export const CONVERSATION_MEMORY_SEARCHES = ['BeforeEachTurn', 'OnDemand'] as const;
export type ConversationMemorySearchOption = (typeof CONVERSATION_MEMORY_SEARCHES)[number];

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
export const emptySkills: SkillSettings = {};
export const emptyWorkspace: WorkspaceSettings = {};

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
}

export const emptyPreserved: PreservedFields = {
  subAgents: null,
  mcpResourceUris: [],
  metadata: {},
  parameters: [],
  sharedInstructionsName: null,
  providerSettings: {},
  responseCache: null,
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
  reasoningOutput: string;
  seed: string;
  topK: string;
  stopSequences: string;
  /** The list as it was loaded; `null` for a new agent. See {@link stopSequencesToSend}. */
  stopSequencesSource: string[] | null;
  frequencyPenalty: string;
  presencePenalty: string;
  toolMode: string;
  allowMultipleToolCalls: TriState;
  toolLoopAllowConcurrentToolCalls: boolean;
  toolLoopMaximumConsecutiveErrors: string;
  toolLoopTerminateOnUnknownCalls: TriState;
  responseFormatKind: ResponseFormatKindOption;
  responseFormatSchema: string;
  responseFormatSchemaName: string;
  responseFormatSchemaDescription: string;
  fallbacks: ModelFallback[];
  toolNames: string[];
  skillNames: string[];
  skills: SkillSettings;
  workspace: WorkspaceSettings;
  extensionNames: string[];
  callableAgents: CallableAgentReference[];
  subAgentCallMode: SubAgentCallModeOption;
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
  reasoningOutput: '',
  seed: '',
  topK: '',
  stopSequences: '',
  stopSequencesSource: null,
  frequencyPenalty: '',
  presencePenalty: '',
  toolMode: '',
  allowMultipleToolCalls: '',
  toolLoopAllowConcurrentToolCalls: false,
  toolLoopMaximumConsecutiveErrors: '',
  toolLoopTerminateOnUnknownCalls: '',
  responseFormatKind: '',
  responseFormatSchema: DEFAULT_SCHEMA_TEXT,
  responseFormatSchemaName: '',
  responseFormatSchemaDescription: '',
  fallbacks: [],
  toolNames: [],
  skillNames: [],
  skills: emptySkills,
  workspace: emptyWorkspace,
  extensionNames: [],
  callableAgents: [],
  subAgentCallMode: 'Background',
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

const WHOLE_NUMBER = /^[+-]?\d+$/;

/**
 * True when the seed text can be sent as it is: empty (not set), or a whole number a JavaScript number
 * holds exactly.
 *
 * The server takes a 64-bit seed, and a JavaScript number is exact only up to `Number.MAX_SAFE_INTEGER`.
 * A larger number is not an error to JavaScript: it is rounded, so `9007199254740993` would go out as
 * `9007199254740992` and the agent would run with a seed nobody wrote. The text is therefore checked
 * before it becomes a number, and a seed outside the range is refused rather than rounded. A stored seed
 * outside the range fails the same check: the response was parsed before the form saw it, so what the
 * form holds is already the rounded number.
 */
export function isValidSeed(text: string): boolean {
  const trimmed = text.trim();

  return trimmed.length === 0 || (WHOLE_NUMBER.test(trimmed) && Number.isSafeInteger(Number(trimmed)));
}

function toSeed(text: string): number | null {
  // An invalid seed is left out here; the field shows its own error and the save and validate
  // buttons are disabled until it is a seed the form can send.
  return isValidSeed(text) ? toNumber(text) : null;
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
    reasoningOutput: form.reasoningOutput.length > 0 ? form.reasoningOutput : null,
    seed: toSeed(form.seed),
    topK: toNumber(form.topK),
    stopSequences: stopSequencesToSend(form),
    frequencyPenalty: toNumber(form.frequencyPenalty),
    presencePenalty: toNumber(form.presencePenalty),
    toolMode: form.toolMode.length > 0 ? form.toolMode : null,
    allowMultipleToolCalls: fromTriState(form.allowMultipleToolCalls),
    toolLoop: {
      allowConcurrentToolCalls: form.toolLoopAllowConcurrentToolCalls,
      maximumConsecutiveErrors: toNumber(form.toolLoopMaximumConsecutiveErrors),
      terminateOnUnknownCalls: fromTriState(form.toolLoopTerminateOnUnknownCalls),
    },
    responseFormat: toResponseFormat(form),
    fallbacks: form.fallbacks.filter(
      (fallback) => fallback.provider.trim().length > 0 && fallback.model.trim().length > 0,
    ),
    providerSettings: form.preserved.providerSettings,
    responseCache: form.preserved.responseCache,
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
    skills: anySettingOn(form.skills) ? form.skills : null,
    workspace: anySettingOn(form.workspace) ? form.workspace : null,
    extensionNames: form.extensionNames,
    callableAgents: form.callableAgents,
    harness: form.harnessEnabled ? form.harness : null,
    compaction: form.compaction.strategy === 'None' ? null : form.compaction,
    memory: memoryIsOn(form.memory) ? form.memory : null,
    subAgents: toSubAgents(form),
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
    reasoningOutput: definition.model.reasoningOutput ?? '',
    seed: definition.model.seed?.toString() ?? '',
    topK: definition.model.topK?.toString() ?? '',
    stopSequences: (definition.model.stopSequences ?? []).join('\n'),
    stopSequencesSource: definition.model.stopSequences ?? null,
    frequencyPenalty: definition.model.frequencyPenalty?.toString() ?? '',
    presencePenalty: definition.model.presencePenalty?.toString() ?? '',
    toolMode: definition.model.toolMode ?? '',
    allowMultipleToolCalls: toTriState(definition.model.allowMultipleToolCalls),
    toolLoopAllowConcurrentToolCalls: definition.model.toolLoop?.allowConcurrentToolCalls ?? false,
    toolLoopMaximumConsecutiveErrors: definition.model.toolLoop?.maximumConsecutiveErrors?.toString() ?? '',
    toolLoopTerminateOnUnknownCalls: toTriState(definition.model.toolLoop?.terminateOnUnknownCalls),
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
    skills: definition.skills ?? emptySkills,
    workspace: definition.workspace ?? emptyWorkspace,
    extensionNames: [...(definition.extensionNames ?? [])],
    callableAgents: (definition.callableAgents ?? []).map(({ name, version }) => ({ name, version: version ?? null })),
    subAgentCallMode: definition.subAgents?.callMode ?? 'Background',
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
    },
  };
}

/**
 * The sub-agent settings to send: the wait limits the console has no control for, plus the call mode
 * it does. A mode the person did not change is sent as it was loaded, so a stored `Background` and an
 * absent one both come back unchanged; a changed mode to `Background` drops the key, and the settings
 * become `null` when nothing else is left.
 */
function toSubAgents(form: FormState): AgentDefinitionRequest['subAgents'] {
  const stored = form.preserved.subAgents ?? null;

  if ((stored?.callMode ?? 'Background') === form.subAgentCallMode) {
    return stored;
  }

  if (form.subAgentCallMode === 'Tool') {
    return { ...stored, callMode: 'Tool' };
  }

  // The server writes the unset wait limits as null, so a key with no value is not "something else".
  const rest = Object.fromEntries(
    Object.entries(stored ?? {}).filter(([key, value]) => key !== 'callMode' && value !== null && value !== undefined),
  );

  return Object.keys(rest).length > 0 ? rest : null;
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
 * True when any setting of a settings block (memory, skills, workspace) is actually turned on.
 *
 * Deliberately generic rather than a list of the flags that existed when this
 * was written: the previous version named file/todo/text search only, so a
 * definition whose sole memory feature was vector search serialized as
 * `memory: null` and lost the block on save. A predicate that enumerates
 * fields goes stale every time one is added; this one cannot.
 */
function anySettingOn(settings: object): boolean {
  return Object.values(settings).some((value) =>
    typeof value === 'string' ? value.trim().length > 0 : value !== null && value !== undefined && value !== false,
  );
}

/**
 * True when a memory block turns anything on. The search mode of conversation memory does not count
 * while it holds its default: the server writes it into every block it returns, so a block with
 * everything off would otherwise never be dropped.
 */
function memoryIsOn(memory: MemorySettings): boolean {
  const { conversationMemorySearch, ...rest } = memory;

  return anySettingOn(
    conversationMemorySearch === undefined || conversationMemorySearch === 'BeforeEachTurn' ? rest : memory,
  );
}

/**
 * Defaults `provider` to the first catalog entry, but only if the form still
 * has none. Takes `current` (the live state at apply time) rather than a
 * value captured earlier, so a concurrent update that already set a provider
 * (e.g. loading an existing definition) is never stomped — see
 * `model.test.ts` for the race this guards against.
 */
export function withDefaultProvider(current: FormState, providerNames: readonly string[]): FormState {
  if (current.provider.length > 0 || providerNames.length === 0) {
    return current;
  }

  return { ...current, provider: providerNames[0] ?? '' };
}
