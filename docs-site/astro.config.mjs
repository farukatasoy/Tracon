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
  integrations: [
    // Diagrams are written as mermaid fences and rendered in the browser, with the
    // theme following Starlight's light/dark switch. It must be listed BEFORE
    // starlight so the fences are transformed before Starlight's code highlighting
    // claims them.
    mermaid({ theme: 'neutral', autoTheme: true }),
    starlight({
      title: 'AgentPrism',
      description:
        'An agent control plane for .NET, built on the Microsoft Agent Framework. ' +
        'Define agents, run them, and see exactly what they did.',
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
          label: 'Getting started',
          items: [
            { label: 'What AgentPrism is', slug: 'getting-started' },
            { label: 'Your first agent', slug: 'getting-started/first-agent' },
            { label: 'Adding a tool', slug: 'getting-started/tools' },
            { label: 'Persistence', slug: 'getting-started/persistence' },
            { label: 'Securing the endpoints', slug: 'getting-started/security' },
          ],
        },
        {
          label: 'Concepts',
          items: [
            { label: 'Architecture', slug: 'concepts' },
            { label: 'Agents and definitions', slug: 'concepts/agents' },
            { label: 'Runs and recording', slug: 'concepts/runs' },
            { label: 'Sessions and conversations', slug: 'concepts/sessions' },
            { label: 'Tools, skills, and MCP', slug: 'concepts/tools' },
            { label: 'Workflows', slug: 'concepts/workflows' },
            { label: 'Evaluation and experiments', slug: 'concepts/evaluation' },
            { label: 'Governance', slug: 'concepts/governance' },
          ],
        },
        {
          label: 'The console',
          items: [{ label: 'UI guide', slug: 'ui' }],
        },
        {
          label: 'HTTP API',
          items: [{ label: 'Overview', slug: 'http-api' }, ...httpApiSidebar],
        },
        {
          label: 'Packages',
          items: [{ label: 'Choosing packages', slug: 'packages' }],
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
