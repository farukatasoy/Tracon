// The example host reads the caller's role from a header while its demo roles are on. The SDKs under test call
// fetch themselves, so the header is added here, to requests for the host and nobody else.

let installed = false;

export function sendDemoRole(baseUrl, role = 'admin') {
  if (installed) {
    return;
  }

  installed = true;

  const original = globalThis.fetch;
  const origin = new URL(baseUrl).origin;

  globalThis.fetch = (input, init) => {
    const url = new URL(typeof input === 'string' || input instanceof URL ? input : input.url);

    if (url.origin !== origin) {
      return original(input, init);
    }

    const request = new Request(input, init);

    request.headers.set('X-Tracon-Demo-Role', role);

    return original(request);
  };
}
