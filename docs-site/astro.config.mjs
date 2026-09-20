// @ts-check
import { readFileSync } from 'node:fs';
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import mermaid from 'astro-mermaid';

import { sidebar } from './src/sidebar.mjs';
import { base, repositoryIsPublic, repositoryUrl, site, siteUrl } from './site.config.mjs';

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
        look: 'neo',
        // `curve` is left at mermaid's own default ('basis'): measured against this
        // palette, it did not move a pixel that mattered.
        // A figure is scaled to the column rather than scrolled, so the knobs that
        // do NOT scale with the text are the ones that cost legibility: node padding
        // and rank spacing are fixed pixels, so every pixel they take is a pixel the
        // text must give back when a wide diagram is fitted. Tightening them and
        // matching the page's own type size lifts the smallest rendered text on the
        // widest figure from 9.5 px to about 12 px.
        flowchart: {
          padding: 12,
          nodeSpacing: 40,
          rankSpacing: 48,
        },
        themeVariables: {
          darkMode: false,
          fontFamily:
            "ui-sans-serif, system-ui, -apple-system, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif",
          fontSize: '17px',
          background: token('tracon-diagram-plate'),
          primaryColor: token('tracon-diagram-node'),
          primaryTextColor: token('tracon-diagram-ink'),
          primaryBorderColor: token('tracon-diagram-line'),
          secondaryColor: token('tracon-diagram-cluster'),
          secondaryTextColor: token('tracon-diagram-ink'),
          secondaryBorderColor: token('tracon-diagram-line'),
          tertiaryColor: token('tracon-diagram-cluster'),
          tertiaryTextColor: token('tracon-diagram-ink'),
          tertiaryBorderColor: token('tracon-diagram-line'),
          mainBkg: token('tracon-diagram-node'),
          nodeBorder: token('tracon-diagram-line'),
          // Flowchart paints every `.label` with this, edge labels included, so it
          // has to read on the plate as well as on a node fill.
          nodeTextColor: token('tracon-diagram-ink'),
          clusterBkg: token('tracon-diagram-cluster'),
          clusterBorder: token('tracon-diagram-line'),
          titleColor: token('tracon-diagram-ink'),
          textColor: token('tracon-diagram-ink'),
          labelColor: token('tracon-diagram-ink'),
          lineColor: token('tracon-diagram-line'),
          // Left unset, the base theme derives it by inverting `background`,
          // which lands near-black — a darker, off-palette mark next to the
          // line it terminates. Measured: #110e09 against our #5b6472 line.
          arrowheadColor: token('tracon-diagram-line'),
          edgeLabelBackground: token('tracon-diagram-plate'),
          labelBackgroundColor: token('tracon-diagram-plate'),
          actorBkg: token('tracon-diagram-node'),
          actorBorder: token('tracon-diagram-line'),
          actorTextColor: token('tracon-diagram-ink'),
          signalColor: token('tracon-diagram-ink'),
          signalTextColor: token('tracon-diagram-ink'),
          noteBkgColor: token('tracon-diagram-cluster'),
          noteBorderColor: token('tracon-diagram-line'),
          noteTextColor: token('tracon-diagram-ink'),
        },
      },
    }),
    starlight({
      title: 'Tracon',
      favicon: '/favicon.svg',
      logo: { src: './public/favicon.svg', alt: '' },
      description:
        'The production control plane for Microsoft Agent Framework — recorded runs, ' +
        'tenant isolation, tamper-evident audit trail, embedded console.',
      head: [
        // 🚨 Starlight's `favicon` emits the SVG link alone. A browser that does not
        // take an SVG icon asks for /favicon.ico by name, and a 404 there leaves the
        // tab showing whatever it cached before — the previous brand, for a reader who
        // visited before the rename. Both files are built by build-package-icon.mjs
        // from the same mark.
        { tag: 'link', attrs: { rel: 'icon', href: '/favicon.ico', sizes: '48x48' } },
        { tag: 'link', attrs: { rel: 'apple-touch-icon', href: '/apple-touch-icon.png' } },
        { tag: 'meta', attrs: { name: 'theme-color', content: token('tracon-surface') } },
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
      social: repositoryIsPublic
        ? [{ icon: 'github', label: 'GitHub', href: repositoryUrl }]
        : undefined,
      editLink: repositoryIsPublic
        ? { baseUrl: `${repositoryUrl}/edit/main/docs-site/` }
        : undefined,
      customCss: ['./src/styles/site.css'],
      components: {
        Header: './src/components/Header.astro',
        Hero: './src/components/Hero.astro',
        PageTitle: './src/components/PageTitle.astro',
        MarkdownContent: './src/components/MarkdownContent.astro',
        Footer: './src/components/Footer.astro',
      },
      routeMiddleware: './src/starlightRouteData.mjs',
      lastUpdated: true,
      sidebar,
    }),
  ],
});
