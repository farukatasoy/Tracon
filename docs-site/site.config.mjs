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
