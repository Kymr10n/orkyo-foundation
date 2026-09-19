import type { ReactNode } from 'react';

/**
 * Stand-in for `@foundation/src/components/ui/dialog`: the shell renders as plain
 * elements (no Radix portal or focus trap) so tests can query the dialog by role.
 * `vi.mock` is hoisted above imports, so each test file declares the mock itself and
 * points it here:
 *
 *   vi.mock('@foundation/src/components/ui/dialog', () => import('@foundation/src/test-utils/dialog-mock'));
 */

// The scaffolds ask for the phone override; this stub is always above that breakpoint.
export const useFullScreenOnPhone = () => undefined;
export const DIALOG_SIZE = { sm: '', md: '', lg: '', xl: '' };
export const Dialog = ({ children, open }: { children: ReactNode; open: boolean }) =>
  open ? <div role="dialog">{children}</div> : null;
export const DialogContent = ({ children }: { children: ReactNode }) => <div>{children}</div>;
export const ScrollableDialogBody = ({ children }: { children: ReactNode }) => <div>{children}</div>;
export const DialogHeader = ({ children }: { children: ReactNode }) => <div>{children}</div>;
export const DialogTitle = ({ children }: { children: ReactNode }) => <h2>{children}</h2>;
export const DialogDescription = ({ children }: { children: ReactNode }) => <p>{children}</p>;
export const DialogFooter = ({ children }: { children: ReactNode }) => <div>{children}</div>;
