import { TemplateDialogBase } from "./TemplateDialogBase";

interface CreateTemplateDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSuccess: () => void;
  entityType?: 'request' | 'space' | 'group';
}

export function CreateTemplateDialog(props: CreateTemplateDialogProps) {
  return <TemplateDialogBase {...props} template={null} />;
}
