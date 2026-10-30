import type { ReactNode } from 'react';
import { Button, Select, TextInput } from '../../components/ui';
import { useT } from '../../lib/i18n';
import type { HandoffEdge } from '../../lib/workflow-steps';

/**
 * Who may hand off to whom in a `Handoff` workflow: one row per edge, `from` → `to`, and
 * the reason the model reads as the description of the handoff.
 *
 * An empty list is not "no handoffs": it is the star the pattern always had (the first agent hands off
 * to every other one), and the screen says so, because a table with no rows reads as a workflow nobody
 * can hand off in. A list replaces the star, so the first row an operator adds drops the others.
 *
 * Nothing here is validated beyond "both ends are chosen": the rules (a pair once, no edge to itself,
 * the first agent has a way out, every agent is reachable) live in `WorkflowDefinitionValidator`, and a
 * copy in the console would drift. The server's message is shown by the save.
 */
export function HandoffEdges({
  agents,
  edges,
  onChange,
}: {
  /** The participants, in the order the chain lists them: an edge may connect any two. */
  agents: readonly string[];
  edges: readonly HandoffEdge[];
  onChange: (edges: HandoffEdge[]) => void;
}): ReactNode {
  const t = useT();

  const replace = (index: number, edge: HandoffEdge): void => {
    onChange(edges.map((existing, position) => (position === index ? edge : existing)));
  };

  // A Select fills its container, so each end sits in a box of its own: without one the three controls of a row
  // stacked one under the other.
  const ends = (edge: HandoffEdge, key: 'from' | 'to', position: number): ReactNode => (
    <div className="min-w-40 flex-1">
      <Select
        ariaLabel={t(key === 'from' ? 'workflowEditor.handoffFrom' : 'workflowEditor.handoffTo', { n: position + 1 })}
        value={edge[key]}
        onChange={(value) => replace(position, { ...edge, [key]: value })}
      >
        <option value="">{t('workflowEditor.chooseAgent')}</option>
        {agents.map((agent) => (
          <option key={agent} value={agent}>
            {agent}
          </option>
        ))}
      </Select>
    </div>
  );

  return (
    <fieldset className="flex flex-col gap-2" data-testid="workflow-handoffs">
      <legend className="mb-1 text-xs font-medium text-muted">{t('workflowEditor.handoffs')}</legend>

      {edges.length === 0 ? (
        <p className="text-sm text-muted" data-testid="workflow-handoffs-star">
          {t('workflowEditor.handoffsStar', { agent: agents[0] ?? '' })}
        </p>
      ) : (
        <ul className="flex flex-col gap-1.5">
          {edges.map((edge, index) => (
            <li
              // An edge has no identity of its own, and the row is a controlled form: the position is the key.
              key={index}
              data-testid="workflow-handoff"
              className="flex flex-wrap items-center gap-2 rounded-md border border-line bg-panel px-2.5 py-1.5 text-base"
            >
              {ends(edge, 'from', index)}
              <span aria-hidden="true">→</span>
              {ends(edge, 'to', index)}
              <TextInput
                className="min-w-40 flex-1"
                aria-label={t('workflowEditor.handoffReason', { n: index + 1 })}
                placeholder={t('workflowEditor.handoffReasonPlaceholder')}
                value={edge.reason}
                onChange={(event) => replace(index, { ...edge, reason: event.target.value })}
              />
              <button
                type="button"
                aria-label={t('workflowEditor.removeHandoff', { n: index + 1 })}
                className="px-1 text-muted hover:text-danger"
                onClick={() => onChange(edges.filter((_, position) => position !== index))}
              >
                ✕
              </button>
            </li>
          ))}
        </ul>
      )}

      <div>
        <Button
          testId="workflow-add-handoff"
          disabled={agents.length < 2}
          onClick={() => onChange([...edges, { from: agents[0] ?? '', to: agents[1] ?? '', reason: '' }])}
        >
          {t('workflowEditor.addHandoff')}
        </Button>
      </div>
    </fieldset>
  );
}
