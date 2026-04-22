import { apiPost } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

export type FeedbackType = 'bug' | 'feature' | 'question' | 'other';

interface CreateFeedbackRequest {
  feedbackType: FeedbackType;
  title: string;
  description?: string;
  pageUrl?: string;
}

interface FeedbackResponse {
  id: string;
  feedbackType: FeedbackType;
  title: string;
  description?: string;
  status: string;
  createdAt: string;
}

export async function submitFeedback(request: CreateFeedbackRequest): Promise<FeedbackResponse> {
  return apiPost<FeedbackResponse>(API_PATHS.FEEDBACK, request);
}
