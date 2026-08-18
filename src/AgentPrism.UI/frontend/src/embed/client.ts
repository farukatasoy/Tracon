/** A single content item inside a run response message. */
export interface RunContent {
  readonly $type: string;
  readonly text?: string;
  readonly callId?: string;
  readonly name?: string;
  readonly arguments?: Record<string, unknown>;
}

export interface RunMessage {
  readonly role: string;
  readonly contents: readonly RunContent[];
}

export interface RunResult {
  readonly runId: string;
  readonly sessionId: string | null;
  readonly response: { readonly messages: readonly RunMessage[] };
}

export interface ClientToolResult {
  readonly callId: string;
  readonly result?: string;
  readonly errorMessage?: string;
}

export interface EmbedClientOptions {
  readonly server: string;
  readonly prefix: string;
  readonly agent: string;
  readonly apiKey: string;
}

/**
 * A minimal client for the run endpoint's NON-STREAMING branch
 * (`Idempotency-Key`). Streaming (SSE) is deliberately out of scope here: a
 * hand-rolled SSE parser would cost bundle budget the widget cannot spare,
 * and a buffered response is enough to complete a turn end to end.
 */
export class EmbedClient {
  private readonly url: string;

  constructor(private readonly options: EmbedClientOptions) {
    const server = options.server.replace(/\/$/, '');
    const prefix = options.prefix.replace(/^\/?/, '/').replace(/\/$/, '');

    this.url = `${server}${prefix}/api/agents/${encodeURIComponent(options.agent)}/run`;
  }

  async sendMessage(message: string, sessionId: string | null): Promise<RunResult> {
    return this.run({ message, sessionId: sessionId ?? undefined });
  }

  async sendToolResults(toolResults: readonly ClientToolResult[], sessionId: string): Promise<RunResult> {
    return this.run({ toolResults, sessionId });
  }

  private async run(body: Record<string, unknown>): Promise<RunResult> {
    const response = await fetch(this.url, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${this.options.apiKey}`,
        'Idempotency-Key': crypto.randomUUID(),
      },
      body: JSON.stringify(body),
    });

    if (!response.ok) {
      throw new Error(`AgentPrism run request failed with status ${response.status}.`);
    }

    return (await response.json()) as RunResult;
  }
}

/** Collects every `functionCall` content across a response's messages. */
export function findFunctionCalls(result: RunResult): RunContent[] {
  const calls: RunContent[] = [];

  for (const message of result.response.messages) {
    for (const content of message.contents) {
      if (content.$type === 'functionCall') {
        calls.push(content);
      }
    }
  }

  return calls;
}

/** Concatenates every `text` content across a response's messages, in order. */
export function extractText(result: RunResult): string {
  const parts: string[] = [];

  for (const message of result.response.messages) {
    for (const content of message.contents) {
      if (content.$type === 'text' && content.text) {
        parts.push(content.text);
      }
    }
  }

  return parts.join('\n');
}
