interface ErrorAlertProps {
  message: string | null;
}

export function ErrorAlert({ message }: ErrorAlertProps) {
  if (!message) return null;
  return (
    <div className="text-sm text-destructive bg-destructive/10 p-3 rounded-md">
      {message}
    </div>
  );
}
