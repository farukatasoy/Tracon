import { describe, expect, it } from 'vitest';
import { computeLayers, foldNodeStates, layoutGraph, NODE_WIDTH } from './workflow-graph';
import type { WorkflowGraph, WorkflowGraphEdge, WorkflowGraphNode } from './types';

function graph(
  nodes: readonly (readonly [string, WorkflowGraphNode['kind']])[],
  edges: readonly (readonly [string, string, WorkflowGraphEdge['kind']?])[],
  startExecutorId = nodes[0]?.[0] ?? '',
): WorkflowGraph {
  return {
    name: 'test',
    startExecutorId,
    nodes: nodes.map(([id, kind]) => ({ id, label: id, kind })),
    edges: edges.map(([from, to, kind]) => ({ from, to, kind: kind ?? 'Direct' })),
    mermaid: '',
  };
}

describe('computeLayers', () => {
  it('places a chain one column per hop', () => {
    const layers = computeLayers(
      graph(
        [
          ['a', 'Agent'],
          ['b', 'Agent'],
          ['out', 'Output'],
        ],
        [
          ['a', 'b'],
          ['b', 'out'],
        ],
      ),
    );

    expect(layers.get('a')).toBe(0);
    expect(layers.get('b')).toBe(1);
    expect(layers.get('out')).toBe(2);
  });

  it('puts fan-out targets in the same column', () => {
    const layers = computeLayers(
      graph(
        [
          ['start', 'Orchestration'],
          ['a', 'Agent'],
          ['b', 'Agent'],
        ],
        [
          ['start', 'a', 'FanOut'],
          ['start', 'b', 'FanOut'],
        ],
      ),
    );

    expect(layers.get('a')).toBe(1);
    expect(layers.get('b')).toBe(1);
  });

  it('settles on a cycle instead of pushing columns forever', () => {
    // GroupChat sends every participant back to its host. A layering pass that
    // re-placed an already-placed node would never terminate.
    const layers = computeLayers(
      graph(
        [
          ['host', 'Orchestration'],
          ['a', 'Agent'],
          ['b', 'Agent'],
        ],
        [
          ['host', 'a'],
          ['host', 'b'],
          ['a', 'host'],
          ['b', 'host'],
        ],
      ),
    );

    expect(layers.get('host')).toBe(0);
    expect(layers.get('a')).toBe(1);
    expect(layers.get('b')).toBe(1);
  });

  it('still places a node the start cannot reach', () => {
    // Dropping it would silently lose part of a graph that really exists.
    const layers = computeLayers(
      graph(
        [
          ['a', 'Agent'],
          ['orphan', 'Agent'],
        ],
        [],
      ),
    );

    expect(layers.get('orphan')).toBeGreaterThan(0);
  });

  it('returns nothing for an empty graph', () => {
    expect(computeLayers(graph([], [])).size).toBe(0);
  });
});

describe('layoutGraph', () => {
  it('grows to the right, one column per layer', () => {
    const layout = layoutGraph(
      graph(
        [
          ['a', 'Agent'],
          ['b', 'Agent'],
        ],
        [['a', 'b']],
      ),
    );

    const [first, second] = layout.nodes;

    expect(first?.x).toBeLessThan(second?.x ?? 0);
    expect(layout.width).toBeGreaterThan(NODE_WIDTH * 2);
    expect(layout.height).toBeGreaterThan(0);
  });

  it('centres a single node against a taller column', () => {
    const layout = layoutGraph(
      graph(
        [
          ['start', 'Orchestration'],
          ['a', 'Agent'],
          ['b', 'Agent'],
        ],
        [
          ['start', 'a'],
          ['start', 'b'],
        ],
      ),
    );

    const start = layout.nodes.find((node) => node.id === 'start');
    const a = layout.nodes.find((node) => node.id === 'a');
    const b = layout.nodes.find((node) => node.id === 'b');

    expect(start?.y).toBeGreaterThan(a?.y ?? 0);
    expect(start?.y).toBeLessThan(b?.y ?? 0);
  });

  it('marks a loop as backwards and routes it under the row', () => {
    const layout = layoutGraph(
      graph(
        [
          ['host', 'Orchestration'],
          ['a', 'Agent'],
        ],
        [
          ['host', 'a'],
          ['a', 'host'],
        ],
      ),
    );

    const back = layout.edges.find((edge) => edge.from === 'a' && edge.to === 'host');

    expect(back?.backwards).toBe(true);
    expect(back?.path.startsWith('M ')).toBe(true);
  });

  it('drops an edge whose endpoint is not in the graph', () => {
    // A line to nowhere is worse than no line.
    const layout = layoutGraph(graph([['a', 'Agent']], [['a', 'ghost']]));

    expect(layout.edges).toHaveLength(0);
  });

  it('gives every node a path-safe finite position', () => {
    const layout = layoutGraph(
      graph(
        [
          ['a', 'Agent'],
          ['b', 'Agent'],
          ['c', 'Output'],
        ],
        [
          ['a', 'b'],
          ['b', 'c'],
        ],
      ),
    );

    for (const node of layout.nodes) {
      expect(Number.isFinite(node.x)).toBe(true);
      expect(Number.isFinite(node.y)).toBe(true);
    }

    for (const edge of layout.edges) {
      expect(edge.path).not.toContain('NaN');
    }
  });
});

describe('foldNodeStates', () => {
  it('follows a node from running to done', () => {
    const states = foldNodeStates([
      { type: 'ExecutorInvoked', text: 'a' },
      { type: 'ExecutorCompleted', text: 'a' },
    ]);

    expect(states.get('a')).toBe('done');
  });

  it('keeps a failure even when the executor runs again', () => {
    // Handoff and GroupChat re-enter the same executor; a later success must not
    // hide the failure that already happened.
    const states = foldNodeStates([
      { type: 'ExecutorInvoked', text: 'a' },
      { type: 'ExecutorFailed', text: 'a' },
      { type: 'ExecutorInvoked', text: 'a' },
      { type: 'ExecutorCompleted', text: 'a' },
    ]);

    expect(states.get('a')).toBe('failed');
  });

  it('ignores events that are not executor events', () => {
    // `MessageDelta` carries model text in the same field. Folding it in would
    // invent a node for every sentence the model streamed.
    const states = foldNodeStates([
      { type: 'MessageDelta', text: 'merhaba' },
      { type: 'ExecutorInvoked', text: null },
      { type: 'ExecutorInvoked', text: '' },
    ]);

    expect(states.size).toBe(0);
  });
});
