import { describe, expect, it } from 'vitest';
import { ClientToolRunner } from './tool-runner.ts';

describe('ClientToolRunner', () => {
  it('runs a registered handler and returns its result', async () => {
    const runner = new ClientToolRunner();
    runner.register('read_page_title', () => 'Shopping cart');

    expect(runner.has('read_page_title')).toBe(true);
    await expect(runner.run('read_page_title', {})).resolves.toBe('Shopping cart');
  });

  it('passes call arguments through to the handler', async () => {
    const runner = new ClientToolRunner();
    runner.register('greet', (args) => `hello ${String(args.name)}`);

    await expect(runner.run('greet', { name: 'Ada' })).resolves.toBe('hello Ada');
  });

  it('awaits an async handler', async () => {
    const runner = new ClientToolRunner();
    runner.register('slow', async () => {
      await Promise.resolve();

      return 'done';
    });

    await expect(runner.run('slow', {})).resolves.toBe('done');
  });

  it('throws for an unregistered tool name', async () => {
    const runner = new ClientToolRunner();

    expect(runner.has('missing')).toBe(false);
    await expect(runner.run('missing', {})).rejects.toThrow(/no handler/i);
  });
});
