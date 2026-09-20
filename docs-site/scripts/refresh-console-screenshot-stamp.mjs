import { writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import { computeUiSourceHash } from './check-console-screens.mjs';

const here = resolve(fileURLToPath(new URL('.', import.meta.url)));
const siteRoot = resolve(here, '..');
const repositoryRoot = resolve(siteRoot, '..');
const sourceRoot = join(repositoryRoot, 'src');
const output = join(siteRoot, 'public/screenshots/.ui-source.sha256');

writeFileSync(output, `${computeUiSourceHash(sourceRoot)}\n`);
console.log(`Updated ${output}`);
