import { relative } from 'node:path';

export function toPosixPath(value) {
  return value.replaceAll('\\', '/');
}

export function portableRelative(from, to) {
  return toPosixPath(relative(from, to));
}
