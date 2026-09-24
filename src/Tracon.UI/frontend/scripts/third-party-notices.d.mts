import type { Plugin } from 'vite';

export declare const NOTICE_NAME: string;

export declare function thirdPartyModules(
  build: 'console' | 'embed',
  options?: { css?: readonly string[] },
): Plugin;

export declare function renderThirdPartyNotices(): string;

export declare function writeThirdPartyNotices(options?: { write?: boolean }): string;
