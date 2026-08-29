import * as React from "react"
import * as DialogPrimitive from "@radix-ui/react-dialog"
import { X } from "lucide-react"

import { cn } from "@foundation/src/lib/utils"
import { useBreakpoint } from "@foundation/src/hooks/useBreakpoint"

/**
 * Shared dialog width vocabulary. Use the `size` prop on `FormDialog` /
 * `ScaffoldDialog` instead of hardcoding `max-w-*` strings,
 * so the handful of dialog widths stay consistent across the app.
 *
 * Every entry is `sm:`-prefixed on purpose. Below that breakpoint the phone gutter on
 * `DialogContent` owns the width, and an unprefixed token here would out-specify it —
 * twMerge only collapses `max-w-*` against another `max-w-*` in the same modifier group.
 */
export type DialogSize = "sm" | "md" | "lg" | "xl"
export const DIALOG_SIZE: Record<DialogSize, string> = {
  sm: "sm:max-w-[440px]", // narrow forms
  md: "sm:max-w-[500px]", // default form width
  lg: "sm:max-w-2xl", // tall / complex dialogs
  xl: "sm:max-w-3xl", // wide enough for a data table
}

/**
 * Phone presentation for the form scaffolds: the dialog takes over the whole screen
 * (edge-to-edge, no centered card) instead of floating as a fixed-width box. That both
 * matches the native pattern and removes the "fixed box wider than the visual viewport"
 * bleed class — `w-full` resolves against the *layout* viewport, which a page with
 * horizontal overflow behind the dialog makes wider than the screen the user sees.
 *
 * Confirmation/alert dialogs deliberately keep the centered card. Desktop/tablet are
 * unchanged. Lives here, once, because `FormDialog` and `ScaffoldDialog` must not drift
 * apart on it.
 */
const PHONE_FULLSCREEN =
  "inset-0 h-[100dvh] max-h-[100dvh] w-full max-w-none translate-x-0 translate-y-0 rounded-none border-0"

/**
 * The phone override for a form scaffold's `DialogContent`, or `undefined` above the
 * phone breakpoint. Returns a class string so the caller can order it against its own
 * width token.
 */
const useFullScreenOnPhone = (): string | undefined => {
  const { isPhone } = useBreakpoint()
  return isPhone ? PHONE_FULLSCREEN : undefined
}

const Dialog = DialogPrimitive.Root

const DialogPortal = DialogPrimitive.Portal

const DialogOverlay = React.forwardRef<
  React.ComponentRef<typeof DialogPrimitive.Overlay>,
  React.ComponentPropsWithoutRef<typeof DialogPrimitive.Overlay>
>(({ className, ...props }, ref) => (
  <DialogPrimitive.Overlay
    ref={ref}
    className={cn(
      "fixed inset-0 z-50 bg-black/80 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 motion-reduce:animate-none",
      className
    )}
    {...props}
  />
))
DialogOverlay.displayName = DialogPrimitive.Overlay.displayName

const DialogContent = React.forwardRef<
  React.ComponentRef<typeof DialogPrimitive.Content>,
  React.ComponentPropsWithoutRef<typeof DialogPrimitive.Content>
