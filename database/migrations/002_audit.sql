-- Migration: Add audit triggers
-- Version: 1.0.1
-- Date: 2025-12-13

-- Drop existing audit objects for idempotent deployment
DROP TRIGGER IF EXISTS trg_audit_atoms ON atom CASCADE;
DROP FUNCTION IF EXISTS audit_atom_changes() CASCADE;
DROP FUNCTION IF EXISTS get_atom_trajectory(BYTEA, TIMESTAMPTZ, TIMESTAMPTZ) CASCADE;
DROP TABLE IF EXISTS atom_audit_log CASCADE;

-- Audit log table
CREATE TABLE atom_audit_log (
    id BIGSERIAL PRIMARY KEY,
    atom_id BYTEA NOT NULL,
    operation CHAR(1) NOT NULL, -- I=Insert, U=Update, D=Delete
    old_geom GEOMETRY(POINTZM, 0),
    new_geom GEOMETRY(POINTZM, 0),
    changed_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    changed_by TEXT DEFAULT current_user
);

CREATE INDEX idx_audit_atom_id ON atom_audit_log(atom_id);
CREATE INDEX idx_audit_changed_at ON atom_audit_log(changed_at);

-- Audit trigger function
CREATE OR REPLACE FUNCTION audit_atom_changes()
RETURNS TRIGGER AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        INSERT INTO atom_audit_log (atom_id, operation, new_geom)
        VALUES (NEW.atom_id, 'I', NEW.geom);
        RETURN NEW;
    ELSIF TG_OP = 'UPDATE' THEN
        -- Only log if geometry changed
        IF NOT ST_Equals(OLD.geom, NEW.geom) THEN
            INSERT INTO atom_audit_log (atom_id, operation, old_geom, new_geom)
            VALUES (NEW.atom_id, 'U', OLD.geom, NEW.geom);
        END IF;
        RETURN NEW;
    ELSIF TG_OP = 'DELETE' THEN
        INSERT INTO atom_audit_log (atom_id, operation, old_geom)
        VALUES (OLD.atom_id, 'D', OLD.geom);
        RETURN OLD;
    END IF;
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

-- Apply trigger to atom
CREATE TRIGGER trg_audit_atoms
    AFTER INSERT OR UPDATE OR DELETE ON atom
    FOR EACH ROW
    EXECUTE FUNCTION audit_atom_changes();

-- Function: Get atom movement history
CREATE OR REPLACE FUNCTION get_atom_trajectory(
    target_atom_id BYTEA,
    lookback_hours INTEGER DEFAULT 24
)
RETURNS TABLE(
    changed_at TIMESTAMPTZ,
    x DOUBLE PRECISION,
    y DOUBLE PRECISION,
    z DOUBLE PRECISION,
    m DOUBLE PRECISION
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        aal.changed_at,
        ST_X(aal.new_geom) as x,
        ST_Y(aal.new_geom) as y,
        ST_Z(aal.new_geom) as z,
        ST_M(aal.new_geom) as m
    FROM atom_audit_log aal
    WHERE aal.atom_id = target_atom_id
        AND aal.operation IN ('I', 'U')
        AND aal.changed_at > now() - (lookback_hours || ' hours')::INTERVAL
    ORDER BY aal.changed_at ASC;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION get_atom_trajectory(BYTEA, INTEGER) IS
'Track how an atom has moved through semantic space over time';
