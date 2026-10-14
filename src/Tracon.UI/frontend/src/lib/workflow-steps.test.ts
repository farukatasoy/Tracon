import { describe, expect, it } from 'vitest';
import {
  canonicalForm,
  chainBody,
  describeSchema,
  hasFunction,
  hasInvalidSettings,
  hasInvalidTimeouts,
  hasPins,
  hasStepTimeouts,
  hasStrandedTimeouts,
  parseSettings,
  parseTimeout,
  settingsText,
  shapeOf,
  stepsOf,
  storedFormOf,
  timeoutText,
  timeoutValue,
  type Step,
} from './workflow-steps';

const agent = (name: string, timeout = ''): Step => ({ kind: 'Agent', name, settings: '', timeout });
const fn = (name: string, settings = '', timeout = ''): Step => ({ kind: 'Function', name, settings, timeout });

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
        { name: 'a', kind: 'Agent', settings: null, timeout: null, agentVersion: null },
        { name: 'tts', kind: 'Function', settings: { voice: 'alloy' }, timeout: null, agentVersion: null },
        { name: 'mix', kind: 'Function', settings: null, timeout: null, agentVersion: null },
      ],
    });
  });

  it('never sends settings for an agent step, whatever its text holds', () => {
    const body = chainBody('Sequential', [{ kind: 'Agent', name: 'a', settings: '{"x":1}', timeout: '' }, fn('tts')]);

    expect(body.nodes[0]).toStrictEqual({ name: 'a', kind: 'Agent', settings: null, timeout: null, agentVersion: null });
  });

  it('sends the other patterns their agents as names', () => {
    expect(chainBody('Magentic', [agent('a'), agent('b')])).toEqual({ agentNames: ['a', 'b'], nodes: [] });
  });

  it('builds no request from settings text that is not JSON: null would stand for "no settings" and erase them', () => {
    expect(() => chainBody('Sequential', [agent('a'), fn('tts', '{ nope')])).toThrow('not valid JSON');
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
    expect(hasInvalidSettings([{ kind: 'Agent', name: 'a', settings: '{ nope', timeout: '' }])).toBe(false);
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

// Phase 196 (F-303, F-405, failure mode 13): a step's timeout and the run timeout.
describe('step timeouts', () => {
  it('reads a stored timeout as seconds and keeps the stored spelling beside it', () => {
    const [writer, render] = stepsOf({
      agentNames: [],
      nodes: [
        { name: 'writer', kind: 'Agent', settings: null, timeout: '00:06:00' },
        { name: 'render', kind: 'Function', settings: null, timeout: '00:00:02.5000000' },
      ],
    });

    expect(writer?.timeout).toBe('360');
    expect(writer?.storedTimeout).toBe('00:06:00');
    expect(render?.timeout).toBe('2.5');
  });

  it('writes an untouched timeout back exactly as stored, and a changed one as hh:mm:ss', () => {
    const steps = stepsOf({
      agentNames: [],
      nodes: [
        { name: 'writer', kind: 'Agent', settings: null, timeout: '00:06:00.0000000' },
        { name: 'render', kind: 'Function', settings: null, timeout: '00:00:02.5000000' },
      ],
    });
    const changed = steps.map((step) => (step.name === 'render' ? { ...step, timeout: '90' } : step));

    expect(chainBody('Sequential', changed).nodes.map((node) => node.timeout)).toEqual(['00:06:00.0000000', '00:01:30']);
  });

  it('clears a timeout the operator emptied: the save is a full replace', () => {
    const [render] = stepsOf({ agentNames: [], nodes: [{ name: 'render', kind: 'Function', settings: null, timeout: '00:00:05' }] });

    expect(chainBody('Sequential', [{ ...render!, timeout: '' }]).nodes[0]?.timeout).toBeNull();
  });

  it('keeps a stored value the seconds field cannot say, untouched', () => {
    expect(timeoutText('00:00:00.0000001')).toBe('00:00:00.0000001');
    expect(timeoutValue('00:00:00.0000001', '00:00:00.0000001')).toBe('00:00:00.0000001');
  });

  it('keeps an agent-only chain an agent list, and builds no request that would drop its step timeouts', () => {
    // Phase 196 decision (K-901 unchanged): a step timeout does not turn an agent list into a
    // node list - that change would make a waiting run unresumable. A timeout the agent list
    // cannot keep holds the save back instead of being dropped by the full replace.
    expect(hasStepTimeouts([agent('a'), agent('b')])).toBe(false);
    expect(hasStepTimeouts([agent('a', '300'), agent('b')])).toBe(true);
    expect(canonicalForm('Sequential', [agent('a', '300'), agent('b')])).toBe('agentNames');
    expect(hasStrandedTimeouts('Sequential', [agent('a', '300'), agent('b')])).toBe(true);
    expect(hasStrandedTimeouts('Sequential', [agent('a', '300'), fn('tts')])).toBe(false);
    expect(hasStrandedTimeouts('Concurrent', [agent('a', '300'), agent('b')])).toBe(true);
    expect(() => chainBody('Sequential', [agent('a', '300'), agent('b')])).toThrow('cannot keep');
    expect(chainBody('Sequential', [agent('a'), agent('b')])).toStrictEqual({ agentNames: ['a', 'b'], nodes: [] });
  });

  it('does not count a timeout as a change of shape: a waiting run stays resumable', () => {
    expect(shapeOf('Sequential', [agent('a'), fn('tts')])).toBe(shapeOf('Sequential', [agent('a', '30'), fn('tts', '', '2')]));
  });

  it('reads seconds, decimal seconds, hh:mm:ss and days, and refuses anything else', () => {
    expect(parseTimeout('')).toEqual({ ok: true, value: null });
    expect(parseTimeout('90')).toEqual({ ok: true, value: '00:01:30' });
    expect(parseTimeout('2.5')).toEqual({ ok: true, value: '00:00:02.500' });
    expect(parseTimeout('90000')).toEqual({ ok: true, value: '1.01:00:00' });
    expect(parseTimeout('00:30:00')).toEqual({ ok: true, value: '00:30:00' });
    expect(parseTimeout('1.02:03:04.5')).toEqual({ ok: true, value: '1.02:03:04.5' });

    expect(parseTimeout('ten')).toEqual({ ok: false });
    expect(parseTimeout('-5')).toEqual({ ok: false });
    expect(parseTimeout('1,5')).toEqual({ ok: false });
    expect(parseTimeout('2.5555')).toEqual({ ok: false });
  });

  it('builds no request from a timeout it cannot read, and holds the save back', () => {
    expect(hasInvalidTimeouts([agent('a', 'soon')])).toBe(true);
    expect(hasInvalidTimeouts([agent('a', '30'), fn('tts', '', '')])).toBe(false);
    expect(() => chainBody('Sequential', [agent('a', 'soon'), fn('tts')])).toThrow('neither seconds nor hh:mm:ss');
  });
});

// Phase 200: a step can pin its stored agent to one version of it.
describe('agent version pins', () => {
  const pinned = (name: string, agentVersion: number | null, timeout = ''): Step => ({ ...agent(name, timeout), agentVersion });

  it('reads a stored pin, and leaves an unpinned or missing one empty', () => {
    const [a, b, c] = stepsOf({
      agentNames: [],
      nodes: [
        { name: 'a', kind: 'Agent', settings: null, agentVersion: 2 },
        { name: 'b', kind: 'Agent', settings: null, agentVersion: null },
        { name: 'c', kind: 'Agent', settings: null },
      ],
    });

    expect(a?.agentVersion).toBe(2);
    expect(b?.agentVersion).toBeUndefined();
    expect(c?.agentVersion).toBeUndefined();

    // An agent list has nowhere to hold one.
    expect(stepsOf({ agentNames: ['x'], nodes: [] })).toEqual([agent('x')]);
  });

  it('counts a pin, and nothing else, as one', () => {
    expect(hasPins([agent('a'), pinned('b', 1)])).toBe(true);
    expect(hasPins([agent('a'), pinned('b', null)])).toBe(false);
    expect(hasPins([agent('a', '30'), fn('tts', '{"a":1}')])).toBe(false);
    expect(hasPins([])).toBe(false);
  });

  it('writes a Sequential chain with a pin as nodes, and an unpinned agent-only chain as agentNames', () => {
    expect(canonicalForm('Sequential', [pinned('a', 2), agent('b')])).toBe('nodes');
    expect(canonicalForm('Sequential', [pinned('a', null), agent('b')])).toBe('agentNames');

    // Another pattern never gets nodes: the server refuses them.
    expect(canonicalForm('Concurrent', [pinned('a', 2), agent('b')])).toBe('agentNames');

    expect(chainBody('Sequential', [pinned('a', 2), agent('b')])).toStrictEqual({
      agentNames: [],
      nodes: [
        { name: 'a', kind: 'Agent', settings: null, timeout: null, agentVersion: 2 },
        { name: 'b', kind: 'Agent', settings: null, timeout: null, agentVersion: null },
      ],
    });
    expect(chainBody('Sequential', [agent('a'), agent('b')])).toStrictEqual({ agentNames: ['a', 'b'], nodes: [] });
  });

  it('writes an untouched pin back as stored, a changed one as typed and a cleared one as null', () => {
    const steps = stepsOf({
      agentNames: [],
      nodes: [
        { name: 'a', kind: 'Agent', settings: null, agentVersion: 2 },
        { name: 'b', kind: 'Agent', settings: null, agentVersion: null },
      ],
    });
    const versionsOf = (chain: readonly Step[]): (number | string | null | undefined)[] =>
      chainBody('Sequential', chain).nodes.map((node) => node.agentVersion);

    expect(versionsOf(steps)).toEqual([2, null]);
    expect(versionsOf(steps.map((step) => ({ ...step, agentVersion: step.name === 'b' ? 5 : step.agentVersion })))).toEqual([2, 5]);

    // Clearing the only pin leaves an agent-only chain, which goes back to the agent list.
    expect(chainBody('Sequential', steps.map((step) => ({ ...step, agentVersion: null })))).toStrictEqual({
      agentNames: ['a', 'b'],
      nodes: [],
    });
  });

  it('sends the pin a node carries whatever its kind, so a value the server sent is never dropped', () => {
    const [step] = stepsOf({ agentNames: [], nodes: [{ name: 'tts', kind: 'Function', settings: null, agentVersion: 4 }] });

    expect(chainBody('Sequential', [step!]).nodes[0]?.agentVersion).toBe(4);
  });

  it('builds no request that would drop a pin: an agent list cannot hold one', () => {
    expect(() => chainBody('Concurrent', [pinned('a', 2), agent('b')])).toThrow('pins its agent to a version');
  });

  it('keeps a step timeout beside a pin: the pin makes the node list, so nothing is stranded', () => {
    expect(hasStrandedTimeouts('Sequential', [pinned('a', 2, '300'), agent('b')])).toBe(false);
    expect(chainBody('Sequential', [pinned('a', 2, '300'), agent('b')]).nodes[0]?.timeout).toBe('00:05:00');

    // Take the pin away and the timeout has no node to live on again.
    expect(hasStrandedTimeouts('Sequential', [pinned('a', null, '300'), agent('b')])).toBe(true);
  });

  it('does not count a pin as a change of shape: a waiting run keeps its graph', () => {
    expect(shapeOf('Sequential', [pinned('a', 2), agent('b')])).toBe(shapeOf('Sequential', [agent('a'), agent('b')]));
    expect(shapeOf('Sequential', [pinned('a', 2), agent('b')])).toBe(shapeOf('Sequential', [pinned('a', 7), agent('b')]));
  });
});
