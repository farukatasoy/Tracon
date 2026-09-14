// The plain-markdown copy of every documentation page, at the page's own address.
//
// The llms.txt convention asks for two things this route provides together: a
// markdown version of each page "at the same URL as the original page ... URLs
// without file names should append index.html.md or index.md", and llms.txt links
// that point at LLM-friendly content rather than at HTML. Both are quoted from
// llmstxt.org.
//
// It is not a convenience. Measured on the built landing page, an HTML-to-text
// pass over Expressive Code's output returns
//   var app = builder.Build();app.MapTracon("/tracon");app.Run();
// because every code LINE is a <div> with no newline character between them, and
// a table's column boundaries disappear the same way. A reader that flattens the
// HTML gets C# that does not compile and tables whose cells no longer line up
// with their headers. The markdown source has neither defect, and it is the
// source the HTML was built from, so it cannot drift from the page.
//
// Every content page gets one, generated pages included: the 782 type pages under
// /api/ are the surface a coding agent asks about most, and they are the ones
// whose code spans suffer most from that flattening.
import type { APIRoute, GetStaticPaths } from 'astro';
import { getCollection } from 'astro:content';

import { siteUrl } from '../../site.config.mjs';

/**
 * Pages with no markdown copy.
 *
 * The landing page is MDX: its body is imports and JSX components, which is not
 * the text a reader wants and is not markdown a model can use. llms-full.txt
 * leaves it out for the same reason. The error page has no content to serve and
 * is already noindex.
 *
 * Both spellings of the landing page are listed. The loader strips `index` from
 * `concepts/index.md` but keeps it for the site root, so excluding '' alone
 * published `/index/index.md` whose own `Page:` line named `/index/` - a route
 * that does not exist. site-seo-denetle.py now compares every built page against
 * its markdown copy and that copy's declared address, so a route that drifts fails
 * the gate rather than shipping a dead address to a reader with no site around it
 * to correct the mistake.
 */
const excluded = new Set(['', 'index', '404']);

export const getStaticPaths: GetStaticPaths = async () => {
  const docs = await getCollection('docs');

  return docs
    .filter((entry) => !excluded.has(entry.id) && typeof entry.body === 'string')
    .map((entry) => ({
      params: { slug: `${entry.id}/index` },
      props: {
        title: entry.data.title,
        description: entry.data.description ?? '',
        // The address of the HTML page this copy belongs to. That is what a reader
        // should cite; the .md address is a fetch detail, not a citation.
        page: `${siteUrl}${entry.id}/`,
        body: entry.body ?? '',
      },
    }));
};

export const GET: APIRoute = ({ props }) => {
  const { title, description, page, body } = props as {
    title: string;
    description: string;
    page: string;
    body: string;
  };

  // The header names the product on every page, because a retrieved fragment
  // arrives without the site around it: "An agent definition is data" identifies
  // no product, and Tracon on its own is an established air-traffic control term.
  // One sentence of provenance per file is what lets a reader attribute and cite
  // the fragment it retrieved.
  const lines = [
    `# ${title}`,
    '',
    '> Tracon documentation. Tracon is a .NET package family that adds a control',
    '> plane on Microsoft Agent Framework, published as NuGet packages that run',
    '> inside your own ASP.NET Core application.',
    '>',
    `> Page: ${page}`,
  ];

  if (description) {
    lines.push(`> Summary: ${description}`);
  }

  lines.push('', body.trim(), '');

  return new Response(lines.join('\n'), {
    headers: { 'Content-Type': 'text/markdown; charset=utf-8' },
  });
};
