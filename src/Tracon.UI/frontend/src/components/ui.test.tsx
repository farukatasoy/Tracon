import { describe, expect, it } from 'vitest';
import { renderScreen, screen } from '../test/render';
import { Button, Field, Select, TextInput } from './ui';
import { Tooltip } from './tooltip';

/**
 * The bindings that connect one primitive to another.
 *
 * 🚨 These exist because a primitive with a FIXED attribute list silently
 * swallows any prop it did not anticipate. `Tooltip` and `Field` generate ARIA
 * bindings and hand them to whatever control the caller passed; when `Button`
 * dropped `aria-describedby`, the tooltip still rendered, the test suite stayed
 * green, and the control lost the description it used to carry. Nothing in the
 * type system or the browser reports that. These tests do.
 */
describe('accessibility bindings between primitives', () => {
  it('lands the tooltip description on the control it wraps', () => {
    renderScreen(
      <Tooltip text="Approve this call and queue a new run.">
        <Button testId="approve">Approve</Button>
      </Tooltip>,
    );

    const button = screen.getByTestId('approve');
    const described = button.getAttribute('aria-describedby');

    expect(described).not.toBeNull();
    expect(document.getElementById(described ?? '')?.textContent).toBe(
      'Approve this call and queue a new run.',
    );
  });

  it('lands a field error on the control, and marks it invalid', () => {
    renderScreen(
      <Field label="Schema" hint="JSON Schema for the response." error="Not valid JSON.">
        {(ids) => <TextInput {...ids} data-testid="schema" defaultValue="{ not json" />}
      </Field>,
    );

    const input = screen.getByTestId('schema');

    expect(input.getAttribute('aria-invalid')).toBe('true');

    const described = (input.getAttribute('aria-describedby') ?? '').split(' ');

    expect(described.length).toBe(2);
    expect(described.map((id) => document.getElementById(id)?.textContent)).toEqual([
      'JSON Schema for the response.',
      'Not valid JSON.',
    ]);
  });

  it('lands a field error on a Select, which renders its own fixed attribute list', () => {
    // `Select` is the other primitive with a closed prop list. It takes the
    // same bindings today only because this test would fail otherwise.
    renderScreen(
      <Field label="Provider" error="Pick a provider.">
        {(ids) => (
          <Select {...ids} testId="provider" value="" onChange={() => undefined}>
            <option value="">—</option>
          </Select>
        )}
      </Field>,
    );

    const select = screen.getByTestId('provider');

    expect(select.getAttribute('aria-invalid')).toBe('true');
    expect(
      document.getElementById(select.getAttribute('aria-describedby') ?? '')?.textContent,
    ).toBe('Pick a provider.');
  });

  it('keeps the required marker in the accessible name', () => {
    // 🚨 NOT decoration. `Field`'s plain-children form cannot put
    // `aria-required` on the control, so hiding the asterisk would announce a
    // required field as optional.
    renderScreen(
      <Field label="Name" required>
        <TextInput defaultValue="" />
      </Field>,
    );

    // A regex, not a literal: the asterisk's spacing is the label's own markup
    // and the browser and jsdom normalise it differently. What matters is that
    // it is IN the name — `UiTests.Trigger_created_from_UI_is_listed` pins the
    // exact browser string.
    expect(screen.getByRole('textbox', { name: /^Name\s*\*$/ })).toBeDefined();
  });
});
