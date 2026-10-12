import type { Dispatch, ReactNode, SetStateAction } from 'react';
import type { AgentParameter } from '@tracon/client';
import { Field, TextInput } from './ui';

type Values = Record<string, string>;

/**
 * One input per declared parameter: a checkbox for a `Boolean`, a number box for
 * a `Number`, text otherwise.
 *
 * Shared by the two screens that start a run with values - the playground (an
 * agent's schema) and a workflow's run panel (the workflow's) - so the two
 * cannot disagree on what a `Boolean` looks like. The kind only labels the
 * input; every value travels as a string.
 */
export function ParameterInputs({
  schema,
  values,
  onChange,
  title,
  testId,
  disabled = false,
}: {
  schema: readonly AgentParameter[];
  values: Values;
  onChange: Dispatch<SetStateAction<Values>>;
  title: string;
  testId: string;
  disabled?: boolean;
}): ReactNode {
  if (schema.length === 0) {
    return null;
  }

  return (
    <div data-testid={testId} className="flex flex-col gap-2">
      <p className="text-xs font-medium text-subtle uppercase">{title}</p>
      <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
        {schema.map((parameter) => (
          <Field key={parameter.name} label={parameter.name} required={parameter.required} hint={parameter.description}>
            {parameter.kind === 'Boolean' ? (
              <input
                type="checkbox"
                checked={(values[parameter.name] ?? parameter.defaultValue ?? 'false') === 'true'}
                disabled={disabled}
                onChange={(event) =>
                  onChange((current) => ({
                    ...current,
                    [parameter.name]: event.target.checked ? 'true' : 'false',
                  }))
                }
                className="size-4"
              />
            ) : (
              <TextInput
                type={parameter.kind === 'Number' ? 'number' : 'text'}
                value={values[parameter.name] ?? parameter.defaultValue ?? ''}
                placeholder={parameter.defaultValue ?? undefined}
                disabled={disabled}
                onChange={(event) => onChange((current) => ({ ...current, [parameter.name]: event.target.value }))}
              />
            )}
          </Field>
        ))}
      </div>
    </div>
  );
}

/**
 * Whether a required parameter has neither a value nor a default. A run with
 * one never starts on the server either (the parameter gate answers `400`);
 * the screen blocks it first so the person sees why before a round trip.
 */
export function hasMissingRequiredParameter(schema: readonly AgentParameter[], values: Values): boolean {
  return schema.some(
    (parameter) =>
      parameter.required &&
      (values[parameter.name] ?? '').trim().length === 0 &&
      (parameter.defaultValue ?? '').length === 0,
  );
}
