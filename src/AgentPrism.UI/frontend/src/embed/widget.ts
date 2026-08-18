import { EmbedClient, extractText, findFunctionCalls, type ClientToolResult } from './client.ts';
import { resolveMessages, type EmbedMessages } from './locale.ts';
import { ClientToolRunner } from './tool-runner.ts';

const STYLES = `
  :host { all: initial; }
  * { box-sizing: border-box; font-family: system-ui, -apple-system, sans-serif; }
  .bubble {
    position: fixed; right: 20px; bottom: 20px; width: 56px; height: 56px;
    border-radius: 9999px; border: none; cursor: pointer;
    background: #6d28d9; color: #fff; font-size: 24px; line-height: 1;
    box-shadow: 0 8px 24px rgba(0,0,0,.25); z-index: 2147483000;
  }
  .panel {
    position: fixed; right: 20px; bottom: 88px; width: 340px; height: 460px;
    background: #fff; border-radius: 12px; box-shadow: 0 12px 36px rgba(0,0,0,.28);
    display: flex; flex-direction: column; overflow: hidden;
    z-index: 2147483000; color: #111827;
  }
  .panel[hidden], .bubble[hidden] { display: none; }
  .header {
    padding: 12px 14px; background: #6d28d9; color: #fff;
    display: flex; align-items: center; justify-content: space-between;
    font-weight: 600; font-size: 14px;
  }
  .header button {
    background: transparent; border: none; color: #fff; cursor: pointer; font-size: 16px;
  }
  .messages { flex: 1; overflow-y: auto; padding: 10px; display: flex; flex-direction: column; gap: 8px; }
  .msg { max-width: 85%; padding: 8px 10px; border-radius: 10px; font-size: 13px; line-height: 1.4; white-space: pre-wrap; }
  .msg.user { align-self: flex-end; background: #ede9fe; color: #4c1d95; }
  .msg.assistant { align-self: flex-start; background: #f3f4f6; color: #111827; }
  .msg.status { align-self: center; color: #6b7280; font-size: 12px; font-style: italic; }
  .composer { display: flex; gap: 6px; padding: 10px; border-top: 1px solid #e5e7eb; }
  .composer input {
    flex: 1; border: 1px solid #d1d5db; border-radius: 8px; padding: 8px 10px; font-size: 13px;
  }
  .composer button {
    border: none; border-radius: 8px; background: #6d28d9; color: #fff; padding: 8px 12px;
    font-size: 13px; cursor: pointer;
  }
  .composer button:disabled { opacity: .5; cursor: default; }
`;

export interface EmbedWidgetOptions {
  readonly server: string;
  readonly prefix: string;
  readonly agent: string;
  readonly apiKey: string;
  readonly locale?: string;
}

export class EmbedWidget {
  private readonly host: HTMLElement;
  private readonly root: ShadowRoot;
  private readonly client: EmbedClient;
  private readonly messages: EmbedMessages;
  private readonly tools = new ClientToolRunner();

  // Reserved up front, not left null until the first response: a client-side
  // tool call is recorded in SESSION history, and a sessionless run (no
  // sessionId at all) carries no history to record it in. Matches the
  // console's own pattern (Playground reserves a conversation id before its
  // first run) — see K-443.
  private readonly sessionId: string = crypto.randomUUID();

  private messageList!: HTMLDivElement;
  private input!: HTMLInputElement;
  private sendButton!: HTMLButtonElement;
  private panel!: HTMLDivElement;
  private bubble!: HTMLButtonElement;

  constructor(options: EmbedWidgetOptions) {
    this.client = new EmbedClient(options);
    this.messages = resolveMessages(options.locale);

    this.host = document.createElement('div');
    this.root = this.host.attachShadow({ mode: 'open' });
    document.body.appendChild(this.host);

    this.render();
  }

  /** Registers a client-side tool implementation. See {@link ClientToolRunner}. */
  registerTool(name: string, handler: (args: Record<string, unknown>) => Promise<string> | string): void {
    this.tools.register(name, handler);
  }

