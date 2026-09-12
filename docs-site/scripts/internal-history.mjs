// The one definition of "a reference into the development record".
//
// Four places need it — three site generators and the .NET ratchet in
// tests/Tracon.Core.UnitTests/Architecture — and four copies drift. Measured:
// while there were copies, a `section 39` in a doc comment passed the .NET gate and
// then failed the site generator. The pattern lives in a plain file so a regex
// engine on either side can read it unchanged.
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

export const patternFile = join(dirname(fileURLToPath(import.meta.url)), 'internal-history.pattern');

export const INTERNAL_HISTORY = new RegExp(readFileSync(patternFile, 'utf8').trim(), 'i');

/** True when the text points at something only the development record explains. */
export function hasInternalHistory(text) {
  return INTERNAL_HISTORY.test(String(text));
}

/** The first such reference, with its position, for an error message worth reading. */
export function internalHistoryMarker(text) {
  const match = INTERNAL_HISTORY.exec(String(text));
  return match ? { text: match[0], index: match.index } : null;
}
