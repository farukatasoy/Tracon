import { useEffect, useRef, type ReactNode } from 'react';
import { useT } from '../lib/i18n';
import { Dialog } from './dialog';
import { Button, ErrorNote } from './ui';

/**
 * The second step in front of an action that cannot be taken back.
 *
 * 🚨 Read `docs/arsiv/fazlar/175-GERI-ALINAMAZ-KARAR-DOGRULAMASI.md` §175.3 before reaching
 * for this. It does NOT belong on every destructive-looking button: an action
 * earns a confirmation only when it cannot be recreated from the same form, or
 * when the decision itself cannot be given again (K-368). Everything else gets
 * the consequence sentence on a `Tooltip` and stays one click. Confirming
 * everything is the same as confirming nothing.
 *
 * It exists as its own component rather than a `Dialog` body per screen for
 * the reason K-483 records: an expression repeated by hand at eleven call
 * sites drifts at one of them, and here the thing that would drift is which
 * button holds focus.
 *
 * Three properties it is responsible for, each proven by an E2E case because
 * none of them can be proven in jsdom:
 *
 *   1. focus opens on CANCEL, never on the destructive answer — `Enter` right
 *      after the dialog appears must change nothing;
 *   2. Esc, Cancel and the backdrop all close it WITHOUT calling `onConfirm`,
 *      so no request is sent;
 *   3. a second click on the confirm button while the first is still in flight
 *      does not fire the action twice.
 */
export function ConfirmDialog({
  open,
  onClose,
  onConfirm,
  title,
  consequence,
  confirmLabel,
  busy = false,
  error,
  tone = 'danger',
  testId,
}: {
  open: boolean;
  /** Called by Esc, Cancel, the close control and the backdrop. Never confirms. */
  onClose: () => void;
  onConfirm: () => void;
  title: string;
  /**
   * What the action does, in one sentence.
   *
   * 🚨 It comes from the SAME message key as the trigger's own tooltip. Writing
   * the sentence twice lets the two drift, and the one a hurried operator reads
   * is whichever they happen to look at.
   */
  consequence: string;
  confirmLabel: string;
  /** The action is in flight: the dialog stays open and both answers lock. */
  busy?: boolean;
  /**
   * A refusal from the server, shown inside the dialog.
   *
   * The dialog is modal, so the screen's own error note is behind a backdrop
   * while this is open — the operator is never shown the same sentence twice.
   */
  error?: unknown;
  tone?: 'danger' | 'default';
  testId?: string;
}): ReactNode {
  const t = useT();
  const cancel = useRef<HTMLButtonElement | null>(null);

  /*
    🚨 A guard against the double click, not a nicety. `busy` only turns true
    once the caller's mutation has re-rendered the screen, and two clicks can
    land inside one frame — which on a delete endpoint means two requests, and
    on `approvals/{id}/decide` means a second verdict on a request that no
    longer exists. The ref settles it synchronously, in the click handler.

    🚨 What it guards is "one action IN FLIGHT", so it has to be released when
    the action SETTLES, not when the dialog closes. Releasing it only on close
    was a defect an audit caught: the dialog deliberately stays open on a
    server refusal, so after a 409 the confirm button looked enabled (`busy` is
    false again) and did nothing at all — no request, no message, no way
    forward except backing out. The busy edge below is the actual lifetime.
  */
  const fired = useRef(false);
  const wasBusy = useRef(false);

  useEffect(() => {
    if (busy) {
      wasBusy.current = true;

      return;
    }

    // A falling edge of `busy` means the mutation settled, either way.
    if (wasBusy.current) {
      wasBusy.current = false;
      fired.current = false;
    }
  }, [busy]);

  useEffect(() => {
    if (!open) {
      fired.current = false;
      wasBusy.current = false;
    }
  }, [open]);

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title={title}
      initialFocus={cancel}
      width="max-w-md"
      testId={testId}
      footer={
        <>
          {/*
            Cancel comes FIRST in the footer, and holds the opening focus. The
            keyboard path through a confirmation should end on "nothing
            happened" unless the operator walks to the other answer.
          */}
          <Button ref={cancel} onClick={onClose} disabled={busy} testId="confirm-cancel">
            {t('common.cancel')}
          </Button>

          <Button
            tone={tone === 'danger' ? 'danger' : 'primary'}
            busy={busy}
            testId="confirm-accept"
            onClick={() => {
              if (fired.current) {
                return;
              }

              fired.current = true;
              onConfirm();
            }}
          >
            {confirmLabel}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3 px-4 py-3">
        <p className="text-body leading-relaxed text-muted">{consequence}</p>
        {error !== undefined && error !== null && <ErrorNote error={error} />}
      </div>
    </Dialog>
  );
}
