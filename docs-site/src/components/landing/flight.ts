// The sample flight the landing page follows, declared once.
//
// Every id, time, hash and snippet on the home page comes from this module, and
// it runs at build time only: no client script imports it. The values are a
// SAMPLE, and the page labels them so. What is not a sample is their shape: the
// event wire names, statuses and audit actions are the ones the product really
// writes, and `flight.events.json` holds the recording that a functional test
// in the .NET suite replays against a real host. If the product changes, that
// test fails; if this module drifts from the recording, the build fails.
//
// Hashes are computed here, never written as literals, with the canonical form
// of the product's audit hasher. Before any value reaches the page, a probe entry
// must hash to the value the product's own hasher gave the .NET test.

import { createHash } from 'node:crypto';

import recorded from './flight.events.json';

const sha256 = (text: string) => createHash('sha256').update(text, 'utf8').digest('hex');

// ---------------------------------------------------------------------------
// Time: one T+ clock, measured from the first request.

const T0 = '2026-09-24T09:12:03.418Z';

/** `T+mm:ss.fff` from the first request to `iso`. */
export function tPlus(iso: string): string {
  const elapsed = Date.parse(iso) - Date.parse(T0);
  const minutes = Math.floor(elapsed / 60_000);
  const seconds = Math.floor(elapsed / 1000) % 60;
  const millis = elapsed % 1000;
  return `T+${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}.${String(millis).padStart(3, '0')}`;
}

const at = {
  request: T0,
  resolved: '2026-09-24T09:12:03.421Z',
  started: '2026-09-24T09:12:03.466Z',
  // The first model call of the run: the recorder counts the request's messages by source.
  context: '2026-09-24T09:12:03.471Z',
  // A queued run is not streamed: the recorder writes the turn's tool events
  // and message after the model returns, just before the run is held.
  statusCall: '2026-09-24T09:12:04.818Z',
  statusResult: '2026-09-24T09:12:04.819Z',
  cancelCall: '2026-09-24T09:12:04.820Z',
  held: '2026-09-24T09:12:04.824Z',
  restart: '2026-09-24T09:13:33.418Z',
  decided: '2026-09-24T09:16:15.718Z',
  completed: '2026-09-24T09:16:17.536Z',
} as const;

export const clock = {
  resolved: tPlus(at.resolved),
  held: tPlus(at.held),
  restart: tPlus(at.restart).slice(0, -4),
  decided: tPlus(at.decided),
  completed: tPlus(at.completed),
};

// ---------------------------------------------------------------------------
// Ids: UUID v7, whose time prefix matches the time the page shows.

