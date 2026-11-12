import { QueryObserver } from '@tanstack/react-query';
import { describe, expect, it, vi } from 'vitest';
import { createQueryClient } from './query-client';

/**
 * A screen that saves before its first list has answered must still show the saved row.
 *
 * `invalidateQueries` does not start a second request for a query that is loading and has no data: it returns the
 * request already in flight, and that request was answered from the state before the save. Fifty-four call sites
 * invalidate after a save; the client the console is built on cancels first, so every one of them reloads.
 */
describe('the console query client', () => {
  it('starts a new request when a query that is still on its first load is invalidated', async () => {
    const client = createQueryClient();
    const answers: Array<(value: string) => void> = [];

    const observer = new QueryObserver(client, {
      queryKey: ['rows'],
      queryFn: () => new Promise<string>((resolve) => answers.push(resolve)),
    });
    const unsubscribe = observer.subscribe(() => undefined);

    try {
      // The first request is in flight and held: the list has not answered yet.
      await vi.waitFor(() => expect(answers).toHaveLength(1));

      // The save finishes now and invalidates the list.
      const invalidated = client.invalidateQueries({ queryKey: ['rows'] });

      // It is a second request, not the first one handed back.
      await vi.waitFor(() => expect(answers).toHaveLength(2), { timeout: 1_000 });

      // The first request would have answered from before the save; only the second one counts.
      answers[0]?.('before the save');
      answers[1]?.('after the save');
      await invalidated;

      await vi.waitFor(() => expect(observer.getCurrentResult().data).toBe('after the save'));
    } finally {
      unsubscribe();
      client.clear();
    }
  });

  it('keeps the configuration it was given', () => {
    const client = createQueryClient({ defaultOptions: { queries: { staleTime: 1234 } } });

    expect(client.getDefaultOptions().queries?.staleTime).toBe(1234);
  });

  it('still reloads a query that already has data', async () => {
    const client = createQueryClient();
    let calls = 0;

    const observer = new QueryObserver(client, {
      queryKey: ['loaded'],
      queryFn: () => Promise.resolve(`call ${(calls += 1)}`),
    });
    const unsubscribe = observer.subscribe(() => undefined);

    try {
      await vi.waitFor(() => expect(observer.getCurrentResult().data).toBe('call 1'));

      await client.invalidateQueries({ queryKey: ['loaded'] });

      await vi.waitFor(() => expect(observer.getCurrentResult().data).toBe('call 2'));
    } finally {
      unsubscribe();
      client.clear();
    }
  });
});
