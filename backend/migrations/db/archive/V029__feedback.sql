-- Feedback table for in-app user feedback
-- Stores feedback for admin review before optionally creating GitHub issues

CREATE TABLE feedback (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID REFERENCES users(id) ON DELETE SET NULL,
    feedback_type VARCHAR(20) NOT NULL CHECK (feedback_type IN ('bug', 'feature', 'question', 'other')),
    title VARCHAR(200) NOT NULL,
    description TEXT,
    page_url TEXT,
    user_agent TEXT,
    status VARCHAR(20) NOT NULL DEFAULT 'new' CHECK (status IN ('new', 'reviewed', 'resolved', 'wont_fix')),
    admin_notes TEXT,
    github_issue_url TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Index for admin filtering by status
CREATE INDEX idx_feedback_status ON feedback(status);

-- Index for listing feedback newest first
CREATE INDEX idx_feedback_created ON feedback(created_at DESC);

COMMENT ON TABLE feedback IS 'User feedback submissions for admin review';
COMMENT ON COLUMN feedback.feedback_type IS 'Type: bug, feature, question, or other';
COMMENT ON COLUMN feedback.status IS 'Review status: new, reviewed, resolved, or wont_fix';
COMMENT ON COLUMN feedback.github_issue_url IS 'Link to GitHub issue if created from this feedback';
