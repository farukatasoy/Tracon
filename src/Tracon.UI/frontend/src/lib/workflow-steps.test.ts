import { describe, expect, it } from 'vitest';
import {
  canonicalForm,
  chainBody,
  describeSchema,
  hasFunction,
  hasInvalidSettings,
  parseSettings,
  settingsText,
  shapeOf,
  stepsOf,
  storedFormOf,
  type Step,
} from './workflow-steps';

const agent = (name: string): Step => ({ kind: 'Agent', name, settings: '' });
const fn = (name: string, settings = ''): Step => ({ kind: 'Function', name, settings });

describe('stepsOf', () => {
  it('reads the agent list as agent steps', () => {
    expect(stepsOf({ agentNames: ['a', 'b'], nodes: [] })).toEqual([agent('a'), agent('b')]);
  });

  it('reads a node list in order, with the settings as editable text', () => {
    expect(
      stepsOf({
        agentNames: [],
        nodes: [
          { name: 'a', kind: 'Agent', settings: null },
          { name: 'tts', kind: 'Function', settings: { voice: 'alloy' } },
        ],
      }),
    ).toEqual([agent('a'), fn('tts', '{\n  "voice": "alloy"\n}')]);
  });

  it('reads a node without a settings member as having none', () => {
    expect(stepsOf({ agentNames: [], nodes: [{ name: 'tts', kind: 'Function' }] })).toEqual([fn('tts')]);
  });
});

describe('the stored form', () => {
  it('is nodes when the definition has a node list, whatever it holds', () => {
    expect(storedFormOf({ nodes: [{ name: 'a', kind: 'Agent' }] })).toBe('nodes');
    expect(storedFormOf({ nodes: [] })).toBe('agentNames');
  });

  it('is written as nodes only for a Sequential chain that holds a function', () => {
    expect(canonicalForm('Sequential', [agent('a'), fn('tts')])).toBe('nodes');
    expect(canonicalForm('Sequential', [agent('a'), agent('b')])).toBe('agentNames');

    // Another pattern never gets nodes: the server refuses them.
    expect(canonicalForm('Concurrent', [agent('a'), agent('b')])).toBe('agentNames');
  });
});

describe('chainBody', () => {
  it('writes the agent list and an EMPTY node list for an agent-only chain', () => {
    expect(chainBody('Sequential', [agent('a'), agent('b')])).toEqual({ agentNames: ['a', 'b'], nodes: [] });
  });

  it('writes the node list and an EMPTY agent list when the chain holds a function (the server refuses both set)', () => {
    expect(chainBody('Sequential', [agent('a'), fn('tts', '{"voice":"alloy"}'), fn('mix')])).toStrictEqual({
      agentNames: [],
      nodes: [
        { name: 'a', kind: 'Agent', settings: null },
        { name: 'tts', kind: 'Function', settings: { voice: 'alloy' } },
        { name: 'mix', kind: 'Function', settings: null },
      ],
    });
  });

  it('never sends settings for an agent step, whatever its text holds', () => {
    const body = chainBody('Sequential', [{ kind: 'Agent', name: 'a', settings: '{"x":1}' }, fn('tts')]);

    expect(body.nodes[0]).toStrictEqual({ name: 'a', kind: 'Agent', settings: null });
  });

  it('sends the other patterns their agents as names', () => {
    expect(chainBody('Magentic', [agent('a'), agent('b')])).toEqual({ agentNames: ['a', 'b'], nodes: [] });
  });

  it('keeps every JSON kind a settings object can hold', () => {
    const text = '{"n":3,"f":-12.5,"b":true,"s":"x","a":[1,"2"],"o":{"k":null},"z":null}';
    const body = chainBody('Sequential', [fn('tts', text)]);

    expect(body.nodes[0]?.settings).toStrictEqual(JSON.parse(text));
  });
});

