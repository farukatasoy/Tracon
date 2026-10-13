import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import { fixture, installApiMock, type FixtureRoute } from '../test/api-fixtures';
import { renderScreen, screen, waitFor, within } from '../test/render';
import { TenantProviderPanel } from './tenant-provider-panel';

const stamp = '2026-10-13T09:00:00Z';

const models = [
  { name: 'openai', models: ['gpt-5.4-mini'], status: 'Unknown' },
  { name: 'anthropic', models: ['claude-haiku'], status: 'Unknown' },
];

// One synthesizer is registered under a name ('studio') that differs from the
// provider name it reports ('studio-engine'): the policy names the latter.
const speech = [
  { name: null, kind: 'Synthesizer', providerName: 'elevenlabs', supportsTenantCredentials: false },
  { name: 'studio', kind: 'Synthesizer', providerName: 'studio-engine', supportsTenantCredentials: true },
  { name: null, kind: 'Transcriber', providerName: 'ElevenLabs', supportsTenantCredentials: false },
];

function routes(allowedProviders: string[] | null, extra: FixtureRoute[] = []): FixtureRoute[] {
  return [
    ...extra,
    fixture('GET', 'api/tenants/current', { tenantId: 'acme' }),
    fixture('GET', 'api/models', models),
    fixture('GET', 'api/voice/providers', speech),
    allowedProviders === null
      ? fixture('GET', 'api/tenants/:tenantId/egress', {}, 404)
      : fixture('GET', 'api/tenants/:tenantId/egress', { tenantId: 'acme', allowedProviders, updatedAt: stamp }),
  ];
}

describe('TenantProviderPanel', () => {
  let restoreFetch: () => void;

  afterEach(() => {
    restoreFetch();
  });

  // The egress policy and the bindings name a provider by the name its instance
  // reports. Built from the model catalog alone, the list never offered a speech
  // vendor, so a restricted tenant could not be given its text-to-speech engine.
  it('offers model and speech providers by provider name, once each', async () => {
    restoreFetch = installApiMock(routes(['openai']));

    renderScreen(<TenantProviderPanel canAdminister />);

    expect(await screen.findByTestId('tenant-egress-option-studio-engine')).toBeTruthy();
    expect(screen.getByTestId('tenant-egress-option-elevenlabs')).toBeTruthy();
    expect(screen.getByTestId('tenant-egress-option-openai')).toBeTruthy();

    // 'ElevenLabs' and 'elevenlabs' are one provider, and the registration name is not a provider name.
    expect(screen.queryByTestId('tenant-egress-option-ElevenLabs')).toBeNull();
    expect(screen.queryByTestId('tenant-egress-option-studio')).toBeNull();
  });

  it('saves a speech provider into the policy', async () => {
    let sent: unknown;
    const user = userEvent.setup();

    restoreFetch = installApiMock(
      routes(['openai'], [
        {
          method: 'PUT',
          pattern: 'api/tenants/:tenantId/egress',
          handler: (_params, _url, body) => {
            sent = body;

            return { tenantId: 'acme', allowedProviders: ['openai', 'studio-engine'], updatedAt: stamp };
          },
        },
      ]),
    );

    renderScreen(<TenantProviderPanel canAdminister />);

    await user.click(await screen.findByTestId('tenant-egress-option-studio-engine'));
    await user.click(screen.getByTestId('tenant-egress-save'));

    await waitFor(() => expect(sent).toEqual({ allowedProviders: ['openai', 'studio-engine'] }));
  });

  // A saved name no registered provider reports any more (a vendor removed from the
  // host, or one saved over HTTP) was invisible but still sent back on every save:
  // it could never be removed from the console.
  it('keeps a saved name that no provider reports, and lets it be removed', async () => {
    let sent: unknown;
    const user = userEvent.setup();

    restoreFetch = installApiMock(
      routes(['openai', 'retired-voice'], [
        {
          method: 'PUT',
          pattern: 'api/tenants/:tenantId/egress',
          handler: (_params, _url, body) => {
            sent = body;

            return { tenantId: 'acme', allowedProviders: ['openai'], updatedAt: stamp };
          },
        },
      ]),
    );

    renderScreen(<TenantProviderPanel canAdminister />);

    const retired = await screen.findByTestId('tenant-egress-option-retired-voice');
    expect(retired.getAttribute('aria-pressed')).toBe('true');

    await user.click(retired);
    await user.click(screen.getByTestId('tenant-egress-save'));

    await waitFor(() => expect(sent).toEqual({ allowedProviders: ['openai'] }));
  });

  it('offers a speech provider for a new binding', async () => {
    const user = userEvent.setup();

    restoreFetch = installApiMock(routes(['openai']));

    renderScreen(<TenantProviderPanel canAdminister />);

    await user.click(await screen.findByTestId('tenant-provider-add'));

    const select = await screen.findByTestId('tenant-provider-select');

    await waitFor(() =>
      expect(within(select).getAllByRole('option').map((option) => option.textContent)).toEqual([
        'anthropic',
        'elevenlabs',
        'openai',
        'studio-engine',
      ]),
    );
  });

  // An older server has no speech provider list; the model names must survive.
  it('still offers the model providers when the speech provider list fails', async () => {
    restoreFetch = installApiMock(routes(['openai'], [fixture('GET', 'api/voice/providers', {}, 404)]));

    renderScreen(<TenantProviderPanel canAdminister />);

    expect(await screen.findByTestId('tenant-egress-option-anthropic')).toBeTruthy();
    expect(screen.queryByTestId('tenant-egress-option-studio-engine')).toBeNull();
  });
});
