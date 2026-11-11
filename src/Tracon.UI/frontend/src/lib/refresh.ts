import type { QueryClient, QueryKey } from '@tanstack/react-query';

/**
 * Reloads a query after a save, even when its first load has not answered yet.
 *
 * 🚨 `invalidateQueries` does not start a second request for a query that is loading and has no data: it hands
 * back the request already in flight, and that request was answered from the state before the save. A user who
 * saves before the first answer arrives (a slow machine, a page that loads a dozen lists at once) then sees the
 * old list, with nothing that would load it again. Cancelling first makes the reload a new request.
 */
export async function refreshAfterSave(queryClient: QueryClient, queryKey: QueryKey): Promise<void> {
  await queryClient.cancelQueries({ queryKey });
  await queryClient.invalidateQueries({ queryKey });
}
