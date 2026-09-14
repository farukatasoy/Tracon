// Where this site is published, declared once.
//
// `site` and `base` are read by seven other modules: the Astro config, the route
// middleware that writes per-page link previews, three generators that emit
// absolute links into generated pages, and two gates that walk the built `dist/`.
// Each of those used to carry its own copy of the literal and nothing verified
// that the copies agreed; one of them even spelled the base without its trailing
// slash. check-content.mjs now fails the build on a literal address anywhere
// outside this file.
//
// `base` is '/' on purpose and is meant to stay there. A sub-path base is a
// property of the host, not of the site: it was '/Tracon/' only because a
// GitHub Pages project site forces the repository name into the path. Pages link
// to each other with plain root-absolute paths, so moving hosts changes `site`
// alone. Moving back under a sub-path would also mean rewriting those links —
// check-links.mjs walks the built output and reports every one of them.
export const site = 'https://tracon.dev';
export const base = '/';

/** `site` + `base`, for the places that have to emit an absolute address. */
export const siteUrl = `${site}${base}`;

/**
 * Hosts that used to serve this site. Nothing may point at them again.
 *
 * The addresses in this repository are not all derived: package READMEs are plain
 * markdown that ships to NuGet, and an analyzer help link is a compiled constant.
 * A move leaves those behind, and a stale one is invisible — it looks like a link
 * and fails only in the reader's browser. check-content.mjs fails the build on any
 * of these hosts, so a forgotten copy is caught before the packages are built.
 *
 * Add the outgoing host here on every move; never remove an entry.
 */
export const formerHosts = ['farukatasoy.github.io', 'agentprism.doayen.web.tr'];

/**
 * The source repository, and whether a reader can open it.
 *
 * The repository is private, which is why the site moved hosts at all. The same fact
 * has a second consequence that is easy to miss: a link to it reaches the reader as a
 * 404. Starlight's edit links and its header icon are gone for that reason, and nine
 * package READMEs offered "Repository and full documentation" to consumers on NuGet
 * who cannot open either half.
 *
 * check-content.mjs bans the address in shipped and hand-written text while this flag
 * is false. Make the repository public and flip the flag: the ban lifts, and the
 * `editLink` / `social` blocks in astro.config.mjs are worth restoring at the same
 * time.
 */
export const repositoryUrl = 'https://github.com/farukatasoy/Tracon';
export const repositoryIsPublic = false;

/** Explicit preview builds remain crawlable so crawlers can read noindex. */
export const indexingEnabled = process.env.TRACON_SITE_INDEXING !== 'disabled';

/**
 * Which AI crawlers may read this site, declared once and grouped by what the
 * crawl is FOR. src/pages/robots.txt.ts renders it; nothing else decides it.
 *
 * Training and search are separate decisions, and every operator below documents
 * them as separate user agents — blocking one has never implied the other. The
 * list is written out rather than left to the `User-agent: *` group so the
 * decision is visible, and so reversing it for one crawler is a one-word edit
 * here rather than a rewrite of the endpoint.
 *
 * The decision for all of them is currently ALLOW, and the reason is the product:
 * Tracon is an unpublished .NET package family whose documentation exists to be
 * found, and whose own capability map (llms.txt) is written for a coding agent to
 * read. Being in the training data of the models that write .NET code is what the
 * map is for; withholding it would cost the thing the site is published for.
 *
 * Two measured facts belong with any future reversal, from the operators' own
 * documentation:
 *  - Google-Extended "does not impact a site's inclusion in Google Search nor is
 *    it used as a ranking signal", and Google states there is nothing extra to do
 *    to appear in AI Overviews or AI Mode. Blocking it costs Gemini training and
 *    grounding, not Search.
 *  - Applebot-Extended is the same shape: "Webpages that disallow
 *    Applebot-Extended can still be included in search results."
 * Blocking CCBot is the one with the widest blast radius: it removes the site
 * from Common Crawl, and from every downstream dataset built on it.
 *
 * The user-initiated fetchers (ChatGPT-User, Claude-User, Perplexity-User,
 * meta-externalfetcher) act for a person who asked for this page by name. They
 * are listed for completeness; Perplexity documents that its fetcher ignores
 * robots.txt for that reason, and OpenAI documents the same for ChatGPT-User.
 *
 * Sources, each the operator's own: developers.openai.com/api/docs/bots,
 * support.claude.com article 8896518, docs.perplexity.ai/guides/bots,
 * developers.google.com/search/docs/crawling-indexing/google-common-crawlers,
 * support.apple.com/en-us/119829, commoncrawl.org/ccbot, and
 * developers.facebook.com/documentation/sharing/webmasters/web-crawlers.
 */
export const crawlerPolicy = [
  {
    purpose: 'Model training and grounding',
    note: 'These crawls may be used to train or ground foundation models.',
    allow: true,
    agents: ['GPTBot', 'ClaudeBot', 'Google-Extended', 'Applebot-Extended', 'meta-externalagent', 'CCBot'],
  },
  {
    purpose: 'Search and citation',
    note: 'These crawls decide whether the site can be surfaced and linked in an answer.',
    allow: true,
    agents: ['OAI-SearchBot', 'Claude-SearchBot', 'PerplexityBot', 'meta-webindexer'],
  },
  {
    purpose: 'Fetches a person asked for',
    note: 'One page, because a user named it. Some operators document that these ignore robots.txt.',
    allow: true,
    agents: ['ChatGPT-User', 'Claude-User', 'Perplexity-User', 'meta-externalfetcher'],
  },
];
