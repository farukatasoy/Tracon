// Gives every page the link preview of the sidebar section it lives in.
//
// Starlight's `head` config is global, so a single og:image there showed the
// console dashboard for a glossary link and for an HTTP reference link alike.
// A route middleware reaches the same head array per page and needs no component
// override, which keeps Starlight upgrades cheap.

import { defineRouteMiddleware } from '@astrojs/starlight/route-data';
import { imageByRoute, imageForRoute } from './sidebar.mjs';
import { siteUrl } from '../site.config.mjs';

// Built once per process, not once per page: this runs for all 1001 routes.
const routes = imageByRoute();

export const onRequest = defineRouteMiddleware((context) => {
  const { starlightRoute } = context.locals;
  const image = `${siteUrl}social/${imageForRoute(starlightRoute.id, routes)}.png`;

  for (const tag of starlightRoute.head) {
    if (tag.attrs?.property === 'og:image' || tag.attrs?.name === 'twitter:image') {
      tag.attrs.content = image;
    }
  }
});
