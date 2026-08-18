export type ClientToolHandler = (args: Record<string, unknown>) => Promise<string> | string;

/**
 * Holds the client-side tool implementations the embedding page registers.
 *
 * K2 boundary: the tool's DECLARATION (name, description, JSON schema) is
 * registered in the AgentPrism host's code (`AddClientTool`); only the
 * IMPLEMENTATION lives here, supplied by the page embedding the widget. This
 * class never receives or interprets a schema — it only maps a name the
 * server already declared to a function the page provides.
 */
export class ClientToolRunner {
  private readonly handlers = new Map<string, ClientToolHandler>();

  register(name: string, handler: ClientToolHandler): void {
    this.handlers.set(name, handler);
  }

  has(name: string): boolean {
    return this.handlers.has(name);
  }

  async run(name: string, args: Record<string, unknown>): Promise<string> {
    const handler = this.handlers.get(name);

    if (!handler) {
      throw new Error(`No handler is registered for client tool '${name}'.`);
    }

    return handler(args);
  }
}
