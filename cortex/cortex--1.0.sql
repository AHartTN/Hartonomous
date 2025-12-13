-- Cortex Extension Installation Script

-- Create extension
CREATE EXTENSION IF NOT EXISTS cortex;

-- Manual cycle trigger function
CREATE OR REPLACE FUNCTION cortex_cycle_once()
RETURNS BOOLEAN
AS 'MODULE_PATHNAME'
LANGUAGE C STRICT;

COMMENT ON FUNCTION cortex_cycle_once() IS 
'Manually trigger one Cortex recalibration cycle (for testing)';
