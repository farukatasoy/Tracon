import {
  QueryClient,
  type InvalidateOptions,
  type InvalidateQueryFilters,
  type QueryClientConfig,
} from '@tanstack/react-query';

/**
 * The query client of the console: an invalidation reloads, even a query whose first load has not answered yet.
 *
 * 🚨 `invalidateQueries` does not start a second request for a query that is loading and has no data: it hands
 * back the request already in flight, and that request was answered from the state before the save. A user who
 * saves before the first answer arrives (a slow machine, a page that loads a dozen lists at once) then sees the
 * old list, with nothing that would load it again. Fifty-four places invalidate after a save and every one has
 * the problem, so the rule lives here and not at each of them: cancelling first makes the reload a new request.
 *
 * Nothing in the console prefetches or reads a query through `fetchQuery`/`ensureQueryData`, so there is no caller
 * whose request the cancellation could take away; a query that already has data keeps showing it while it reloads.
 */
export class ConsoleQueryClient extends QueryClient {
  public override async invalidateQueries(
    filters?: InvalidateQueryFilters,
    options?: InvalidateOptions,
  ): Promise<void> {
    await this.cancelQueries(filters);
    await super.invalidateQueries(filters, options);
  }
}

/** Builds the query client the console and its screen tests share. */
export function createQueryClient(config?: QueryClientConfig): QueryClient {
  return new ConsoleQueryClient(config);
}
