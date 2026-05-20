import { Toaster as SonnerToaster, type ToasterProps } from "sonner";
import { useAppStore } from "@foundation/src/store/app-store";

/**
 * App-wide toast renderer. Mount once in the root layout (AppLayout).
 * Use the `toast` function from "sonner" anywhere to fire toasts:
 *
 *   import { toast } from "sonner";
 *   toast.success("Request created");
 *   toast.error("Failed to schedule");
 */
export function Toaster(props: ToasterProps) {
  const resolvedTheme = useAppStore((s) => s.resolvedTheme);

  return (
    <SonnerToaster
      theme={resolvedTheme}
      position="bottom-right"
      richColors
      closeButton
      {...props}
    />
  );
}
