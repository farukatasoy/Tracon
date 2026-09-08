// @ts-check
import { readFileSync } from 'node:fs';
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import mermaid from 'astro-mermaid';

import { sidebar } from './src/sidebar.mjs';
import { base, site, siteUrl } from './site.config.mjs';

// The diagram palette lives in site.css and is read from there rather than
// repeated here. Mermaid needs literal colours because it does colour maths in
// JavaScript and cannot resolve a custom property, and a second copy of the
// palette would drift from the one the contrast gate checks.
const styleSheet = readFileSync(new URL('./src/styles/site.css', import.meta.url), 'utf8');

/** @param {string} name */
function token(name) {
  const match = new RegExp(`--${name}:\\s*(#[0-9a-fA-F]{3,8})\\s*;`).exec(styleSheet);
  if (!match) throw new Error(`site.css does not declare --${name}`);
  return match[1];
}

// The address is declared in site.config.mjs alone. `base` is part of every
// generated link and the API reference generator prepends the same value, so a
// literal here would be one of eight copies that nothing keeps in agreement.
export default defineConfig({
  site,
  base,
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
    // `autoTheme` is off on purpose. It would swap mermaid's built-in light and
    // dark themes and discard the palette below; the figures are theme-independent
    // by design, which is argued where the tokens are declared in site.css.
    mermaid({
      theme: 'base',
      autoTheme: false,
      mermaidConfig: {
        // `curve` and `look` are left at mermaid's own defaults ('basis' and
        // 'classic'): measured against this palette, neither moved a pixel that
        // mattered. Only the spacing knobs below did, so only they are set.
        flowchart: {
          padding: 20,
          nodeSpacing: 55,
          rankSpacing: 70,
        },
        themeVariables: {
          darkMode: false,
          fontFamily:
            "ui-sans-serif, system-ui, -apple-system, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif",
          fontSize: '15px',
          background: token('ap-diagram-plate'),
          primaryColor: token('ap-diagram-node'),
          primaryTextColor: token('ap-diagram-ink'),
          primaryBorderColor: token('ap-diagram-line'),
          secondaryColor: token('ap-diagram-cluster'),
          secondaryTextColor: token('ap-diagram-ink'),
          secondaryBorderColor: token('ap-diagram-line'),
          tertiaryColor: token('ap-diagram-cluster'),
          tertiaryTextColor: token('ap-diagram-ink'),
          tertiaryBorderColor: token('ap-diagram-line'),
          mainBkg: token('ap-diagram-node'),
          nodeBorder: token('ap-diagram-line'),
          // Flowchart paints every `.label` with this, edge labels included, so it
          // has to read on the plate as well as on a node fill.
          nodeTextColor: token('ap-diagram-ink'),
          clusterBkg: token('ap-diagram-cluster'),
          clusterBorder: token('ap-diagram-line'),
          titleColor: token('ap-diagram-ink'),
          textColor: token('ap-diagram-ink'),
          labelColor: token('ap-diagram-ink'),
          lineColor: token('ap-diagram-line'),
          // Left unset, the base theme derives it by inverting `background`,
          // which lands near-black — a darker, off-palette mark next to the
          // line it terminates. Measured: #110e09 against our #5b6472 line.
          arrowheadColor: token('ap-diagram-line'),
          edgeLabelBackground: token('ap-diagram-plate'),
          labelBackgroundColor: token('ap-diagram-plate'),
          actorBkg: token('ap-diagram-node'),
          actorBorder: token('ap-diagram-line'),
          actorTextColor: token('ap-diagram-ink'),
          signalColor: token('ap-diagram-ink'),
          signalTextColor: token('ap-diagram-ink'),
          noteBkgColor: token('ap-diagram-cluster'),
          noteBorderColor: token('ap-diagram-line'),
          noteTextColor: token('ap-diagram-ink'),
        },
      },
    }),
    starlight({
      title: 'AgentPrism',
      favicon: '/favicon.svg',
      logo: { src: './public/favicon.svg', alt: '' },
      description:
        'An agent control plane for .NET, built on the Microsoft Agent Framework. ' +
        'Define agents, run them, and see exactly what they did.',
      head: [
        { tag: 'meta', attrs: { name: 'theme-color', content: '#10151f' } },
        {
          tag: 'meta',
          attrs: {
            property: 'og:image',
            // Replaced per page by src/starlightRouteData.mjs. This value is the
            // fallback for a route the sidebar does not reach.
            content: `${siteUrl}social/overview.png`,
          },
        },
        {
          tag: 'meta',
          attrs: {
            name: 'twitter:image',
            // Replaced per page by src/starlightRouteData.mjs. This value is the
            // fallback for a route the sidebar does not reach.
            content: `${siteUrl}social/overview.png`,
          },
        },
        { tag: 'meta', attrs: { name: 'twitter:card', content: 'summary_large_image' } },
      ],
      // No `social` GitHub icon and no `editLink`. Both pointed at the repository,
      // which is private: measured 2026-08-21, github.com/farukatasoy/AgentPrism
      // answers 404 to an anonymous reader, and Starlight put an "Edit page" link on
      // all 39 hand-written pages. A link that every visitor can only fail to follow
      // is worse than no link. Restore both blocks if the repository is ever made
      // public — that is the reopening condition recorded with K-542.
      customCss: ['./src/styles/site.css'],
      routeMiddleware: './src/starlightRouteData.mjs',
      lastUpdated: true,
      sidebar,
    }),
  ],
});