  private render(): void {
    const style = document.createElement('style');
    style.textContent = STYLES;
    this.root.appendChild(style);

    this.bubble = document.createElement('button');
    this.bubble.className = 'bubble';
    this.bubble.type = 'button';
    this.bubble.textContent = '💬';
    this.bubble.setAttribute('aria-label', this.messages.bubbleLabel);
    this.bubble.addEventListener('click', () => this.open());

    this.panel = document.createElement('div');
    this.panel.className = 'panel';
    this.panel.hidden = true;

    const header = document.createElement('div');
    header.className = 'header';

    const title = document.createElement('span');
    title.textContent = this.messages.title;

    const closeButton = document.createElement('button');
    closeButton.type = 'button';
    closeButton.textContent = '✕';
    closeButton.setAttribute('aria-label', this.messages.close);
    closeButton.addEventListener('click', () => this.close());

    header.append(title, closeButton);

    this.messageList = document.createElement('div');
    this.messageList.className = 'messages';

    const composer = document.createElement('div');
    composer.className = 'composer';

    this.input = document.createElement('input');
    this.input.type = 'text';
    this.input.placeholder = this.messages.placeholder;
    this.input.addEventListener('keydown', (event) => {
      if (event.key === 'Enter') {
        void this.send();
      }
    });

    this.sendButton = document.createElement('button');
    this.sendButton.type = 'button';
    this.sendButton.textContent = this.messages.send;
    this.sendButton.addEventListener('click', () => void this.send());

    composer.append(this.input, this.sendButton);
    this.panel.append(header, this.messageList, composer);

    this.root.append(this.bubble, this.panel);
  }

  private open(): void {
    this.panel.hidden = false;
    this.bubble.hidden = true;
    this.input.focus();
  }

  private close(): void {
    this.panel.hidden = true;
    this.bubble.hidden = false;
  }

  private appendMessage(role: 'user' | 'assistant' | 'status', text: string): void {
    const bubble = document.createElement('div');
    bubble.className = `msg ${role}`;
    bubble.textContent = text;
    this.messageList.appendChild(bubble);
    this.messageList.scrollTop = this.messageList.scrollHeight;
  }

  private setBusy(busy: boolean): void {
    this.input.disabled = busy;
    this.sendButton.disabled = busy;
  }

  private async send(): Promise<void> {
    const text = this.input.value.trim();

    if (!text) {
      return;
    }

    this.input.value = '';
    this.appendMessage('user', text);
    this.setBusy(true);

    try {
      const result = await this.client.sendMessage(text, this.sessionId);
      await this.handleResult(result);
    } catch {
      this.appendMessage('status', this.messages.requestFailed);
    } finally {
      this.setBusy(false);
    }
  }

  /**
   * Renders the response text (if any), then resolves every client-side
   * tool call the model made and sends the results back — looping until the
   * model produces a turn with no more pending calls.
   */
  private async handleResult(result: import('./client.ts').RunResult): Promise<void> {
    const finalText = extractText(result);

    if (finalText) {
      this.appendMessage('assistant', finalText);
    }

    const calls = findFunctionCalls(result);

    if (calls.length === 0) {
      return;
    }

    this.appendMessage('status', this.messages.runningTool);

    const toolResults: ClientToolResult[] = [];

    for (const call of calls) {
      if (!call.callId || !call.name) {
        continue;
      }

      if (!this.tools.has(call.name)) {
        toolResults.push({ callId: call.callId, errorMessage: this.messages.toolNotAvailable });

        continue;
      }

      try {
        const value = await this.tools.run(call.name, call.arguments ?? {});
        toolResults.push({ callId: call.callId, result: value });
      } catch (error) {
        toolResults.push({
          callId: call.callId,
          errorMessage: error instanceof Error ? error.message : String(error),
        });
      }
    }

    const next = await this.client.sendToolResults(toolResults, this.sessionId);
    await this.handleResult(next);
  }
}
