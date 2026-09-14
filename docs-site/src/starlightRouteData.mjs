// Gives every page the link preview of the sidebar section it lives in.
//
// Starlight's `head` config is global, so a single og:image there showed the
// console dashboard for a glossary link and for an HTTP reference link alike.
// A route middleware reaches the same head array per page and needs no component
// override, which keeps Starlight upgrades cheap.

import { defineRouteMiddleware } from '@astrojs/starlight/route-data';
import { imageByRoute, imageForRoute } from './sidebar.mjs';
import { indexingEnabled, siteUrl } from '../site.config.mjs';

// Built once per process, not once per page: this runs for every route.
const routes = imageByRoute();

export const onRequest = defineRouteMiddleware((context) => {
  const { starlightRoute } = context.locals;
  const id = starlightRoute.id;
  // The CLR reference and the wire schema describe different contracts even when
  // their short names match. Keep their search results distinguishable.
  const section = id.startsWith('http-api/schemas/') ? 'HTTP schema'
    : id.startsWith('http-api/') ? 'HTTP API'
    : id.startsWith('api/package-') ? '.NET package'
    : id.startsWith('api/') ? '.NET API' : '';
  const pageTitle = starlightRoute.entry.data.title;
  const title = section ? `${pageTitle} — ${section}` : pageTitle;
  for (const tag of starlightRoute.head) {
    if (tag.tag === 'title') tag.content = `${title} | Tracon`;
    if (tag.attrs?.property === 'og:title') tag.attrs.content = title;
    if (id === '' && tag.attrs?.property === 'og:type') tag.attrs.content = 'website';
  }
  if (id === '404') {
    starlightRoute.head = starlightRoute.head.filter((tag) =>
      tag.attrs?.rel !== 'canonical' && tag.attrs?.property !== 'og:url');
  }
  if (id === '404' || !indexingEnabled) {
    starlightRoute.head.push({ tag: 'meta', attrs: { name: 'robots', content: 'noindex' } });
  }
  if (id === '') {
    starlightRoute.head.push({ tag: 'script', attrs: { type: 'application/ld+json' }, content: JSON.stringify({
      '@context': 'https://schema.org', '@type': 'WebSite', name: 'Tracon', url: siteUrl,
    }) });
  }
  const kind = id === 'api' || id.startsWith('api/') ? '.NET API' : id.startsWith('http-api/') ? 'HTTP API' : 'Documentation';
  starlightRoute.head.push({ tag: 'meta', attrs: { 'data-pagefind-filter': 'Content[content]', content: kind } });
  const image = `${siteUrl}social/${imageForRoute(starlightRoute.id, routes)}.png`;

  for (const tag of starlightRoute.head) {
    if (tag.attrs?.property === 'og:image' || tag.attrs?.name === 'twitter:image') {
      tag.attrs.content = image;
    }
  }
});
