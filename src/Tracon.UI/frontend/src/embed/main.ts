import { EmbedWidget } from './widget.ts';

/**
 * Auto-initializing entry point for the embeddable chat widget.
 *
 * Reads its own `<script>` tag's `data-*` attributes and mounts a floating
 * chat bubble. `document.currentScript` is reliable here because this file
 * is built as a classic (non-module) IIFE — see `vite.embed.config.ts` —
 * and therefore always runs synchronously as the tag is parsed.
 *
 * @example
 * ```html
 * <script src="https://api.example.com/tracon/embed/embed.js"
 *   data-server="https://api.example.com"
 *   data-prefix="/tracon"
 *   data-agent="shop-assistant"
 *   data-api-key="sk_..."></script>
 * <script>
 *   // Runs after the widget script, in document order.
 *   window.TraconEmbed.registerTool('read_page_title', () => document.title);
 * </script>
 * ```
 */
function main(): void {
  const script = document.currentScript as HTMLScriptElement | null;
  const dataset = script?.dataset ?? {};

  const server = dataset.server ?? (script ? new URL(script.src).origin : '');
  const prefix = dataset.prefix ?? '/tracon';
  const agent = dataset.agent;
  const apiKey = dataset.apiKey;

  if (!agent || !apiKey) {
    console.error("Tracon embed: the script tag needs 'data-agent' and 'data-api-key' attributes.");

    return;
  }

  const widget = new EmbedWidget({ server, prefix, agent, apiKey, locale: dataset.locale });

  window.TraconEmbed = {
    registerTool: (name, handler) => widget.registerTool(name, handler),
  };
}

declare global {
  interface Window {
    TraconEmbed: {
      registerTool: (name: string, handler: (args: Record<string, unknown>) => Promise<string> | string) => void;
    };
  }
}

main();
