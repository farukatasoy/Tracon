// robots.txt, generated rather than committed to public/.
//
// The file has to name the sitemap by absolute address, and a static copy in
// public/ would be one more hand-edited copy of the site address — exactly what
// site.config.mjs exists to prevent. As an endpoint it derives from the same
// declaration as every canonical link on the site.
//
// It earns its place: the site has just moved hosts and has no inbound links, so
// the sitemap is how a crawler reaches the thousand generated reference pages.
import type { APIRoute } from 'astro';

import { siteUrl } from '../../site.config.mjs';

const body = `User-agent: *
Allow: /

Sitemap: ${siteUrl}sitemap-index.xml
`;

export const GET: APIRoute = () =>
  new Response(body, { headers: { 'Content-Type': 'text/plain; charset=utf-8' } });
