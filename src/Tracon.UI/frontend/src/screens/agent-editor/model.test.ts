import { describe, expect, it } from 'vitest';
import type { AgentDefinition } from '../../lib/server-types';
import { emptyForm, fromDefinition, isValidSeed, toRequest, withDefaultProvider } from './model';

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
    callableAgents: [],
    subAgents: { waitTimeout: '00:00:30' },
    mcpResourceUris: ['docs:file:///handbook.md'],
    memory: {
      enableFileMemory: false,
      enableTodo: false,
      enableTextSearch: false,
      enableVectorSearch: true,
      vectorCollection: 'internal-documents',
      enableConversationMemory: false,
      conversationMemorySearch: 'BeforeEachTurn',
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

  // F-626: the read approval is the editor's own control. A definition that turned it off must come back
  // from the form as it went in; one that never set it must not grow the block.
  describe('skill settings', () => {
    it('keeps the setting a stored definition turned on', () => {
      const form = fromDefinition({ ...definition, skills: { disableReadApproval: true } });

      expect(form.skills.disableReadApproval).toBe(true);
      expect(toRequest(form).skills).toEqual({ disableReadApproval: true });
    });

    it('reads a definition without skill settings as the default and sends none', () => {
      expect(fromDefinition(definition).skills.disableReadApproval).toBeUndefined();
      expect(toRequest(fromDefinition(definition)).skills).toBeNull();
      expect(toRequest(fromDefinition({ ...definition, skills: null })).skills).toBeNull();
    });

    it('sends no block once the box is cleared, so the default approval applies again', () => {
      const form = { ...fromDefinition({ ...definition, skills: { disableReadApproval: true } }) };

      expect(toRequest({ ...form, skills: { ...form.skills, disableReadApproval: false } }).skills).toBeNull();
    });
  });

  // F-628: the workspace is the editor's own control. A definition that turned it on must come back from the form
  // as it went in; one that never set it must not grow the block.
  describe('workspace settings', () => {
    it('keeps the settings a stored definition turned on', () => {
      const stored = { enabled: true, readOnly: true, disableReadApproval: true };
      const form = fromDefinition({ ...definition, workspace: stored });

      expect(form.workspace).toEqual(stored);
      expect(toRequest(form).workspace).toEqual(stored);
    });

    it('reads a definition without a workspace as off and sends none', () => {
      expect(fromDefinition(definition).workspace.enabled).toBeUndefined();
      expect(toRequest(fromDefinition(definition)).workspace).toBeNull();
      expect(toRequest(fromDefinition({ ...definition, workspace: null })).workspace).toBeNull();
    });

    it('sends no block once every box is cleared, so the agent has no workspace again', () => {
      const form = fromDefinition({ ...definition, workspace: { enabled: true } });

      expect(toRequest({ ...form, workspace: { enabled: false, readOnly: false, disableReadApproval: false } }).workspace).toBeNull();
    });
  });

  // Phase 228: the call mode is the editor's own control; the wait limits beside it still are not.
  describe('sub-agent call mode', () => {
    it('reads a definition without sub-agent settings as background tasks and sends none', () => {
      const form = fromDefinition({ ...definition, subAgents: null });

      expect(form.subAgentCallMode).toBe('Background');
      expect(toRequest(form).subAgents).toBeNull();
    });

    it('sends the settings as loaded while the mode is not touched', () => {
      const stored = { waitTimeout: '00:00:30', callMode: 'Background' as const };

      expect(toRequest(fromDefinition({ ...definition, subAgents: stored })).subAgents).toEqual(stored);
    });

    it('turns tool mode on and keeps the wait limits the form has no control for', () => {
      const form = { ...fromDefinition(definition), subAgentCallMode: 'Tool' as const };

      expect(toRequest(form).subAgents).toEqual({ waitTimeout: '00:00:30', callMode: 'Tool' });
      expect(toRequest({ ...fromDefinition({ ...definition, subAgents: null }), subAgentCallMode: 'Tool' }).subAgents)
        .toEqual({ callMode: 'Tool' });
    });

    it('turns tool mode off by dropping the key, and the settings with it when nothing else is left', () => {
      const tool = { waitTimeout: '00:00:30', callMode: 'Tool' as const };
      const withLimits = { ...fromDefinition({ ...definition, subAgents: tool }), subAgentCallMode: 'Background' as const };
      const alone = { ...fromDefinition({ ...definition, subAgents: { callMode: 'Tool' } }), subAgentCallMode: 'Background' as const };

      // What the server sends back for a definition that set only the mode: the unset limits are null.
      const echoed = { ...fromDefinition({ ...definition, subAgents: { callMode: 'Tool', childDeadline: null, waitTimeout: null } }), subAgentCallMode: 'Background' as const };

      expect(toRequest(withLimits).subAgents).toEqual({ waitTimeout: '00:00:30' });
      expect(toRequest(alone).subAgents).toBeNull();
      expect(toRequest(echoed).subAgents).toBeNull();
    });
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
      // The server writes the default search mode into every block it returns.
      enableConversationMemory: false,
      conversationMemorySearch: 'BeforeEachTurn' as const,
    };

    const bare = toRequest(fromDefinition({ ...definition, memory: nothingOn }));

    expect(bare.memory).toBeNull();
  });

  it('keeps a memory block whose only setting is conversation memory', () => {
    const remembering = {
      enableFileMemory: false,
      enableTodo: false,
      enableTextSearch: false,
      enableVectorSearch: false,
      enableConversationMemory: true,
      conversationMemorySearch: 'OnDemand' as const,
      conversationMemoryMaxResults: 4,
    };

    const request = toRequest(fromDefinition({ ...definition, memory: remembering }));

    expect(request.memory).toEqual(remembering);
  });
});

