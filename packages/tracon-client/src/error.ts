/**
 * A failed request, carrying the server's own explanation.
 *
 * The management API answers with `application/problem+json` (decision
 * K-038), so `title` and `detail` are written for a human and are shown
 * verbatim (K-232) — this layer does not translate them.
 */
export class TraconError extends Error {
  readonly status: number;
  readonly title: string;
  readonly detail: string | null;

  constructor(status: number, title: string, detail: string | null) {
    super(detail ? `${title}: ${detail}` : title);
    this.name = 'TraconError';
    this.status = status;
    this.title = title;
    this.detail = detail;
  }
}
