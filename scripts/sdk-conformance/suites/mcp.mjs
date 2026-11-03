// Calls the MCP endpoint with the official MCP Inspector's command line (`--cli`): the tool list, a tool call, and the
// two ways a call can fail. The Inspector connects with the MCP TypeScript SDK, so the handshake, the protocol version
// and the tool schema are checked by that SDK and not by a request written here.

import { execFile } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { createRequire } from 'node:module';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { promisify } from 'node:util';

const run1 = promisify(execFile);
const require = createRequire(import.meta.url);

// `bin` of the package: the launcher dispatches to the CLI. It is run with this Node, not through `npx`, which is a
// `.cmd` on Windows.
const inspectorRoot = dirname(require.resolve('@modelcontextprotocol/inspector/package.json'));
const launcher = join(inspectorRoot, 'clients', 'launcher', 'build', 'index.js');

const TOOL = 'tracon_summarizer';

async function inspect(url, catalog, ...args) {
  try {
    const { stdout } = await run1(
      process.execPath,
      [launcher, '--cli', url, '--transport', 'http', '--header', 'X-Tracon-Demo-Role: admin', ...args],
      { env: { ...process.env, MCP_CATALOG_PATH: catalog }, timeout: 60_000 },
    );

    return { code: 0, json: JSON.parse(stdout), stdout };
  } catch (error) {
    // The Inspector prints a failure as one JSON line on stdout and exits non-zero.
    let json = null;

    try {
      json = JSON.parse(error.stdout);
    } catch {
      // Not JSON.
    }

    return { code: error.code, json, stdout: error.stdout ?? '', stderr: error.stderr ?? '' };
  }
}

export async function run({ baseUrl }) {
  const results = [];
  const check = (id, ok, detail) => results.push({ id, ok, detail });

  // The Inspector keeps its server list in a file; keep it out of the home directory.
  const directory = mkdtempSync(join(tmpdir(), 'tracon-mcp-'));
  const catalog = join(directory, 'mcp.json');
  const url = `${baseUrl}/tracon/mcp`;

  try {
    const list = await inspect(url, catalog, '--method', 'tools/list');
    const tool = list.json?.tools?.find((candidate) => candidate.name === TOOL);

    check('tools/list answers with the exposed agent as a tool', list.code === 0 && tool !== undefined, `exit ${list.code}: ${list.stdout.slice(0, 160)}`);
    check('the tool declares an object schema with a required string `message`', tool?.inputSchema?.type === 'object' && tool.inputSchema.required?.includes('message') && tool.inputSchema.properties?.message?.type === 'string', JSON.stringify(tool?.inputSchema));
    check('only the exposed agent is listed', (list.json?.tools ?? []).length === 1, (list.json?.tools ?? []).map((candidate) => candidate.name).join(','));

    const call = await inspect(url, catalog, '--method', 'tools/call', '--tool-name', TOOL, '--tool-arg', 'message=hello from the MCP Inspector');
    const text = call.json?.content?.map((part) => part.text ?? '').join('') ?? '';

    check('tools/call returns the agent answer as text content', call.code === 0 && text.includes('hello from the MCP Inspector') && call.json?.isError !== true, `exit ${call.code}: ${call.stdout.slice(0, 160)}`);

    const missing = await inspect(url, catalog, '--method', 'tools/call', '--tool-name', TOOL, '--tool-arg', 'other=value');

    check('a call without the required argument fails as a tool error, not as an answer', missing.code !== 0 && /isError|tool_is_error/u.test(missing.stdout), `exit ${missing.code}: ${missing.stdout.slice(0, 160)}`);

    const unknown = await inspect(url, catalog, '--method', 'tools/call', '--tool-name', 'no_such_tool');

    check('a tool the server does not have is refused', unknown.code !== 0, `exit ${unknown.code}: ${unknown.stdout.slice(0, 160)}`);

    const resources = await inspect(url, catalog, '--method', 'resources/list');

    check('resources/list answers with a list, whether or not it holds anything', resources.code === 0 && Array.isArray(resources.json?.resources), `exit ${resources.code}: ${resources.stdout.slice(0, 120)}`);
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }

  return { results };
}