// F-607: the server takes a 64-bit seed, a JavaScript number holds 53 bits exactly, and a number that does not fit
// comes back as another one. The form keeps the seed as text and never rounds it into a request.
describe('seed', () => {
  const withSeed = (seed: string) => ({ ...emptyForm, seed });
  const MAX_SAFE = '9007199254740991';

  it('sends a whole number inside the safe range as that number, the ends included', () => {
    expect(toRequest(withSeed('42')).model.seed).toBe(42);
    expect(toRequest(withSeed('-7')).model.seed).toBe(-7);
    expect(toRequest(withSeed('0')).model.seed).toBe(0);
    expect(toRequest(withSeed(' 42 ')).model.seed).toBe(42);
    expect(toRequest(withSeed('+5')).model.seed).toBe(5);
    expect(toRequest(withSeed(MAX_SAFE)).model.seed).toBe(Number.MAX_SAFE_INTEGER);
    expect(toRequest(withSeed(`-${MAX_SAFE}`)).model.seed).toBe(Number.MIN_SAFE_INTEGER);
  });

  it('does not send a seed outside the safe range, and says it is not valid', () => {
    for (const text of ['9007199254740992', '9007199254740993', '-9007199254740992', '3000000000000000000', '99999999999999999999']) {
      expect(isValidSeed(text), text).toBe(false);
      expect(toRequest(withSeed(text)).model.seed, text).toBeNull();
    }
  });

  it('does not send text that is not a whole number, and says it is not valid', () => {
    for (const text of ['1.5', 'abc', '12abc', '0x10', '1e3', '--1', '+-1', '++1', '+', '-', '1,000', '4 2']) {
      expect(isValidSeed(text), text).toBe(false);
      expect(toRequest(withSeed(text)).model.seed, text).toBeNull();
    }
  });

  it('treats an empty seed as not set', () => {
    for (const text of ['', '   ']) {
      expect(isValidSeed(text), text).toBe(true);
      expect(toRequest(withSeed(text)).model.seed, text).toBeNull();
    }
  });

  // The response is parsed before the form sees it: the server's 9007199254740993 reaches the form as the nearest
  // number, 9007199254740992. That number is not the stored seed, so it is never sent back.
  it('flags a stored seed the browser could not read exactly, and never sends the rounded number back', () => {
    const stored = JSON.parse('9007199254740993') as number;
    const form = fromDefinition({ ...definitionWithSeed(stored) });

    expect(form.seed).toBe('9007199254740992');
    expect(isValidSeed(form.seed)).toBe(false);
    expect(toRequest(form).model.seed).toBeNull();
  });

  it('flags a stored seed the browser writes with an exponent', () => {
    const form = fromDefinition(definitionWithSeed(JSON.parse('1000000000000000000000') as number));

    expect(form.seed).toBe('1e+21');
    expect(isValidSeed(form.seed)).toBe(false);
  });

  it('keeps a stored seed inside the safe range exactly', () => {
    const form = fromDefinition(definitionWithSeed(Number.MAX_SAFE_INTEGER));

    expect(isValidSeed(form.seed)).toBe(true);
    expect(toRequest(form).model.seed).toBe(Number.MAX_SAFE_INTEGER);
  });

  function definitionWithSeed(seed: number): AgentDefinition {
    return {
      name: 'seeded',
      instructions: 'Be brief.',
      model: { provider: 'openai', model: 'configured-model', fallbacks: [], seed },
      toolNames: [],
      skillNames: [],
      extensionNames: [],
      callableAgents: [],
      origin: 'Database',
      version: 1,
    } as unknown as AgentDefinition;
  }
});
