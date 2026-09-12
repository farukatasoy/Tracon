import type { WorkflowEdgeKind, WorkflowNodeKind } from '@tracon/client';
import type { WorkflowGraph } from './server-types';

/**
 * Layered layout for a workflow graph.
 *
 * Hand written rather than pulled from a graph library, for the same reason the
 * trace waterfall is hand drawn: the bundle has a hard gzip budget enforced at
 * build time (decision K-002) and mermaid.js alone costs around 100 KB gzipped —
 * roughly 40% of the budget for a single screen.
 *
 * The layout is deliberately simple: one column per layer, nodes stacked inside
 * it. That draws chains, fan-outs and fan-ins correctly. Cyclic patterns
 * (`Handoff`, `GroupChat`) route their back edges under the row instead of
 * through it. When a graph is too tangled to read, the escape hatch is the
 * "Copy Mermaid" button — the server hands us Microsoft Agent Framework's own
 * Mermaid text, which any external tool can lay out properly.
 */

/** Node box width in SVG units. */
export const NODE_WIDTH = 148;

/** Node box height in SVG units. */
export const NODE_HEIGHT = 40;

const LAYER_GAP = 72;
const ROW_GAP = 18;
const PADDING = 16;

/** Extra room under the graph so back edges have somewhere to run. */
const BACK_EDGE_LANE = 26;

export interface LaidOutNode {
  id: string;
  label: string;
  kind: WorkflowNodeKind;
  agentName: string | null;
  layer: number;
  x: number;
  y: number;
}

export interface LaidOutEdge {
  from: string;
  to: string;
  kind: WorkflowEdgeKind;
  /** SVG path data. */
  path: string;
  /** True when the edge points at the same layer or an earlier one — a loop. */
  backwards: boolean;
}

export interface GraphLayout {
  nodes: LaidOutNode[];
  edges: LaidOutEdge[];
  width: number;
  height: number;
}

/**
 * Assigns every node a layer.
 *
 * Breadth-first from the start node, so a node sits one column right of the
 * earliest node that can reach it. Nodes the start cannot reach are not
 * dropped — they are placed in a trailing column. Hiding them would silently
 * lose part of a graph that really does exist.
 */
export function computeLayers(graph: WorkflowGraph): Map<string, number> {
  const outgoing = new Map<string, string[]>();

  for (const edge of graph.edges) {
    const targets = outgoing.get(edge.from);

    if (targets === undefined) {
      outgoing.set(edge.from, [edge.to]);
    } else {
      targets.push(edge.to);
    }
  }

  const known = new Set(graph.nodes.map((node) => node.id));
  const layers = new Map<string, number>();
  const start = known.has(graph.startExecutorId) ? graph.startExecutorId : graph.nodes[0]?.id;

  if (start === undefined) {
    return layers;
  }

  layers.set(start, 0);

  let frontier = [start];

  while (frontier.length > 0) {
    const next: string[] = [];

    for (const id of frontier) {
      const depth = layers.get(id) ?? 0;

      for (const target of outgoing.get(id) ?? []) {
        // A node already placed keeps its column. Re-placing it on a later
        // visit would push every cycle one column further on each pass and
        // never settle.
        if (!known.has(target) || layers.has(target)) {
          continue;
        }

        layers.set(target, depth + 1);
        next.push(target);
      }
    }

    frontier = next;
  }

  let trailing = Math.max(-1, ...layers.values()) + 1;

  for (const node of graph.nodes) {
    if (!layers.has(node.id)) {
      layers.set(node.id, trailing);
      trailing += 1;
    }
  }

  return layers;
}