>(({ className, children, ...props }, ref) => (
  <DialogPortal>
    <DialogOverlay />
    <DialogPrimitive.Content
      ref={ref}
      className={cn(
        // Height-bounded flex column by default (`max-h-[85dvh]`) so a dialog never grows past the
        // viewport. `overflow-y-auto` is the bleed backstop: any content taller than the cap scrolls
        // *inside* the box instead of spilling past it, so a dialog can never bleed regardless of how
        // its body is built. For the preferred experience — scroll only the body with header/footer
        // pinned — wrap tall content in <ScrollableDialogBody>; that body absorbs the overflow via
        // `flex-1 min-h-0`, leaving this outer scroller nothing to do (no double scrollbar). Callers
        // may still override max-h / overflow / gap / padding. `dvh` keeps clear of mobile chrome.
        // `overflow-x-hidden` is not decoration: CSS computes a `visible` axis to `auto` when the
        // other axis is not visible, so `overflow-y-auto` alone silently makes a dialog scroll
        // sideways — which is how a stray wide child pushes labels off the left edge.
        // The phone gutter (`max-w-[calc(100%-2rem)]`) keeps a raw dialog off both screen
        // edges. FormDialog and ScaffoldDialog never see it — their PHONE_FULLSCREEN sets
        // `max-w-none rounded-none` — so it is what a bare DialogContent or a confirm gets.
        "fixed left-[50%] top-[50%] z-50 flex max-h-[85dvh] w-full max-w-[calc(100%-2rem)] translate-x-[-50%] translate-y-[-50%] flex-col gap-4 overflow-x-hidden overflow-y-auto rounded-lg border bg-background p-6 shadow-lg duration-200 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 data-[state=closed]:slide-out-to-left-1/2 data-[state=closed]:slide-out-to-top-[48%] data-[state=open]:slide-in-from-left-1/2 data-[state=open]:slide-in-from-top-[48%] motion-reduce:animate-none sm:max-w-lg",
        className
      )}
      {...props}
    >
      {children}
      <DialogPrimitive.Close className="absolute right-4 top-4 rounded-sm opacity-70 ring-offset-background transition-opacity hover:opacity-100 focus:outline-hidden focus:ring-2 focus:ring-ring focus:ring-offset-2 disabled:pointer-events-none data-[state=open]:bg-accent data-[state=open]:text-muted-foreground">
        <X className="h-4 w-4" />
        <span className="sr-only">Close</span>
      </DialogPrimitive.Close>
    </DialogPrimitive.Content>
  </DialogPortal>
))
DialogContent.displayName = DialogPrimitive.Content.displayName

/**
 * The single sanctioned scroll region for a dialog. Place tall content here so
 * the header and footer stay pinned while only the body scrolls. Use inside a
 * height-bounded, `flex flex-col` `DialogContent` (e.g. `max-h-[85dvh]`); the
 * paired `flex-1 min-h-0` is what makes the body actually scroll instead of
 * pushing the dialog past the viewport.
 *
 * This replaces the three divergent per-dialog overflow recipes
 * (`overflow-y-auto` on content / nested `ScrollArea` / inner `overflow-y-auto`
 * div) with one consistent affordance.
 *
 * Vertical only: `overflow-x-hidden` is explicit because CSS computes a `visible`
 * axis to `auto` whenever the other axis is not visible, so `overflow-y-auto` on its
 * own turns a form body into a sideways scroller the moment one child is too wide —
 * the labels then scroll out of view while the pinned footer stays put. Content that
 * genuinely must scroll sideways (a wide table) brings its own `overflow-x-auto`
 * container.
 */
const ScrollableDialogBody = ({
  className,
  ...props
}: React.HTMLAttributes<HTMLDivElement>) => (
  <div
    className={cn("flex-1 min-h-0 overflow-x-hidden overflow-y-auto", className)}
    {...props}
  />
)
ScrollableDialogBody.displayName = "ScrollableDialogBody"

const DialogHeader = ({
  className,
  ...props
}: React.HTMLAttributes<HTMLDivElement>) => (
  <div
    className={cn(
      "flex flex-col space-y-1.5 text-center sm:text-left",
      className
    )}
    {...props}
  />
)
DialogHeader.displayName = "DialogHeader"

const DialogFooter = ({
  className,
  ...props
}: React.HTMLAttributes<HTMLDivElement>) => (
  <div
    className={cn(
      "flex flex-col-reverse sm:flex-row sm:justify-end sm:space-x-2",
      className
    )}
    {...props}
  />
)
DialogFooter.displayName = "DialogFooter"

const DialogTitle = React.forwardRef<
  React.ComponentRef<typeof DialogPrimitive.Title>,
  React.ComponentPropsWithoutRef<typeof DialogPrimitive.Title>
>(({ className, ...props }, ref) => (
  <DialogPrimitive.Title
    ref={ref}
    className={cn(
      "text-lg font-semibold leading-none tracking-tight",
      className
    )}
    {...props}
  />
))
DialogTitle.displayName = DialogPrimitive.Title.displayName

const DialogDescription = React.forwardRef<
  React.ComponentRef<typeof DialogPrimitive.Description>,
  React.ComponentPropsWithoutRef<typeof DialogPrimitive.Description>
>(({ className, ...props }, ref) => (
  <DialogPrimitive.Description
    ref={ref}
    className={cn("text-sm text-muted-foreground", className)}
    {...props}
  />
))
DialogDescription.displayName = DialogPrimitive.Description.displayName

export {
  useFullScreenOnPhone,
  Dialog,
  DialogContent,
  DialogHeader,
  DialogFooter,
  DialogTitle,
  DialogDescription,
  ScrollableDialogBody,
}
