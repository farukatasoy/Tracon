import { describe, expect, it } from 'vitest';
import type { AgentDefinition } from '../../lib/server-types';
import { emptyForm, fromDefinition, toRequest, withDefaultProvider } from './model';

describe('withDefaultProvider', () => {
  it('defaults to the first catalog provider when the form has none', () => {
    const next = withDefaultProvider(emptyForm, ['anthropic', 'openai']);

    expect(next.provider).toBe('anthropic');
  });

  it('leaves an empty form unchanged when the catalog is empty', () => {
    const next = withDefaultProvider(emptyForm, []);

    expect(next).toBe(emptyForm);
  });

  // HATA-S4-009 / MT-UIAG-014: the effect that loads an existing definition
  // and the effect that defaults the provider can both settle in the same
  // React commit. When that happens this function receives `current` as the
  // *already-loaded* form (the definition's real provider applied first),
  // not the stale pre-load form its own effect closed over. It must not
  // overwrite a provider that is already set, even though it differs from
  // the catalog's first entry.
  it('does not overwrite a provider already set by a concurrent update', () => {
    const loadedFromDefinition: typeof emptyForm = { ...emptyForm, provider: 'openai' };

    const next = withDefaultProvider(loadedFromDefinition, ['anthropic', 'google']);

    expect(next.provider).toBe('openai');
  });
});

