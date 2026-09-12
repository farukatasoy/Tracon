import { describe, expect, it } from 'vitest';
import { renderScreen, screen } from '../test/render';
import { Badge, Button, Field, LinkButton, Select, TextInput } from './ui';
import { Link } from '../lib/router';
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

  it('lands a disclosure state on the button that owns it', () => {
    // 🚨 The fourth closed-prop-list trap. A row that expands says so with
    // `aria-expanded` and names what it expands with `aria-controls`; a
    // primitive that swallows either turns an announced disclosure into a
    // button whose effect is invisible to anyone not watching the screen.
    renderScreen(
      <Button testId="details" aria-expanded={false} aria-controls="panel-1">
        Details
      </Button>,
    );

    const button = screen.getByTestId('details');

    expect(button.getAttribute('aria-expanded')).toBe('false');
    expect(button.getAttribute('aria-controls')).toBe('panel-1');
  });

  it('lands the tooltip description on the link it wraps', () => {
    // `Link` is the third primitive with a closed prop list, and the one a
    // tooltip is most likely to be put on next: a bare row identifier.
    renderScreen(
      <Tooltip text="Open this run.">
        <Link to="runs/abc" testId="run-link">
          abc
        </Link>
      </Tooltip>,
    );

    const link = screen.getByTestId('run-link');

    expect(
      document.getElementById(link.getAttribute('aria-describedby') ?? '')?.textContent,
    ).toBe('Open this run.');
  });

  it('makes a described badge reachable, and binds the description to it', () => {
    // 🚨 A badge carrying only a `title` was unreachable by keyboard and
    // invisible on touch. `description` is what replaced it, and the binding is
    // only worth anything if the badge can actually be focused.
    renderScreen(<Badge description="Defined in code; the stored copy is ignored.">code</Badge>);

    const chip = screen.getByText('code');

    expect(chip.getAttribute('tabindex')).toBe('0');
    expect(document.getElementById(chip.getAttribute('aria-describedby') ?? '')?.textContent).toBe(
      'Defined in code; the stored copy is ignored.',
    );
  });

  it('leaves an undescribed badge out of the tab order', () => {
    renderScreen(<Badge>queued</Badge>);

    expect(screen.getByText('queued').getAttribute('tabindex')).toBeNull();
  });
});

/**
 * How a navigation looks and what it is.
 *
 * 🚨 `Link` rendered a class-less `<a>` until phase 165, so 25 of its 48 call
 * sites read as plain text. The default matters as much as the override does:
 * a caller that dresses a link deliberately — muted in a table cell, or shaped
 * like a button — must still win.
 */
describe('links', () => {
  it('reads as a link without being dressed', () => {
    renderScreen(
      <Link to="runs" testId="bare">
        Runs
      </Link>,
    );

    expect(screen.getByTestId('bare').className).toContain('text-accent');
  });

  it('lets a caller replace the default outright', () => {
    renderScreen(
      <Link to="runs" testId="muted" className="text-muted hover:text-fg">
        Runs
      </Link>,
    );

    const link = screen.getByTestId('muted');

    expect(link.className).toBe('text-muted hover:text-fg');
    expect(link.className).not.toContain('text-accent');
  });

  it('renders a button-shaped navigation as one anchor, not a button inside one', () => {
    // 🚨 `<Link><Button/></Link>` was the old spelling: a <button> inside an
    // <a> is invalid HTML and gives the keyboard two stops for one destination.
    renderScreen(
      <LinkButton to="agents/new" tone="primary" testId="new-agent">
        New agent
      </LinkButton>,
    );

    const link = screen.getByTestId('new-agent');

    expect(link.tagName).toBe('A');
    expect(link.querySelector('button')).toBeNull();
    expect(link.getAttribute('href')).toContain('agents/new');
  });
});
