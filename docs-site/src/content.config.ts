import { defineCollection } from 'astro:content';
import { docsLoader, i18nLoader } from '@astrojs/starlight/loaders';
import { docsSchema, i18nSchema } from '@astrojs/starlight/schema';

export const collections = {
  docs: defineCollection({ loader: docsLoader(), schema: docsSchema() }),
  // Starlight 0.42 warns when the `i18n` collection is missing. The site is English
  // only and overrides no UI strings, so the collection holds one empty file.
  i18n: defineCollection({ loader: i18nLoader(), schema: i18nSchema() }),
};
