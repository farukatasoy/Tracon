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

import { crawlerPolicy, indexingEnabled, siteUrl } from '../../site.config.mjs';

/**
 * Pagefind's index, which is content rather than a page.
 *
 * `fragment/` holds one JSON copy of every page's text (1137 files, 4.7 MB) and
 * `index/` holds the binary term index (44 files, 1.3 MB). Neither is a document
 * anybody should be served: they are a second, worse encoding of pages the
 * crawler already has, and on a site with no inbound links they are most of what
 * a first crawl would spend itself on.
 *
 * The scripts and stylesheets beside them are deliberately NOT listed. Google's
 * own guidance is not to block resources a page needs to render, and the search
 * dialog loads them; only the data underneath is excluded.
 */
const excludedPaths = ['/pagefind/fragment/', '/pagefind/index/'];

/**
 * One group: the agents it names, then the rules they get.
 *
 * The path exclusions are repeated into EVERY group rather than written once
 * under `*`. A crawler obeys exactly one group - the most specific one that
 * matches its name - and ignores the rest, so naming GPTBot below without
 * repeating them would have quietly exempted all fourteen named crawlers from
 * the pagefind exclusion that `*` gets. Skipping the exclusions for a group that
 * is disallowed outright would be harmless but misleading, so they are carried
 * there too: a disallowed crawler reads one rule, not a contradiction.
 *
 * The exclusions are written BEFORE the blanket `Allow: /`. RFC 9309 says the most
 * specific match must win, which makes the order irrelevant to a compliant parser -
 * but not every parser is one: Python's own urllib.robotparser takes the first
 * matching rule, and with `Allow: /` first it reported the pagefind index as
 * crawlable. Ordering costs nothing and makes the rule hold under both readings.
 */
function group(agents: readonly string[], allow: boolean) {
  return [
    ...agents.map((agent) => `User-agent: ${agent}`),
    ...(allow ? excludedPaths.map((path) => `Disallow: ${path}`) : []),
    allow ? 'Allow: /' : 'Disallow: /',
  ].join('\n');
}

const body = [
  group(['*'], true),
  ...crawlerPolicy.map(
    ({ purpose, note, allow, agents }) =>
      `# ${purpose} — ${allow ? 'allowed' : 'not allowed'}. ${note}\n` + group(agents, allow),
  ),
  indexingEnabled ? `Sitemap: ${siteUrl}sitemap-index.xml` : '',
]
  .filter(Boolean)
  .join('\n\n');

export const GET: APIRoute = () =>
  new Response(`${body}\n`, { headers: { 'Content-Type': 'text/plain; charset=utf-8' } });
