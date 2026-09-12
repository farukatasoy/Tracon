import { useEffect, useState, type ReactNode } from 'react';
import { client, unwrap } from '../../lib/api';
import { useT } from '../../lib/i18n';
import type { AttachmentDescriptor } from '../../lib/server-types';
import { CrossIcon, PaperclipIcon } from '../../components/icons';

/**
 * Fetches an image attachment's bytes once and hands back an object URL.
 *
 * A plain `<img src="api/attachments/{id}">` cannot carry the bearer token
 * (browsers do not attach custom headers to resource loads), so the preview
 * has to go through `client.GET(..., { parseAs: 'blob' })` and wrap the result.
 */
function useAttachmentPreview(id: string, enabled: boolean): string | null {
  const [url, setUrl] = useState<string | null>(null);

  useEffect(() => {
    if (!enabled) {
      return;
    }

    let objectUrl: string | null = null;
    let cancelled = false;

    void (unwrap(client.GET('/api/attachments/{id}', { params: { path: { id } }, parseAs: 'blob' })) as Promise<Blob>)
      .then((blob) => {
        if (!cancelled) {
          objectUrl = URL.createObjectURL(blob);
          setUrl(objectUrl);
        }
      })
      .catch(() => {
        // Preview is best-effort; the chip below falls back to a plain icon.
      });

    return () => {
      cancelled = true;

      if (objectUrl !== null) {
        URL.revokeObjectURL(objectUrl);
      }
    };
  }, [id, enabled]);

  return url;
}

/** A small pill showing one attached file, with an image thumbnail when possible. */
export function AttachmentChip({
  attachment,
  onRemove,
}: {
  attachment: AttachmentDescriptor;
  onRemove?: () => void;
}): ReactNode {
  const t = useT();
  const isImage = attachment.mediaType.startsWith('image/');
  const previewUrl = useAttachmentPreview(attachment.id, isImage);

  return (
    <span
      data-testid="attachment-chip"
      className="inline-flex items-center gap-1.5 rounded-md border border-line bg-panel py-1 pr-2 pl-1 text-[11px]"
    >
      {previewUrl !== null ? (
        <img src={previewUrl} alt="" className="size-5 rounded object-cover" />
      ) : (
        <PaperclipIcon className="size-3.5 text-subtle" />
      )}
      <span className="max-w-[10rem] truncate" title={attachment.fileName}>
        {attachment.fileName}
      </span>
      {onRemove !== undefined && (
        <button
          type="button"
          onClick={onRemove}
          className="text-subtle hover:text-fg"
          aria-label={t('playground.removeAttachment', { name: attachment.fileName })}
        >
          <CrossIcon className="size-3" />
        </button>
      )}
    </span>
  );
}
