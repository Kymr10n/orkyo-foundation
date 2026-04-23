import { TemplateDialogBase } from "./TemplateDialogBase";
import type { Template } from "@foundation/src/types/templates";

interface EditTemplateDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  template: Template;
  onSuccess: () => void;
}

export function EditTemplateDialog({ template, ...props }: EditTemplateDialogProps) {
  return <TemplateDialogBase {...props} template={template} />;
}
