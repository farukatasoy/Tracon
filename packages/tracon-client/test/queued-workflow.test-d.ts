import { expectTypeOf, test } from 'vitest';
import type { components, paths } from '../src/schema.js';

type AcceptedRunResponse = components['schemas']['AcceptedRunResponse'];

/** The `application/json` body a path's POST declares for `202 Accepted`, or `never` when it declares none. */
type AcceptedBody<Path extends keyof paths> = paths[Path] extends {
  post: { responses: { 202: { content: { 'application/json': infer Body } } } };
}
  ? Body
  : never;

/**
 * `queued-workflow.test.ts` reads the 202 body through a cast, which compiles whatever the
 * schema says. These assertions bind the shape to the generated schema itself: a spec that
 * stops declaring the `202` body, or declares another one, fails the type check.
 */
test('a queued workflow run, resume and response are declared to answer 202 with the accepted-run shape', () => {
  expectTypeOf<AcceptedBody<'/api/workflows/{name}/run'>>().toEqualTypeOf<AcceptedRunResponse>();
  expectTypeOf<AcceptedBody<'/api/workflows/runs/{runId}/resume'>>().toEqualTypeOf<AcceptedRunResponse>();
  expectTypeOf<AcceptedBody<'/api/workflows/runs/{runId}/respond'>>().toEqualTypeOf<AcceptedRunResponse>();
});
