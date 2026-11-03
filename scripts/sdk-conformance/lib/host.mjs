// Builds and runs the reference host (samples/Tracon.Api) as a child process on a free loopback port.
//
// The environment mirrors the other out-of-process conformance run (scripts/openapi_fuzz.py): Staging loads no user
// secrets, so a developer's provider keys and databases never reach a run, and the demo roles let a request pick its
// role with a header.

import { execFileSync, spawn } from 'node:child_process';
import { once } from 'node:events';
import { existsSync, openSync } from 'node:fs';
import { createServer } from 'node:net';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));

export const repositoryRoot = resolve(here, '../../..');
export const hostProject = join(repositoryRoot, 'samples/Tracon.Api/Tracon.Api.csproj');
export const hostOutput = join(repositoryRoot, 'artifacts/bin/Tracon.Api/release');

const SCRUBBED_PREFIXES = ['TRACON', 'ASPNETCORE_', 'DOTNET_ENVIRONMENT', 'OTEL_'];
const READY_SECONDS = 180;

/** The current environment without anything that would change what the host does. */
function hostEnvironment() {
  const kept = Object.entries(process.env).filter(([key]) => !SCRUBBED_PREFIXES.some((prefix) => key.toUpperCase().startsWith(prefix)));

  return {
    ...Object.fromEntries(kept),
    ASPNETCORE_ENVIRONMENT: 'Staging',
    Tracon__Demo__Roles__Enabled: 'true',
    Tracon__Ui__AuthToken: '',
    Tracon__PostgreSql__ConnectionString: '',
    Tracon__SqlServer__ConnectionString: '',
    Tracon__Sqlite__ConnectionString: '',
    Logging__LogLevel__Default: 'Warning',
  };
}

/** Builds the host without its console: the console is not part of the HTTP surface and building it needs Node packages. */
export function buildHost() {
  console.log('Building the reference host ...');
  execFileSync(
    'dotnet',
    ['build', hostProject, '-c', 'Release', '-m:3', '--nologo', '-v:q', '-p:TraconFrontendEnabled=false'],
    { env: { ...process.env, MSBUILDDISABLENODEREUSE: '1' }, stdio: ['ignore', 'inherit', 'inherit'] },
  );
}

async function freePort() {
  const probe = createServer();

  probe.listen(0, '127.0.0.1');
  await once(probe, 'listening');

  const { port } = probe.address();

  probe.close();
  await once(probe, 'close');

  return port;
}

/** Starts the host and waits until it serves its OpenAPI document. Returns its base URL and a function that stops it. */
export async function startHost(logPath) {
  const dll = join(hostOutput, 'Tracon.Api.dll');

  if (!existsSync(dll)) {
    throw new Error(`${dll} does not exist; run without --no-build first.`);
  }

  const port = await freePort();
  const baseUrl = `http://127.0.0.1:${port}`;
  const log = openSync(logPath, 'w');
  const child = spawn('dotnet', [dll, '--contentRoot', hostOutput, '--urls', baseUrl], {
    cwd: hostOutput,
    env: hostEnvironment(),
    stdio: ['ignore', log, log],
  });

  let exited = null;

  child.once('exit', (code) => {
    exited = code ?? -1;
  });

  const deadline = Date.now() + READY_SECONDS * 1000;

  while (Date.now() < deadline) {
    if (exited !== null) {
      throw new Error(`The host exited with ${exited} before it was ready; see ${logPath}.`);
    }

    try {
      const response = await fetch(`${baseUrl}/openapi/v1.json`, { signal: AbortSignal.timeout(5000) });

      if (response.status === 200) {
        return {
          baseUrl,
          async stop() {
            if (exited !== null) {
              return;
            }

            child.kill('SIGTERM');
            await Promise.race([once(child, 'exit'), new Promise((done) => setTimeout(done, 15000))]);

            if (exited === null) {
              child.kill('SIGKILL');
            }
          },
        };
      }
    } catch {
      // Not listening yet.
    }

    await new Promise((done) => setTimeout(done, 1000));
  }

  child.kill('SIGKILL');
  throw new Error(`The host was not ready after ${READY_SECONDS} s; see ${logPath}.`);
}
