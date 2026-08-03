import { useMemo, type ReactNode } from 'react';
import {
  NODE_HEIGHT,
  NODE_WIDTH,
  layoutGraph,
  type LaidOutNode,
  type NodeState,
} from '../lib/workflow-graph';
import { Badge, cx } from './ui';
import type { WorkflowGraph, WorkflowNodeKind } from '../lib/types';

/**
 * The compiled workflow graph, drawn as inline SVG.
 *
 * Hand drawn on purpose. mermaid.js renders the very text the server already
 * hands us, but costs around 100 KB gzipped against a 250 KB budget enforced at
 * build time (decision K-002) — 40% of the budget for one screen. The layout
 * lives in `lib/workflow-graph.ts` as pure functions so it can be unit tested
 * without a browser; this file only paints.
 *
 * Node ids are the executor ids the run events carry, so live colouring is a
 * map lookup rather than a heuristic.
 */
export function WorkflowGraphView({
  graph,
  states,
  className,
}: {
  graph: WorkflowGraph;
  /** Live node states folded from the run's events. Empty when nothing is running. */
  states?: Map<string, NodeState>;
  className?: string;
}): ReactNode {
  const layout = useMemo(() => layoutGraph(graph), [graph]);

  if (layout.nodes.length === 0) {
    return null;
  }

  return (
    // The graph can be wider than the panel; it scrolls inside its own box so
    // the page itself never scrolls sideways.
    <div className={cx('overflow-x-auto', className)}>
      <svg
        role="img"
        aria-label={`${graph.name} workflow graph`}
        data-testid="workflow-graph"
        viewBox={`0 0 ${layout.width} ${layout.height}`}
        width={layout.width}
        height={layout.height}
        className="max-w-none"
      >
        <defs>
          <marker
            id="ap-arrow"
            viewBox="0 0 8 8"
            refX="7"
            refY="4"
            markerWidth="6"
            markerHeight="6"
            orient="auto-start-reverse"
          >
            <path d="M 0 1 L 7 4 L 0 7 z" fill="var(--ap-line-strong)" />
          </marker>
        </defs>

        <g>
          {layout.edges.map((edge) => (
            <path
              key={`${edge.from}->${edge.to}`}
              d={edge.path}
              fill="none"
              stroke="var(--ap-line-strong)"
              strokeWidth={1.4}
              strokeDasharray={edge.backwards ? '4 3' : undefined}
              markerEnd="url(#ap-arrow)"
            />
          ))}
        </g>

        {layout.nodes.map((node) => (
          <NodeBox key={node.id} node={node} state={states?.get(node.id) ?? 'idle'} />
        ))}
      </svg>
    </div>
  );
}

/** Fill and text colour per node role. Shape and label carry the meaning too. */
const KIND_STYLE: Record<WorkflowNodeKind, { fill: string; stroke: string; radius: number }> = {
  Agent: { fill: tint('--ap-violet'), stroke: 'var(--ap-violet)', radius: 8 },
  Orchestration: { fill: 'var(--ap-raised)', stroke: 'var(--ap-line-strong)', radius: 8 },
  RequestPort: { fill: tint('--ap-amber'), stroke: 'var(--ap-amber)', radius: 20 },
  Output: { fill: tint('--ap-emerald'), stroke: 'var(--ap-emerald)', radius: 20 },
  Unknown: { fill: 'var(--ap-raised)', stroke: 'var(--ap-line-strong)', radius: 8 },
};

/**
 * A faint wash of a theme colour.
 *
 * Mixed at run time rather than added as new `*-soft` tokens: the palette
 * already defines both themes, and a hard-coded hex would break one of them.
 */
function tint(token: string): string {
  return `color-mix(in srgb, var(${token}) 14%, transparent)`;
}

/** Outline colour per live state. Idle keeps the node's own role colour. */
const STATE_STROKE: Record<NodeState, string | null> = {
  idle: null,
  running: 'var(--ap-cyan)',
  done: 'var(--ap-emerald)',
  failed: 'var(--ap-danger)',
};

function NodeBox({ node, state }: { node: LaidOutNode; state: NodeState }): ReactNode {
  const style = KIND_STYLE[node.kind];
  const stroke = STATE_STROKE[state] ?? style.stroke;

  return (
    <g data-testid="workflow-node" data-node-id={node.id} data-state={state}>
      <title>{`${node.id}${node.agentName === null ? '' : ` — agent ${node.agentName}`}`}</title>

      <rect
        x={node.x}
        y={node.y}
        width={NODE_WIDTH}
        height={NODE_HEIGHT}
        rx={style.radius}
        fill={style.fill}
        stroke={stroke}
        strokeWidth={state === 'idle' ? 1.2 : 2}
      />

      {state === 'running' && (
        // A quiet pulse is the only motion on the screen; it answers "which
        // step is the run sitting on right now" without reading the event list.
        <rect
          x={node.x}
          y={node.y}
          width={NODE_WIDTH}
          height={NODE_HEIGHT}
          rx={style.radius}
          fill="none"
          stroke={stroke}
          strokeWidth={2}
          className="animate-pulse"
        />
      )}

      <text
        x={node.x + NODE_WIDTH / 2}
        y={node.y + NODE_HEIGHT / 2 + 4}
        textAnchor="middle"
        fill="var(--ap-fg)"
        className="text-[11px]"
      >
        {truncate(node.label)}
      </text>
    </g>
  );
}

/** Keeps a long executor id inside its box. The full id is in the tooltip. */
function truncate(label: string): string {
  return label.length <= 20 ? label : `${label.slice(0, 19)}…`;
}

/** Explains the node shapes. Colour alone never carries meaning. */
export function WorkflowGraphLegend(): ReactNode {
  return (
    <div className="flex flex-wrap items-center gap-1.5 text-[11px] text-subtle">
      <Badge tone="accent">agent</Badge>
      <Badge>orchestration</Badge>
      <Badge tone="warn">request port</Badge>
      <Badge tone="success">output</Badge>
      <span>· dashed edges loop back</span>
    </div>
  );
}
