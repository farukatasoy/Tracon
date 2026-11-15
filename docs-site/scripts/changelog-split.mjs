// Splits the body of one release into pages that each stay under the site's weight ceiling.
//
// Why a release is split at all:
//
//   A release page weighs what its Markdown weighs. One entry of the changelog is a paragraph with a "What to do"
//   list, and a release that fixes a hundred things is a hundred of them: the 1.0.0-preview.12 entry is 98 KB of
//   Markdown and rendered 62 615 B gzip against a 59 500 B ceiling, the third release (after 1.0.0-preview.4 and
//   1.0.0-preview.8) that crossed it. Each crossing used to be settled with a ceiling for that one page and a promise
//   that the next release would be split; `check-weight.mjs` says where the split belongs, and this is it.
//
// Where it cuts:
//
//   At the boundary of an entry, never inside one: an entry is a top-level `- ` item with everything under it, and a
//   `## ` heading is the section the entries after it belong to. A part that begins inside a section repeats the
//   section's name with "(continued)", so a reader who lands on part 3 knows what the entries are. The parts are of
//   about the same size (the body is divided by the number of parts it needs, not filled page by page), so the last
//   page is not a few lines. A line that starts with `- ` or `## ` inside a fenced code block is code, not an entry.

/** The most Markdown bytes one page carries. 64 000 B of this changelog's Markdown weighs about 29 KB gzipped, which with the 24 KB every page shares leaves the page well under the ceiling. */
export const PART_LIMIT = 64_000;

/**
 * @param {string} body The body of one release, headings already at the level the page uses.
 * @param {number} [limit] The most Markdown bytes one page carries.
 * @returns {string[]} The pages, in order. One page when the body fits.
 */
export function splitBody(body, limit = PART_LIMIT) {
  const chunks = toChunks(body);
  const total = chunks.reduce((sum, chunk) => sum + chunk.size, 0);

  if (total <= limit) {
    return [body];
  }

  const count = Math.ceil(total / limit);
  const target = total / count;
  const parts = [[]];
  let used = 0;

  for (const chunk of chunks) {
    const part = parts[parts.length - 1];
    const wouldOverflow = used + chunk.size > target;
    const holdsAnEntry = part.some((held) => held.kind === 'entry');

    if (chunk.kind === 'entry' && wouldOverflow && holdsAnEntry && parts.length < count) {
      // A heading that is the last thing of a part belongs to the entries after it, so it moves with them.
      const carried = part.at(-1)?.kind === 'heading' ? [part.pop()] : [];

      parts.push([...carried]);
      used = carried.reduce((sum, held) => sum + held.size, 0);
    }

    parts[parts.length - 1].push(chunk);
    used += chunk.size;
  }

  return parts.map((part, index) => {
    const first = part.find((chunk) => chunk.kind !== 'lead');
    const continued =
      index > 0 && first?.kind === 'entry' && first.section
        ? `## ${first.section} (continued)\n\n`
        : '';

    return `${continued}${part.map((chunk) => chunk.text).join('\n')}`.trim();
  });
}

/** How many top-level entries a body holds. */
export function countEntries(body) {
  return toChunks(body).filter((chunk) => chunk.kind === 'entry').length;
}

/**
 * Reads the body as a lead, `## ` headings and top-level `- ` entries. The text of a chunk runs up to the next one,
 * so joining the chunks with a newline returns the body.
 */
function toChunks(body) {
  const chunks = [];
  let fence = null;
  let section = null;
  let current = { kind: 'lead', lines: [], section };

  const close = () => {
    if (current.lines.length > 0) {
      const text = current.lines.join('\n');

      chunks.push({ kind: current.kind, text, section: current.section, size: Buffer.byteLength(`${text}\n`) });
    }
  };

  for (const line of body.split('\n')) {
    const marker = /^\s*(```+|~~~+)/.exec(line);

    if (marker) {
      fence = fence === null ? marker[1][0] : line.trimStart().startsWith(fence) ? null : fence;
    }

    const heading = fence === null && /^## /.test(line);
    const entry = fence === null && /^- /.test(line);

    if (heading || entry) {
      close();
      section = heading ? line.slice(3).trim() : section;
      current = { kind: heading ? 'heading' : 'entry', lines: [], section };
    }

    current.lines.push(line);
  }

  close();

  return chunks;
}
