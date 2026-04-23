import { useState } from 'react';
import { useLocation } from 'react-router-dom';
import { MessageSquarePlus, Bug, Lightbulb, HelpCircle, MoreHorizontal, Loader2, CheckCircle } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@foundation/src/components/ui/dialog';
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import { Textarea } from '@foundation/src/components/ui/textarea';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@foundation/src/components/ui/select';
import { submitFeedback, type FeedbackType } from '@foundation/src/lib/api/feedback-api';
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@foundation/src/components/ui/tooltip';

const feedbackTypes: { value: FeedbackType; label: string; icon: React.ReactNode; description: string }[] = [
  { value: 'bug', label: 'Bug Report', icon: <Bug className="h-4 w-4" />, description: 'Something isn\'t working correctly' },
  { value: 'feature', label: 'Feature Request', icon: <Lightbulb className="h-4 w-4" />, description: 'Suggest a new feature or improvement' },
  { value: 'question', label: 'Question', icon: <HelpCircle className="h-4 w-4" />, description: 'Ask for help or clarification' },
  { value: 'other', label: 'Other', icon: <MoreHorizontal className="h-4 w-4" />, description: 'General feedback' },
];

export function FeedbackButton() {
  const location = useLocation();
  const [open, setOpen] = useState(false);
  const [feedbackType, setFeedbackType] = useState<FeedbackType>('bug');
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [submitted, setSubmitted] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const resetForm = () => {
    setFeedbackType('bug');
    setTitle('');
    setDescription('');
    setError(null);
    setSubmitted(false);
  };

  const handleOpenChange = (newOpen: boolean) => {
    setOpen(newOpen);
    if (!newOpen) {
      // Reset form when closing (after a short delay to not show reset during close animation)
      setTimeout(resetForm, 200);
    }
  };

  const handleSubmit = async () => {
    if (!title.trim()) {
      setError('Please enter a title');
      return;
    }

    setIsSubmitting(true);
    setError(null);

    try {
      await submitFeedback({
        feedbackType,
        title: title.trim(),
        description: description.trim() || undefined,
        pageUrl: location.pathname,
      });
      setSubmitted(true);
      // Auto-close after success
      setTimeout(() => {
        handleOpenChange(false);
      }, 1500);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to submit feedback');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <>
      <TooltipProvider>
        <Tooltip>
          <TooltipTrigger asChild>
            <Button
              variant="outline"
              size="icon"
              className="fixed bottom-4 right-4 h-12 w-12 rounded-full shadow-lg z-50 bg-background hover:bg-accent"
              onClick={() => setOpen(true)}
              aria-label="Send feedback"
            >
              <MessageSquarePlus className="h-5 w-5" />
            </Button>
          </TooltipTrigger>
          <TooltipContent side="left">
            <p>Send Feedback</p>
          </TooltipContent>
        </Tooltip>
      </TooltipProvider>

      <Dialog open={open} onOpenChange={handleOpenChange}>
        <DialogContent className="sm:max-w-[425px]">
          <DialogHeader>
            <DialogTitle>Send Feedback</DialogTitle>
            <DialogDescription>
              Help us improve by sharing your thoughts, reporting bugs, or requesting features.
            </DialogDescription>
          </DialogHeader>

          {submitted ? (
            <div className="flex flex-col items-center justify-center py-8 text-center">
              <CheckCircle className="h-12 w-12 text-green-500 mb-4" />
              <p className="text-lg font-medium">Thank you!</p>
              <p className="text-sm text-muted-foreground">Your feedback has been submitted.</p>
            </div>
          ) : (
            <div className="space-y-4 py-4">
              <div className="space-y-2">
                <Label htmlFor="feedback-type">Type</Label>
                <Select
                  value={feedbackType}
                  onValueChange={(value) => setFeedbackType(value as FeedbackType)}
                >
                  <SelectTrigger id="feedback-type">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {feedbackTypes.map((type) => (
                      <SelectItem key={type.value} value={type.value}>
                        <div className="flex items-center gap-2">
                          {type.icon}
                          <span>{type.label}</span>
                        </div>
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <p className="text-xs text-muted-foreground">
                  {feedbackTypes.find((t) => t.value === feedbackType)?.description}
                </p>
              </div>

              <div className="space-y-2">
                <Label htmlFor="feedback-title">Title *</Label>
                <Input
                  id="feedback-title"
                  placeholder="Brief summary of your feedback"
                  value={title}
                  onChange={(e) => setTitle(e.target.value)}
                  maxLength={200}
                />
              </div>

              <div className="space-y-2">
                <Label htmlFor="feedback-description">Description</Label>
                <Textarea
                  id="feedback-description"
                  placeholder="Provide more details (optional)"
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                  rows={4}
                />
              </div>

              {error && (
                <p className="text-sm text-destructive">{error}</p>
              )}
            </div>
          )}

          {!submitted && (
            <DialogFooter>
              <Button variant="outline" onClick={() => handleOpenChange(false)}>
                Cancel
              </Button>
              <Button onClick={handleSubmit} disabled={isSubmitting}>
                {isSubmitting && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
                Submit
              </Button>
            </DialogFooter>
          )}
        </DialogContent>
      </Dialog>
    </>
  );
}