function uuidV7(iso: string, payloadHex: string): string {
  if (!/^[0-9a-f]{20}$/.test(payloadHex)) throw new Error(`A sample id payload must be 20 hex digits: ${payloadHex}`);
  const bytes = Buffer.alloc(16);
  const millis = BigInt(Date.parse(iso));
  for (let i = 0; i < 6; i++) bytes[i] = Number((millis >> BigInt(40 - 8 * i)) & 0xffn);
  Buffer.from(payloadHex, 'hex').copy(bytes, 6, 0, 10);
  bytes[6] = (bytes[6] & 0x0f) | 0x70;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = bytes.toString('hex');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

/** `01a0d2af…a47e`: the first eight and the last four characters. */
export const short = (value: string) => `${value.slice(0, 8)}…${value.slice(-4)}`;

const ids = {
  tenant: uuidV7('2026-09-24T08:41:12.504Z', '3e218f214d6a0c9e5b12'),
  auditTenant: uuidV7('2026-09-24T08:41:12.506Z', '0c7a9d156b2e8f4a1c03'),
  key: uuidV7('2026-09-24T08:57:40.116Z', 'd31a8c526a4f1e9d2b10'),
  auditKey: uuidV7('2026-09-24T08:57:40.118Z', 'e029b3c71d4a0f6c2185'),
  run: uuidV7(at.request, 'b529a0e3f5d2c8b1a47e'),
  approval: uuidV7(at.held, 'c4eb1d75e08c3a9f4621'),
  auditDecision: uuidV7(at.decided, 'f19a6e24c8b3d1e0f571'),
};

export const sample = {
  agent: 'support',
  tenant: 'acme',
  session: 's-7f3a',
  keyPrefix: 'ap_acme_Q7fk',
  actor: 'ops@acme.test',
  order: 'ORD-7',
  message: 'Please cancel ORD-7. I ordered it twice.',
  idempotencyKey: 'ord-7-cancel-1',
  runId: ids.run,
  approvalId: ids.approval,
};

// ---------------------------------------------------------------------------
// Run R1: the event tape. Its wire names must equal the recording.

export interface TapeRow {
  seq: number;
  type: string;
  t: string;
  detail: string;
  held: boolean;
}

// What each recorded event shows: its text, or its payload when the text is
// empty, in the form the product stores. The model's second turn is a tool call
// with no text, and the held call is named only in the closing event's payload.
const rowFor: Record<string, (tool: string | null) => Omit<TapeRow, 'seq' | 'type' | 'held'>> = {
  'run.started': () => ({ t: tPlus(at.started), detail: `"${sample.message}"` }),
  // The reserved `tracon.context` event every run records on its first model call (its text, not a payload).
  custom: () => ({ t: tPlus(at.context), detail: '"context: 1 messages"' }),
  'tool.invoking': (tool) => ({ t: tPlus(at.statusCall), detail: `${tool} orderId=${sample.order}` }),
  'tool.invoked': () => ({ t: tPlus(at.statusResult), detail: `"Order ${sample.order} has shipped."` }),
  'message.completed': () => ({ t: tPlus(at.cancelCall), detail: '"" · a tool call, no text' }),
  'run.awaiting-input': () => ({ t: tPlus(at.held), detail: '[{"requestId":"…","toolName":"cancel_order"}]' }),
};

export const tape: TapeRow[] = recorded.r1.map((type, seq) => {
  const make = rowFor[type];
  if (!make) {
    throw new Error(
      `flight.events.json records "${type}" in run R1, and the landing tape has no row for it. ` +
        'Add the row to flight.ts, or re-record the flight.',
    );
  }
  return { seq, type, held: type === 'run.awaiting-input', ...make(recorded.r1Tools[seq] ?? null) };
});

if (!tape.at(-1)?.held || recorded.r1Status !== 'AwaitingApproval') {
  throw new Error('The landing story needs R1 to end held in AwaitingApproval; the recording says otherwise.');
}
if (recorded.r2Status !== 'Completed' || recorded.sameSession !== true) {
  throw new Error('The landing story needs R2 Completed in the same session; the recording says otherwise.');
}

/** The status the recording gave the run the decision opened. */
export const continuedStatus = recorded.r2Status;

// ---------------------------------------------------------------------------
// Audit trail of tenant acme: the product's canonical form, hashed here.

interface AuditSample {
  id: string;
  action: string;
  entity: string;
  after: string;
  createdAt: string;
}

const quote = (value: string | null) =>
  value === null
    ? 'null'
    : `"${value.replace(/\\/g, '\\\\').replace(/"/g, '\\"').replace(/\n/g, '\\n').replace(/\r/g, '\\r')}"`;

// The hashed time is UTC truncated to exactly six fractional digits.
const hashedTime = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{6}Z$/;

function canonical(previousHash: string | null, actor: string, entry: Omit<AuditSample, 'id'>): string {
  if (!hashedTime.test(entry.createdAt)) {
    throw new Error(`Audit sample ${entry.action} has createdAt ${entry.createdAt}; the product hashes yyyy-MM-ddTHH:mm:ss.ffffffZ.`);
  }
  return (
    `{"prevHash":${quote(previousHash)},"tenantId":${quote(sample.tenant)},"actor":${quote(actor)},` +
    `"action":${quote(entry.action)},"entity":${quote(entry.entity)},"before":null,` +
    `"after":${quote(entry.after)},"createdAt":"${entry.createdAt}"}`
  );
}

// The .NET test hashes this exact entry with the product's own hasher and
// records the result as canonicalSample. It has a previous hash, quotes and an
// escaped '+', the parts a canonical form most easily gets wrong. If the two
// disagree, nothing below holds.
const probe = sha256(
  canonical('548f50472d6b293459b1f66d8dc2c347d2506aa7b5db9fbe859844efcb65d746', sample.actor, {
    action: 'tenant.create',
    entity: 'tenant:acme',
    after: '{"id":"01a0d5a1-dde5-7c5e-bd60-3fc0cbcbbe46","slug":"acme","displayName":"Acme","createdAt":"2026-09-24T22:55:50.24598\\u002B00:00"}',
    createdAt: '2026-09-24T22:55:50.484878Z',
  }),
);
if (probe !== recorded.canonicalSample) {
  throw new Error(
    `The landing's audit canonical form hashes the probe entry to ${probe}; the product's hasher gave ${recorded.canonicalSample}.`,
  );
}

const auditSamples: AuditSample[] = [
  {
    id: ids.auditTenant,
    action: 'tenant.create',
    entity: `tenant:${sample.tenant}`,
    // The product's JSON writer escapes '+', so the stored text carries the six
    // characters \u002B; the hash covers the stored text.
    after: `{"id":"${ids.tenant}","slug":"acme","displayName":"Acme","createdAt":"2026-09-24T08:41:12.504331\\u002B00:00"}`,
    createdAt: '2026-09-24T08:41:12.506187Z',
  },
  {
    id: ids.auditKey,
    action: 'apikey.create',
    entity: `apikey:${ids.key}`,
    after: `{"name":"support-desk","keyPrefix":"${sample.keyPrefix}","scopes":["RunsRead","RunsWrite"]}`,
    createdAt: '2026-09-24T08:57:40.118204Z',
  },
  {
    id: ids.auditDecision,
    action: 'approval.decision',
    entity: 'tool:cancel_order',
    after: `{"approved":false,"approvalId":"${ids.approval}"}`,
    createdAt: '2026-09-24T09:16:15.718530Z',
  },
];

// A time inside a stored JSON text drops trailing zeros, unlike the hashed one.
for (const entry of auditSamples) {
  if (/"createdAt":"[^"]*\.\d*0\\u002B/.test(entry.after)) {
    throw new Error(`Audit sample ${entry.action} keeps a trailing zero the product's JSON writer drops.`);
  }
}

