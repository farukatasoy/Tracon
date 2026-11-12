const WHOLE_NUMBER = /^\d+$/;

/**
 * True when a limit field can be sent as it is: empty (no limit), or a whole number from 0 that a JavaScript
 * number holds exactly.
 *
 * The server takes a 64-bit limit, and a JavaScript number is exact only up to `Number.MAX_SAFE_INTEGER`. A
 * larger number is not an error to JavaScript: it is rounded, so `9223372036854775807` would go out as
 * `9223372036854775808`. Anything else that is not a whole number (`abc`, `-5`, `1.5`) used to become an empty
 * limit, which the server reads as no limit at all. The text is checked before it becomes a number, and a value
 * the form cannot send is refused, not rounded and not turned into "unlimited".
 */
export function isValidLimit(text: string): boolean {
  const trimmed = text.trim();

  return trimmed.length === 0 || (WHOLE_NUMBER.test(trimmed) && Number.isSafeInteger(Number(trimmed)));
}

/** The number a limit field holds; `null` for an empty field (no limit) or a value `isValidLimit` refuses. */
export function toLimit(text: string): number | null {
  const trimmed = text.trim();

  return trimmed.length > 0 && isValidLimit(trimmed) ? Number(trimmed) : null;
}
