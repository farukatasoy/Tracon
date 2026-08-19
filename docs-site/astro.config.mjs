// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import mermaid from 'astro-mermaid';

import apiSidebar from './src/generated/api-sidebar.json' with { type: 'json' };
import httpApiSidebar from './src/generated/http-api-sidebar.json' with { type: 'json' };

// GitHub Pages project site. `base` is part of every generated link, and the API
// reference generator writes the same prefix — change both together.
export default defineConfig({
  site: 'https://farukatasoy.github.io',
  base: '/AgentPrism/',
  trailingSlash: 'always',
  vite: {
    build: {
      // Mermaid already loads its diagram parser on demand. Its largest shared
      // parser chunk is about 662 KB minified and 142 KB gzip, so the default raw
      // 500 KB warning does not describe the network cost of normal documentation
      // pages. Keep a measured ceiling instead of suppressing all size warnings.
      chunkSizeWarningLimit: 700,
    },
  },
  integrations: [
    // Diagrams are written as mermaid fences and rendered in the browser, with the
    // theme following Starlight's light/dark switch. It must be listed BEFORE
    // starlight so the fences are transformed before Starlight's code highlighting
    // claims them.
    mermaid({ theme: 'neutral', autoTheme: true }),
    starlight({
      title: 'AgentPrism',
      favicon: '/favicon.svg',
      description:
        'An agent control plane for .NET, built on the Microsoft Agent Framework. ' +
        'Define agents, run them, and see exactly what they did.',
      head: [
        { tag: 'meta', attrs: { name: 'theme-color', content: '#10151f' } },
        {
          tag: 'meta',
          attrs: {
            property: 'og:image',
            content: 'https://farukatasoy.github.io/AgentPrism/screenshots/dashboard.png',
          },
        },
        {
          tag: 'meta',
          attrs: {
            name: 'twitter:image',
            content: 'https://farukatasoy.github.io/AgentPrism/screenshots/dashboard.png',
          },
        },
        { tag: 'meta', attrs: { name: 'twitter:card', content: 'summary_large_image' } },
      ],
      social: [
        { icon: 'github', label: 'GitHub', href: 'https://github.com/farukatasoy/AgentPrism' },
      ],
      editLink: {
        baseUrl: 'https://github.com/farukatasoy/AgentPrism/edit/main/docs-site/',
      },
      customCss: ['./src/styles/site.css'],
      lastUpdated: true,
      sidebar: [
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
            { label: 'Security', slug: 'getting-started/security' },
            { label: 'Runs and recording', slug: 'concepts/runs' },
            { label: 'Reliable runs', slug: 'guides/reliability' },
            { label: 'Observability and cost', slug: 'guides/observability' },
            { label: 'Governance', slug: 'concepts/governance' },
            { label: 'Production deployment', slug: 'guides/production' },
            { label: 'Troubleshooting', slug: 'troubleshooting' },
          ],
        },
        {
          label: 'The console',
          items: [{ label: 'UI guide', slug: 'ui' }],
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
      ],
    }),
  ],
});