/** Places every node and routes every edge. */
export function layoutGraph(graph: WorkflowGraph): GraphLayout {
  const layers = computeLayers(graph);
  const columns = new Map<number, string[]>();

  for (const node of graph.nodes) {
    const layer = layers.get(node.id) ?? 0;
    const column = columns.get(layer);

    if (column === undefined) {
      columns.set(layer, [node.id]);
    } else {
      column.push(node.id);
    }
  }

  const tallest = Math.max(1, ...[...columns.values()].map((column) => column.length));
  const height = PADDING * 2 + tallest * NODE_HEIGHT + (tallest - 1) * ROW_GAP;
  const layerCount = Math.max(1, ...[...columns.keys()].map((layer) => layer + 1));

  const placed = new Map<string, LaidOutNode>();

  for (const node of graph.nodes) {
    const layer = layers.get(node.id) ?? 0;
    const column = columns.get(layer) ?? [];
    const index = column.indexOf(node.id);

    // Each column is centred vertically, so a single node in a tall graph does
    // not cling to the top edge.
    const columnHeight = column.length * NODE_HEIGHT + (column.length - 1) * ROW_GAP;
    const top = (height - columnHeight) / 2;

    placed.set(node.id, {
      id: node.id,
      label: node.label,
      kind: node.kind,
      agentName: node.agentName ?? null,
      layer,
      x: PADDING + layer * (NODE_WIDTH + LAYER_GAP),
      y: top + index * (NODE_HEIGHT + ROW_GAP),
    });
  }

  const edges: LaidOutEdge[] = [];

  for (const edge of graph.edges) {
    const from = placed.get(edge.from);
    const to = placed.get(edge.to);

    // An edge pointing at a node that is not in the graph cannot be drawn.
    // Dropping it is right: the alternative is a line to nowhere.
    if (from === undefined || to === undefined) {
      continue;
    }

    const backwards = to.layer <= from.layer;

    edges.push({
      from: edge.from,
      to: edge.to,
      kind: edge.kind,
      backwards,
      path: backwards ? backwardPath(from, to, height) : forwardPath(from, to),
    });
  }

  return {
    nodes: [...placed.values()],
    edges,
    width: PADDING * 2 + layerCount * NODE_WIDTH + (layerCount - 1) * LAYER_GAP,
    height: height + (edges.some((edge) => edge.backwards) ? BACK_EDGE_LANE : 0),
  };
}

/** A left-to-right curve between two boxes in different columns. */
function forwardPath(from: LaidOutNode, to: LaidOutNode): string {
  const startX = from.x + NODE_WIDTH;
  const startY = from.y + NODE_HEIGHT / 2;
  const endX = to.x;
  const endY = to.y + NODE_HEIGHT / 2;
  const bend = Math.max(18, (endX - startX) / 2);

  return `M ${round(startX)} ${round(startY)} C ${round(startX + bend)} ${round(startY)}, ${round(endX - bend)} ${round(endY)}, ${round(endX)} ${round(endY)}`;
}

/**
 * A loop back to the same column or an earlier one.
 *
 * Routed under the whole row rather than straight across: `Handoff` and
 * `GroupChat` send every participant back to their host, and drawing those
 * lines through the boxes would make the busiest part of the graph unreadable.
 */
function backwardPath(from: LaidOutNode, to: LaidOutNode, rowHeight: number): string {
  const startX = from.x + NODE_WIDTH / 2;
  const startY = from.y + NODE_HEIGHT;
  const endX = to.x + NODE_WIDTH / 2;
  const endY = to.y + NODE_HEIGHT;
  const lane = rowHeight + BACK_EDGE_LANE / 2;

  return `M ${round(startX)} ${round(startY)} C ${round(startX)} ${round(lane)}, ${round(endX)} ${round(lane)}, ${round(endX)} ${round(endY)}`;
}

function round(value: number): number {
  return Math.round(value * 10) / 10;
}

/**
 * Live state of a node, folded from the run's event stream.
 *
 * `ExecutorInvoked` / `ExecutorCompleted` / `ExecutorFailed` carry the executor
 * id in their `text` field, and graph node ids are the same strings, so the two
 * join without any lookup table.
 */
export type NodeState = 'idle' | 'running' | 'done' | 'failed';

export function foldNodeStates(
  events: readonly { type: string; text?: string | null }[],
): Map<string, NodeState> {
  const states = new Map<string, NodeState>();

  for (const event of events) {
    const id = event.text;

    if (id == null || id.length === 0) {
      continue;
    }

    if (event.type === 'ExecutorInvoked') {
      // A failed node stays failed: a later invocation of the same executor in
      // a loop should not hide the failure that already happened.
      if (states.get(id) !== 'failed') {
        states.set(id, 'running');
      }
    } else if (event.type === 'ExecutorCompleted') {
      if (states.get(id) !== 'failed') {
        states.set(id, 'done');
      }
    } else if (event.type === 'ExecutorFailed') {
      states.set(id, 'failed');
    }
  }

  return states;
}
