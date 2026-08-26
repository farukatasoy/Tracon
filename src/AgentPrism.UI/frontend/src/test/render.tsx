import type { ReactElement, ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, type RenderOptions } from '@testing-library/react';
import { LocaleProvider } from '../lib/i18n';
import { RouterProvider } from '../lib/router';

function createTestQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      // A retried failed query would leave a screen stuck on its loading
      // state well past this test's own timeout; every fixture failure here
      // is meant to be seen on the first try.
      queries: { retry: false, gcTime: 0 },
      mutations: { retry: false },
    },
  });
}

function Providers({ children }: { children: ReactNode }): ReactElement {
  return (
    <QueryClientProvider client={createTestQueryClient()}>
      <LocaleProvider>
        <RouterProvider>{children}</RouterProvider>
      </LocaleProvider>
    </QueryClientProvider>
  );
}

/**
 * Renders a screen (or section) component with the same providers `app.tsx`
 * wraps it in at runtime — query client, locale, router — minus `AccessGate`
 * and `Layout`, which a screen-level test has no reason to exercise.
 */
export function renderScreen(ui: ReactElement, options?: Omit<RenderOptions, 'wrapper'>) {
  return render(ui, { wrapper: Providers, ...options });
}

export * from '@testing-library/react';
