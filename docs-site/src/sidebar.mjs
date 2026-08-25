// The sidebar, and the link-preview image each of its sections gets.
//
// Three consumers read this file: astro.config.mjs builds the navigation from it,
// src/starlightRouteData.mjs turns it into a per-page og:image, and
// scripts/check-content.mjs asserts that every section has an image and that the
// image is on disk. Keeping one copy is the point — a section added in the config
// alone would silently inherit another section's preview.

import { existsSync, readFileSync } from 'node:fs';

// The two generated sidebars are .gitignore'd: `npm run generate` writes them, and
// `prebuild` guarantees they exist by the time astro builds. A STATIC import would
// make this module unloadable before that — and check-content.mjs imports it and is
// meant to run on a clean checkout, before the expensive generate step. Measured: a
// static import turned `npm run check:content` into ERR_MODULE_NOT_FOUND on a fresh
// clone, which would have killed the publish job before it built anything.
function generated(name) {
  const path = new URL(`./generated/${name}.json`, import.meta.url);
  return existsSync(path) ? JSON.parse(readFileSync(path, 'utf8')) : [];
}

const apiSidebar = generated('api-sidebar');
const httpApiSidebar = generated('http-api-sidebar');

export const sidebar = [
  {
    label: 'Start here',
    items: [
      { label: 'What AgentPrism is', slug: 'getting-started', badge: 'Preview' },
      { label: 'Your first agent', slug: 'getting-started/first-agent' },
      { label: 'Complete capability map', slug: 'capabilities' },
      { label: 'Architecture', slug: 'concepts' },
    ],
  },
  {
    label: 'Build agents',
    items: [
      { label: 'Agents and definitions', slug: 'concepts/agents' },
      { label: 'Model providers', slug: 'guides/model-providers' },
      { label: 'Add a tool', slug: 'getting-started/tools' },
      { label: 'Tools, skills, and MCP', slug: 'concepts/tools' },
      { label: 'Context and memory', slug: 'guides/context-and-memory' },
      { label: 'Structured output', slug: 'guides/structured-output' },
      { label: 'Attachments and multimodal', slug: 'guides/multimodal' },
      { label: 'Knowledge and RAG', slug: 'guides/knowledge' },
      { label: 'Voice and live conversation', slug: 'guides/voice' },
    ],
  },
  {
    label: 'Orchestrate',
    items: [
      { label: 'Sessions and conversations', slug: 'concepts/sessions' },
      { label: 'Workflows', slug: 'concepts/workflows' },
      { label: 'Jobs, schedules, and queues', slug: 'guides/background-work' },
    ],
  },
  {
    label: 'Test and improve',
    items: [
      { label: 'Test without model calls', slug: 'guides/testing' },
      { label: 'Evaluation and experiments', slug: 'concepts/evaluation' },
    ],
  },
  {
    label: 'Integrate and expose',
    items: [
      { label: 'OpenAI-compatible API', slug: 'guides/openai-api' },
      { label: 'MCP server and A2A', slug: 'guides/external-agents' },
      { label: 'Client-side tools and the embeddable widget', slug: 'guides/client-side-tools' },
      { label: 'Inbound triggers', slug: 'guides/inbound-triggers' },
      { label: 'HTTP API conventions', slug: 'http-api' },
    ],
  },
  {
    label: 'Operate in production',
    items: [
      { label: 'Persistence', slug: 'getting-started/persistence' },
      { label: 'Write your own store', slug: 'guides/write-your-own-store' },
      { label: 'Write your own judge', slug: 'guides/write-your-own-judge' },
      { label: 'Security', slug: 'getting-started/security' },
      { label: 'Runs and recording', slug: 'concepts/runs' },
      { label: 'Reliable runs', slug: 'guides/reliability' },
      { label: 'Observability and cost', slug: 'guides/observability' },
      { label: 'Governance', slug: 'concepts/governance' },
      { label: 'Typed client and CLI', slug: 'guides/cli' },
      { label: 'TypeScript client', slug: 'guides/typescript-client' },
      { label: 'Production deployment', slug: 'guides/production' },
      { label: 'Embedding into a host application', slug: 'guides/embedding' },
      { label: 'Troubleshooting', slug: 'troubleshooting' },
    ],
  },
  {
    label: 'The console',
    items: [{ label: 'UI guide', slug: 'ui' }],
  },
  {
    label: 'Coding agents',
    items: [{ label: 'Agent map and diagnostics', slug: 'guides/coding-agents' }],
  },
  {
    label: 'Reference',
    items: [
      { label: 'Configuration', slug: 'reference/configuration' },
      { label: 'Compatibility matrices', slug: 'reference/compatibility' },
      { label: 'Versions and upgrades', slug: 'reference/versioning' },
      { label: 'Choosing packages', slug: 'packages' },
      { label: 'Glossary', slug: 'reference/glossary' },
    ],
  },
  {
    label: 'HTTP API reference',
    collapsed: true,
    items: [...httpApiSidebar],
  },
  {
    label: 'API reference',
    collapsed: true,
    items: [{ label: 'Overview', slug: 'api' }, ...apiSidebar],
  },
];

// Four images, eleven sections. A section shares an image with the sections that
// answer the same question; a reader who pastes two reference links should see the
// reference card twice, not the dashboard twice.
export const sectionImages = {
  'Start here': 'overview',
  'Build agents': 'overview',
  Orchestrate: 'overview',
  'Test and improve': 'operate',
  'Integrate and expose': 'reference',
  'Operate in production': 'operate',
  'The console': 'console',
  'Coding agents': 'reference',
  Reference: 'reference',
  'HTTP API reference': 'reference',
  'API reference': 'reference',
};

export const defaultImage = 'overview';

/** Maps every route the sidebar reaches to the image name its section owns. */
export function imageByRoute() {
  const exact = new Map();
  // The generated reference sections list one page per package or per tag, not the
  // 900-odd member pages underneath them. Those inherit their section by prefix.
  const byPrefix = new Map();

  for (const section of sidebar) {
    const image = sectionImages[section.label];
    if (!image) continue;

    const walk = (entries) => {
      for (const entry of entries) {
        if (entry.items) {
          walk(entry.items);
          continue;
        }

        if (entry.slug) {
          exact.set(entry.slug, image);
          continue;
        }

        if (!entry.link) continue;

        // Only a generated section earns a prefix. Every hand-written page is
        // listed by slug, so inheriting one by prefix would let the last section
        // to mention `guides/` claim all of them.
        const route = entry.link.replace(/^\/|\/$/g, '');
        exact.set(route, image);

        const [head] = route.split('/');
        if (head && route !== head) byPrefix.set(head, image);
      }
    };

    walk(section.items);
  }

  return { exact, byPrefix };
}

/** The image a page should advertise. Never returns nothing. */
export function imageForRoute(route, { exact, byPrefix } = imageByRoute()) {
  return exact.get(route) ?? byPrefix.get(route.split('/')[0]) ?? defaultImage;
}
