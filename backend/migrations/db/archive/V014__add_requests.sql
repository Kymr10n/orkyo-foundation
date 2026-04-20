-- V014: Add requests table
-- Requests represent space-time utilization requests with duration and scheduling constraints

CREATE TABLE requests (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(200) NOT NULL,
    description TEXT,
    
    -- Space and item references
    space_id UUID REFERENCES spaces(id) ON DELETE SET NULL,
    request_item_id VARCHAR(100), -- External reference to job/entity
    
    -- Time constraints (nullable for unscheduled requests)
    start_ts TIMESTAMP WITH TIME ZONE,
    end_ts TIMESTAMP WITH TIME ZONE,
    
    -- Duration (stored separately for scheduling flexibility)
    duration_value INTEGER NOT NULL CHECK (duration_value > 0),
    duration_unit VARCHAR(20) NOT NULL CHECK (duration_unit IN ('minutes', 'hours', 'days', 'weeks', 'months', 'years')),
    
    -- Fixed flags (scheduling constraints)
    fixed_start BOOLEAN DEFAULT FALSE,
    fixed_end BOOLEAN DEFAULT FALSE,
    fixed_duration BOOLEAN DEFAULT FALSE,
    
    -- Status
    status VARCHAR(20) NOT NULL DEFAULT 'planned' CHECK (status IN ('planned', 'in_progress', 'done', 'cancelled')),
    
    -- Metadata
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    -- Constraints
    CONSTRAINT valid_time_range CHECK (
        (start_ts IS NULL AND end_ts IS NULL) OR 
        (start_ts IS NOT NULL AND end_ts IS NOT NULL AND end_ts > start_ts)
    )
);

-- Indexes for common queries
CREATE INDEX idx_requests_space_id ON requests(space_id);
CREATE INDEX idx_requests_status ON requests(status);
CREATE INDEX idx_requests_start_ts ON requests(start_ts);
CREATE INDEX idx_requests_end_ts ON requests(end_ts);
CREATE INDEX idx_requests_time_range ON requests(start_ts, end_ts);

-- Request requirements (criterion values)
CREATE TABLE request_requirements (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    request_id UUID NOT NULL REFERENCES requests(id) ON DELETE CASCADE,
    criterion_id UUID NOT NULL REFERENCES criteria(id) ON DELETE CASCADE,
    value JSONB NOT NULL, -- Stores the criterion value (type depends on criterion.data_type)
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    -- Each request can have each criterion only once
    UNIQUE(request_id, criterion_id)
);

CREATE INDEX idx_request_requirements_request_id ON request_requirements(request_id);
CREATE INDEX idx_request_requirements_criterion_id ON request_requirements(criterion_id);

-- Update trigger for updated_at
CREATE OR REPLACE FUNCTION update_requests_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trigger_requests_updated_at
    BEFORE UPDATE ON requests
    FOR EACH ROW
    EXECUTE FUNCTION update_requests_updated_at();

-- Comments
COMMENT ON TABLE requests IS 'Space-time utilization requests with scheduling constraints';
COMMENT ON COLUMN requests.duration_value IS 'Duration as integer value';
COMMENT ON COLUMN requests.duration_unit IS 'Duration unit: minutes, hours, days, weeks, months, years';
COMMENT ON COLUMN requests.fixed_start IS 'If true, start time cannot be changed during scheduling';
COMMENT ON COLUMN requests.fixed_end IS 'If true, end time cannot be changed during scheduling';
COMMENT ON COLUMN requests.fixed_duration IS 'If true, duration cannot be changed during scheduling';
COMMENT ON TABLE request_requirements IS 'Criterion requirements for requests';
