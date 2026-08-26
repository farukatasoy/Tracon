import { useCallback, useRef, useState, type RefObject } from 'react';
import { client, unwrap } from '../../lib/api';
import type { AttachmentDescriptor } from '../../lib/server-types';

export interface UseAttachmentsResult {
  pending: AttachmentDescriptor[];
  uploading: boolean;
  error: unknown;
  fileInputRef: RefObject<HTMLInputElement | null>;
  upload: (files: FileList | File[]) => Promise<void>;
  remove: (id: string) => void;
  /** Clears pending state, e.g. when starting a new chat. */
  reset: () => void;
  /** Reads the current pending attachments and clears them in one step — what a send() needs. */
  take: () => AttachmentDescriptor[];
}

/**
 * Owns the "attach a file before sending" lifecycle: upload, remove, and the
 * hidden `<input type="file">` it is driven from.
 *
 * Uploaded ahead of the message, not bundled with it: the endpoint is a plain
 * multipart POST, independent from the SSE run request. A file is usable in
 * the *next* `run` call as soon as its descriptor comes back.
 */
export function useAttachments(sessionId: string | null): UseAttachmentsResult {
  const [pending, setPending] = useState<AttachmentDescriptor[]>([]);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const upload = useCallback(
    async (files: FileList | File[]) => {
      setError(null);
      setUploading(true);

      try {
        for (const file of Array.from(files)) {
          const form = new FormData();

          form.append('file', file, file.name);

          const descriptor = (await unwrap(
            client.POST('/api/attachments', {
              params: { query: { sessionId: sessionId ?? undefined } },
              body: form as unknown as { file: string },
            }),
          )) as AttachmentDescriptor;

          setPending((current) => [...current, descriptor]);
        }
      } catch (caught) {
        setError(caught);
      } finally {
        setUploading(false);
      }
    },
    [sessionId],
  );

  const remove = useCallback((id: string) => {
    setPending((current) => current.filter((attachment) => attachment.id !== id));
    void client
      .DELETE('/api/attachments/{id}', { params: { path: { id } } })
      .catch(() => {
        // Best effort: the reference is already gone from the next message
        // either way, and the row is orderless clutter at worst.
      });
  }, []);

  const reset = useCallback(() => {
    setPending([]);
    setError(null);
  }, []);

  // Not `useCallback`: it must read the LATEST `pending` at call time (send()
  // calls it synchronously right before clearing the prompt), and a `pending`
  // dependency would make its identity change on every upload anyway.
  const take = (): AttachmentDescriptor[] => {
    const taken = pending;

    setPending([]);

    return taken;
  };

  return { pending, uploading, error, fileInputRef, upload, remove, reset, take };
}
