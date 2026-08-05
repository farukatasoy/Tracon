import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { App } from './app';
import { ApiError } from './lib/api';
import { LocaleProvider, initialiseLocale } from './lib/i18n';
import { RouterProvider } from './lib/router';
import { applyTheme, readThemePreference } from './lib/theme';
import './styles.css';

// Applied before the first render so the correct palette is on screen from the
// first paint. Doing this inside a component would flash the other theme.
applyTheme(readThemePreference());

// Same reason, and it also puts `lang` on <html> before a screen reader starts
// announcing: the first frame is already in the right language.
initialiseLocale();

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // The console shows live state; refetching on focus is what an operator
      // expects when returning to the tab.
      refetchOnWindowFocus: true,
      staleTime: 5_000,
      retry: (failureCount, error) => {
        // A rejected token or a denied policy will be rejected again. Retrying
        // only delays the explanation the user needs to see.
        if (error instanceof ApiError && error.status >= 400 && error.status < 500) {
          return false;
        }

        return failureCount < 2;
      },
    },
  },
});

const container = document.getElementById('root');

if (container === null) {
  throw new Error('AgentPrism UI: #root element is missing from the shell.');
}

createRoot(container).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <LocaleProvider>
        <RouterProvider>
          <App />
        </RouterProvider>
      </LocaleProvider>
    </QueryClientProvider>
  </StrictMode>,
);
