import { tr } from './locale.tr.ts';

/**
 * The widget's own, small dictionary — deliberately separate from
 * `src/locales/`. Pulling in the console's ~800-key dictionary would blow
 * the widget's 30 KB budget for a handful of strings (Phase 61, Open
 * Question 5).
 */
export interface EmbedMessages {
  readonly bubbleLabel: string;
  readonly title: string;
  readonly placeholder: string;
  readonly send: string;
  readonly close: string;
  readonly runningTool: string;
  readonly toolNotAvailable: string;
  readonly requestFailed: string;
}

const en: EmbedMessages = {
  bubbleLabel: 'Open chat',
  title: 'Chat',
  placeholder: 'Message…',
  send: 'Send',
  close: 'Close',
  runningTool: 'Running…',
  toolNotAvailable: 'This page cannot fulfil that request.',
  requestFailed: 'Something went wrong. Please try again.',
};

export function resolveMessages(locale: string | undefined): EmbedMessages {
  const language = (locale ?? navigator.language).toLowerCase();

  return language.startsWith('tr') ? tr : en;
}
