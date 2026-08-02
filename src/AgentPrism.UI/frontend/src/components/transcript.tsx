import { useState, type ReactNode } from 'react';
import { prettyJson } from '../lib/format';
import type { TranscriptItem } from '../lib/transcript';
import { Badge, CodeBlock, cx } from './ui';
import { CheckIcon, ChevronIcon, CrossIcon, SpinnerIcon } from './icons';

/**
 * Renders folded transcript blocks.
 *
 * A tool call is the interesting part of an agent run, so it gets a card of its
 * own with arguments and result rather than being flattened into the text.
 */
export function TranscriptView({
  items,
  streaming,
}: {
  items: readonly TranscriptItem[];
  streaming?: boolean;
}): ReactNode {
  return (
    <div className="flex flex-col gap-2.5">
      {items.map((item, index) => {
        const isLast = index === items.length - 1;

        switch (item.kind) {
          case 'text':
            return (
              <p
                key={item.id}
                className={cx(
                  'text-[13px] leading-relaxed whitespace-pre-wrap',
                  streaming === true && isLast && 'ap-stream-caret',
                )}
              >
                {item.text}
              </p>
            );

          case 'reasoning':
            return <ReasoningBlock key={item.id} text={item.text} />;

          case 'error':
            return (
              <div
                key={item.id}
                className="rounded-md border border-line bg-danger-soft px-3 py-2 text-[12px] text-danger"
              >
                {item.message}
              </div>
            );

          case 'tool':
            return <ToolCard key={item.id} item={item} />;

          default:
            return null;
        }
      })}
    </div>
  );
}

function ReasoningBlock({ text }: { text: string }): ReactNode {
  const [open, setOpen] = useState(false);

  return (
    <div className="rounded-md border border-line bg-raised">
      <button
        type="button"
        onClick={() => setOpen(!open)}
        className="flex w-full items-center gap-1.5 px-3 py-1.5 text-left text-[12px] text-muted"
      >
        <ChevronIcon className={cx('size-3 transition-transform', open && 'rotate-90')} />
        Reasoning
      </button>
      {open && (
        <p className="px-3 pb-3 text-[12px] leading-relaxed whitespace-pre-wrap text-muted">{text}</p>
      )}
    </div>
  );
}

function ToolCard({ item }: { item: Extract<TranscriptItem, { kind: 'tool' }> }): ReactNode {
  // A finished call is usually noise once you have read it; a running or failed
  // one is exactly what you opened the screen for.
  const [open, setOpen] = useState(item.state !== 'ok');

  return (
    <div
      data-testid="tool-card"
      className="overflow-hidden rounded-md border border-line bg-panel"
      style={{ borderLeft: '2px solid var(--ap-rose)' }}
    >
      <button
        type="button"
        onClick={() => setOpen(!open)}
        className="flex w-full items-center gap-2 px-3 py-2 text-left"
      >
        <ChevronIcon className={cx('size-3 shrink-0 text-subtle transition-transform', open && 'rotate-90')} />
        <span className="font-mono text-[12px] font-medium">{item.name}</span>
        <ToolState state={item.state} />
      </button>

      {open && (
        <div className="flex flex-col gap-2 border-t border-line px-3 py-2.5">
          <Section label="Arguments" body={item.args} empty="No arguments." />
          {item.error === null ? (
            <Section label="Result" body={item.result} empty="Waiting for the result…" />
          ) : (
            <Section label="Error" body={item.error} empty="" tone="danger" />
          )}
        </div>
      )}
    </div>
  );
}

function ToolState({ state }: { state: 'running' | 'ok' | 'failed' }): ReactNode {
  if (state === 'running') {
    return (
      <Badge tone="info">
        <SpinnerIcon className="size-3" />
        running
      </Badge>
    );
  }

  if (state === 'failed') {
    return (
      <Badge tone="danger">
        <CrossIcon className="size-3" />
        failed
      </Badge>
    );
  }

  return (
    <Badge tone="success">
      <CheckIcon className="size-3" />
      done
    </Badge>
  );
}

function Section({
  label,
  body,
  empty,
  tone,
}: {
  label: string;
  body: string | null;
  empty: string;
  tone?: 'danger';
}): ReactNode {
  return (
    <div>
      <span className="mb-1 block text-[11px] font-semibold tracking-wide text-subtle uppercase">
        {label}
      </span>
      {body === null || body.length === 0 ? (
        <span className={cx('text-[12px]', tone === 'danger' ? 'text-danger' : 'text-subtle')}>
          {empty}
        </span>
      ) : (
        <CodeBlock code={prettyJson(body)} maxHeight="max-h-64" />
      )}
    </div>
  );
}
