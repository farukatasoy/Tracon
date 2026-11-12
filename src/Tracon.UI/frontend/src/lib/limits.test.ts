import { describe, expect, it } from 'vitest';
import { isValidLimit, toLimit } from './limits';

describe('limit fields', () => {
  it('accept an empty field and whole numbers a JavaScript number holds exactly', () => {
    for (const text of ['', '  ', '0', '100', ' 42 ', String(Number.MAX_SAFE_INTEGER)]) {
      expect(isValidLimit(text), text).toBe(true);
    }
  });

  it('refuse what would be rounded or turned into no limit', () => {
    for (const text of ['abc', '-5', '1.5', '1e3', '9007199254740993', '9223372036854775807', '12 34']) {
      expect(isValidLimit(text), text).toBe(false);
      expect(toLimit(text), text).toBeNull();
    }
  });

  it('read an empty field as no limit and a valid one as its number', () => {
    expect(toLimit('')).toBeNull();
    expect(toLimit(' 250 ')).toBe(250);
    expect(toLimit('0')).toBe(0);
  });
});
