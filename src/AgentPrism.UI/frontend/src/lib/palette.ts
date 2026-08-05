/**
 * Command palette matching. Pure logic, covered by unit tests.
 *
 * Roughly forty lines of subsequence scoring instead of a fuzzy-search
 * dependency: the catalogue this runs over is the one already in the browser,
 * a few hundred entries at most, and the ranking rules that matter here are
 * simple enough to state — an earlier match beats a later one, a match at a
 * word boundary beats one inside a word, and a run of adjacent characters beats
 * a scattered one.
 */

export interface CommandLike {
  /** Text shown to the user, already translated. */
  label: string;
  /** Section heading the command is listed under, already translated. */
  group: string;
  /** Extra words to match on that are not shown, such as an identifier. */
  keywords?: string;
}

const WORD_BOUNDARY = /[\s\-_/.:]/;

/**
 * Scores a subsequence match, or returns `null` when the query does not fit.
 *
 * Higher is better. The number has no meaning beyond ordering.
 */
export function fuzzyScore(text: string, query: string): number | null {
  const haystack = text.toLowerCase();
  const needle = query.toLowerCase().replace(/\s+/g, '');

  if (needle.length === 0) {
    return 0;
  }

  let score = 0;
  let cursor = 0;
  let previous = -2;

  for (const character of needle) {
    const index = haystack.indexOf(character, cursor);

    if (index === -1) {
      return null;
    }

    if (index === previous + 1) {
      score += 8;
    }

    if (index === 0 || WORD_BOUNDARY.test(haystack[index - 1] as string)) {
      score += 6;
    }

    // A match near the front of the label is more likely to be what was meant.
    score += Math.max(0, 10 - index);

    previous = index;
    cursor = index + 1;
  }

  // A short label that matched is a better hit than a long one that also did.
  return score - Math.floor(haystack.length / 8);
}

/**
 * Filters and orders commands for a query.
 *
 * An empty query keeps the declared order, which is how the palette shows its
 * default list: navigation first, then actions.
 */
export function rankCommands<T extends CommandLike>(commands: readonly T[], query: string): T[] {
  const trimmed = query.trim();

  if (trimmed.length === 0) {
    return [...commands];
  }

  const scored: { command: T; score: number; index: number }[] = [];

  for (const [index, command] of commands.entries()) {
    const haystack = command.keywords === undefined ? command.label : `${command.label} ${command.keywords}`;
    const score = fuzzyScore(haystack, trimmed);

    if (score !== null) {
      scored.push({ command, score, index });
    }
  }

  // Ties keep the declared order so the list does not shuffle while typing.
  scored.sort((left, right) => right.score - left.score || left.index - right.index);

  return scored.map((entry) => entry.command);
}