if (auditSamples.map((entry) => entry.action).join() !== recorded.auditActions.join()) {
  throw new Error('The landing audit chain differs from the recorded audit actions of tenant acme.');
}

export interface ChainEntry extends AuditSample {
  previousHash: string | null;
  hash: string;
}

export const chain: ChainEntry[] = [];
for (const entry of auditSamples) {
  const previousHash = chain.at(-1)?.hash ?? null;
  chain.push({ ...entry, previousHash, hash: sha256(canonical(previousHash, sample.actor, entry)) });
}

const decision = chain.at(-1)!;
export const forged = {
  hash: sha256(
    canonical(decision.previousHash, sample.actor, {
      ...decision,
      after: decision.after.replace('"approved":false', '"approved":true'),
    }),
  ),
};

export const verify = {
  status: recorded.verify.status,
  entriesChecked: recorded.verify.entriesChecked,
  failingId: decision.id,
};

if (verify.entriesChecked !== chain.length) {
  throw new Error('The recorded verify count differs from the landing audit chain.');
}

export const productionProfile = { firstLine: recorded.productionProfileFirstLine };

// ---------------------------------------------------------------------------
// Snippets: C# shown on the page, with a small build-time highlighter.

export interface Snippet {
  file: string;
  lines: string[];
  marks: number[];
}

export const snippets = {
  twoLines: {
    file: 'Program.cs',
    lines: [
      'using Tracon;',
      '',
      'var builder =',
      '    WebApplication.CreateBuilder(args);',
      'builder.AddTracon().UseUI();',
      '',
      'var app = builder.Build();',
      'app.MapTracon(); // at /tracon',
      'app.Run();',
    ],
    marks: [4, 7],
  },
  tenancy: {
    file: 'Program.cs',
    lines: [
      'builder.AddTracon()',
      '    .UseTenancy(o =>',
      '    {',
      '        o.Enabled = true;',
      '        o.ClaimType = "tenant_id";',
      '    });',
    ],
    marks: [3, 4],
  },
  tool: {
    file: 'Tools/OrderTools.cs',
    lines: [
      '[TraconTool("cancel_order",',
      '    "Cancels an order.",',
      '    RequiresApproval = true,',
      '    Effect = ToolEffect.Destructive)]',
      'public static string CancelOrder(',
      '    [Description("The order id.")]',
      '    string orderId)',
      '    => $"Order {orderId} is canceled.";',
    ],
    marks: [2],
  },
  persistence: {
    file: 'Program.cs',
    lines: ['builder.AddTracon()', '    .UsePostgreSql(connectionString);'],
    marks: [1],
  },
  profile: {
    file: 'Program.cs',
    lines: ['builder.AddTracon()', '    .RequireProductionProfile();'],
    marks: [1],
  },
} satisfies Record<string, Snippet>;

const API = new Set([
  'AddTracon',
  'UseUI',
  'MapTracon',
  'UseTenancy',
  'UsePostgreSql',
  'TraconTool',
  'RequiresApproval',
  'RequireProductionProfile',
]);
const KEYWORDS = new Set(['using', 'var', 'true', 'public', 'static', 'string']);

const escapeHtml = (text: string) => text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

/** One line of C# as HTML: API names, keywords, strings and comments get a class. */
export function highlight(line: string): string {
  const token = /(\/\/.*$)|(\$?"(?:[^"\\]|\\.)*")|([A-Za-z_]\w*)|([^A-Za-z_$"/]+|[$"/])/g;
  let html = '';
  for (const [, comment, text, word, other] of line.matchAll(token)) {
    if (comment) html += `<span class="c">${escapeHtml(comment)}</span>`;
    else if (text) html += `<span class="s">${escapeHtml(text)}</span>`;
    else if (word && API.has(word)) html += `<span class="t">${word}</span>`;
    else if (word && KEYWORDS.has(word)) html += `<span class="k">${word}</span>`;
    else html += escapeHtml(word ?? other ?? '');
  }
  return html;
}