describe('parseSettings', () => {
  it('reads empty text as no settings', () => {
    expect(parseSettings('')).toEqual({ ok: true, value: null });
    expect(parseSettings('  \n ')).toEqual({ ok: true, value: null });
  });

  it('reads JSON and says nothing about its shape: that is the server\'s answer', () => {
    expect(parseSettings('{"a":1}')).toEqual({ ok: true, value: { a: 1 } });
    expect(parseSettings('[1,2]')).toEqual({ ok: true, value: [1, 2] });
    expect(parseSettings('3')).toEqual({ ok: true, value: 3 });
  });

  it('refuses text that is not JSON', () => {
    expect(parseSettings('{ nope')).toEqual({ ok: false });
    expect(parseSettings("{'a':1}")).toEqual({ ok: false });
    expect(parseSettings('{"a":1,}')).toEqual({ ok: false });
  });

  it('is the check that holds a save back, and only for function steps', () => {
    expect(hasInvalidSettings([agent('a'), fn('tts', '{ nope')])).toBe(true);
    expect(hasInvalidSettings([agent('a'), fn('tts', '{"a":1}'), fn('mix')])).toBe(false);
    expect(hasInvalidSettings([{ kind: 'Agent', name: 'a', settings: '{ nope' }])).toBe(false);
  });
});

describe('settingsText', () => {
  it('writes null and undefined as empty, and objects as indented JSON', () => {
    expect(settingsText(null)).toBe('');
    expect(settingsText(undefined)).toBe('');
    expect(settingsText({ a: 1 })).toBe('{\n  "a": 1\n}');
  });

  it('round-trips through parseSettings', () => {
    const value = { voice: 'alloy', loudnessLufs: -12.5, tags: ['a'], nested: { on: true }, none: null };

    expect(parseSettings(settingsText(value))).toEqual({ ok: true, value });
  });
});

describe('shapeOf', () => {
  it('changes with the pattern, the steps and their order', () => {
    const base = shapeOf('Sequential', [agent('a'), agent('b')]);

    expect(shapeOf('Concurrent', [agent('a'), agent('b')])).not.toBe(base);
    expect(shapeOf('Sequential', [agent('b'), agent('a')])).not.toBe(base);
    expect(shapeOf('Sequential', [agent('a')])).not.toBe(base);
    expect(shapeOf('Sequential', [agent('a'), fn('b')])).not.toBe(base);
  });

  it('does not change with the settings: a settings edit leaves a waiting run resumable', () => {
    expect(shapeOf('Sequential', [fn('tts', '{"a":1}')])).toBe(shapeOf('Sequential', [fn('tts', '{"a":2}')]));
  });
});

describe('hasFunction', () => {
  it('is true only when a step is a function', () => {
    expect(hasFunction([agent('a'), fn('tts')])).toBe(true);
    expect(hasFunction([agent('a')])).toBe(false);
    expect(hasFunction([])).toBe(false);
  });
});

describe('describeSchema', () => {
  // The schema the server exports for the settings record of the phase document's example.
  const tts = {
    type: 'object',
    properties: {
      voice: { type: 'string' },
      loudnessLufs: { type: 'number' },
      speed: { type: 'string', enum: ['Slow', 'Normal', 'Fast'] },
      maxRetries: { type: ['integer', 'null'] },
      tags: { type: 'array', items: { type: 'string' } },
    },
    required: ['voice'],
    additionalProperties: false,
  };

  it('lists the members with their types and which are required', () => {
    expect(describeSchema(tts)).toEqual([
      { name: 'voice', type: 'string', required: true },
      { name: 'loudnessLufs', type: 'number', required: false },
      { name: 'speed', type: '"Slow" | "Normal" | "Fast"', required: false },
      { name: 'maxRetries', type: 'integer?', required: false },
      { name: 'tags', type: 'string[]', required: false },
    ]);
  });

  it('lists nothing for the bare object schema of a settings type that could not be described', () => {
    expect(describeSchema({ type: 'object' })).toBeNull();
  });

  it('lists nothing for anything that is not an object schema', () => {
    expect(describeSchema(null)).toBeNull();
    expect(describeSchema(undefined)).toBeNull();
    expect(describeSchema('object')).toBeNull();
    expect(describeSchema([])).toBeNull();
  });

  it('reads a member it cannot type as a plain value', () => {
    expect(describeSchema({ type: 'object', properties: { odd: { $comment: 'Unsupported .NET type', not: true } } })).toEqual([
      { name: 'odd', type: 'value', required: false },
    ]);
  });
});
