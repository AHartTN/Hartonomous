-- ==============================================================================
-- TRIGGERS: Maintain data integrity
-- ==============================================================================

-- Trigger: Validate S³ normalization on insert/update
CREATE OR REPLACE FUNCTION check_s3_normalization()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
    norm_sq DOUBLE PRECISION;
BEGIN
    norm_sq := NEW.s3_x * NEW.s3_x + NEW.s3_y * NEW.s3_y +
               NEW.s3_z * NEW.s3_z + NEW.s3_w * NEW.s3_w;

    IF ABS(norm_sq - 1.0) > 0.0001 THEN
        RAISE EXCEPTION 'S³ point not normalized: ||p||² = %', norm_sq;
    END IF;

    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_atoms_s3_normalization
BEFORE INSERT OR UPDATE ON atoms
FOR EACH ROW
EXECUTE FUNCTION check_s3_normalization();