// B01: the editor rebuilds the request from form state, and `PUT /api/agents`
// is a full replace — so a definition field the form never carries is not
// "left alone" by an edit, it is erased. Measured loss: `parameters`,
// `sharedInstructionsName`, the three `model` sub-fields, and a `memory` that
// only had vector search turned on.
describe('definition round trip', () => {
  const definition: AgentDefinition = {
    name: 'review-agent',
    displayName: 'Review Agent',
    description: 'Reviews pull requests.',
    instructions: 'Be brief.',
    model: {
      provider: 'openai',
      model: 'configured-model',
      temperature: null,
      maxOutputTokens: null,
      topP: null,
      fallbacks: [],
      providerSettings: { 'anthropic.promptCaching': true } as never,
      responseCache: { enabled: true, lifetime: '00:05:00' },
      seed: 42,
      topK: 40,
      stopSequences: ['END', 'STOP'],
      frequencyPenalty: 0.25,
      presencePenalty: 0.5,
      toolMode: 'required:diff-tool',
      allowMultipleToolCalls: false,
      reasoningOutput: 'Summary',
      toolLoop: { allowConcurrentToolCalls: true, maximumConsecutiveErrors: 1, terminateOnUnknownCalls: false },
    },
    toolNames: ['diff-tool'],
    skillNames: [],
    // The order is the order the extensions run: the round trip must keep it.
    extensionNames: ['second-run', 'first-run'],
    callableAgentNames: [],
    subAgents: { waitTimeout: '00:00:30' },
    mcpResourceUris: ['docs:file:///handbook.md'],
    memory: {
      enableFileMemory: false,
      enableTodo: false,
      enableTextSearch: false,
      enableVectorSearch: true,
      vectorCollection: 'internal-documents',
    },
    metadata: { owner: 'platform-team' } as never,
    parameters: [{ name: 'tone', kind: 'Text', required: false }],
    sharedInstructionsName: 'shared-block',
    compaction: null,
    harness: null,
    origin: 'Database',
    version: 3,
  };

  const request = toRequest(fromDefinition(definition));

  it('keeps the fields the editor has no control for', () => {
    expect(request.parameters).toEqual(definition.parameters);
    expect(request.sharedInstructionsName).toBe('shared-block');
    expect(request.subAgents).toEqual(definition.subAgents);
    expect(request.mcpResourceUris).toEqual(definition.mcpResourceUris);
    expect(request.metadata).toEqual(definition.metadata);
  });

  it('keeps the extension names in the order they run', () => {
    expect(request.extensionNames).toEqual(['second-run', 'first-run']);
  });

  it('keeps the model sub-fields the editor rebuilds around', () => {
    expect(request.model.providerSettings).toEqual(definition.model.providerSettings);
    expect(request.model.responseCache).toEqual(definition.model.responseCache);
  });

  // F-598: the sampling fields and the tool-call loop are the editor's own controls now,
  // so a definition that sets them must come back from the form as it went in.
  it('keeps the request fields and the tool-call loop the editor now has controls for', () => {
    expect(request.model.seed).toBe(42);
    expect(request.model.topK).toBe(40);
    expect(request.model.stopSequences).toEqual(['END', 'STOP']);
    expect(request.model.frequencyPenalty).toBe(0.25);
    expect(request.model.presencePenalty).toBe(0.5);
    expect(request.model.toolMode).toBe('required:diff-tool');
    expect(request.model.allowMultipleToolCalls).toBe(false);
    expect(request.model.reasoningOutput).toBe('Summary');
    expect(request.model.toolLoop).toEqual({
      allowConcurrentToolCalls: true,
      maximumConsecutiveErrors: 1,
      terminateOnUnknownCalls: false,
    });
  });

  it('sends the unset fields as null so the provider default applies', () => {
    const bare = toRequest(fromDefinition({ ...definition, model: { ...definition.model, seed: null, topK: null, stopSequences: null, frequencyPenalty: null, presencePenalty: null, toolMode: null, allowMultipleToolCalls: null, reasoningOutput: null, toolLoop: null } }));

    expect(bare.model.seed).toBeNull();
    expect(bare.model.topK).toBeNull();
    expect(bare.model.stopSequences).toBeNull();
    expect(bare.model.frequencyPenalty).toBeNull();
    expect(bare.model.presencePenalty).toBeNull();
    expect(bare.model.toolMode).toBeNull();
    expect(bare.model.allowMultipleToolCalls).toBeNull();
    expect(bare.model.reasoningOutput).toBeNull();
    expect(bare.model.toolLoop).toEqual({ allowConcurrentToolCalls: false, maximumConsecutiveErrors: null, terminateOnUnknownCalls: null });
  });

  it('writes one stop sequence per line and drops empty lines', () => {
    const form = { ...fromDefinition(definition), stopSequences: 'END\n\nSTOP\r\n' };

    expect(toRequest(form).model.stopSequences).toEqual(['END', 'STOP']);
    expect(toRequest({ ...form, stopSequences: '\n' }).model.stopSequences).toBeNull();
  });

  it('keeps a stop sequence exactly as typed, spaces included', () => {
    const form = { ...fromDefinition(definition), stopSequences: ' User:\nEND ' };

    expect(toRequest(form).model.stopSequences).toEqual([' User:', 'END ']);
  });

  // The text box holds one sequence per line: a stored sequence with a line break, or one that is only
  // whitespace, cannot be rebuilt from it. An edit of another field must not rewrite it.
  it('sends an untouched stop list as loaded, even one the text box cannot hold', () => {
    const awkward = ['\nUser:', '  ', 'a\r\nb'];
    const loaded = fromDefinition({ ...definition, model: { ...definition.model, stopSequences: awkward } });

    expect(toRequest({ ...loaded, description: 'edited elsewhere' }).model.stopSequences).toEqual(awkward);

    // Typing into the box takes the text as the new list.
    expect(toRequest({ ...loaded, stopSequences: 'END' }).model.stopSequences).toEqual(['END']);
  });

  // The old predicate asked only about file/todo/text search, so a definition
  // whose sole memory feature was vector search serialized as `memory: null`.
  it('keeps a memory block that only has vector search turned on', () => {
    expect(request.memory).toEqual(definition.memory);
  });

  it('still drops a memory block in which nothing is turned on', () => {
    const nothingOn = {
      enableFileMemory: false,
      enableTodo: false,
      enableTextSearch: false,
      enableVectorSearch: false,
    };

    const bare = toRequest(fromDefinition({ ...definition, memory: nothingOn }));

    expect(bare.memory).toBeNull();
  });
});
