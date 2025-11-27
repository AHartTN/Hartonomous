"""Add Hilbert index and encoding dimension

Revision ID: 031_add_hilbert_and_encoding
Revises: 030ddd58e667
Create Date: 2025-11-27 12:40:00.000000

"""
from alembic import op
import sqlalchemy as sa
from sqlalchemy.dialects import postgresql

# revision identifiers, used by Alembic.
revision = '031_add_hilbert_and_encoding'
down_revision = '030ddd58e667'
branch_labels = None
depends_on = None


def upgrade():
    # 1. Add hilbert_index column
    op.add_column('atom', 
        sa.Column('hilbert_index', sa.BigInteger(), nullable=True)
    )
    
    # 2. Upgrade spatial_key from PointZ to PointZM
    op.execute("""
        ALTER TABLE atom 
        ALTER COLUMN spatial_key 
        TYPE geometry(PointZM, 0)
        USING CASE 
            WHEN spatial_key IS NOT NULL THEN
                ST_MakePoint(
                    ST_X(spatial_key)::FLOAT8,
                    ST_Y(spatial_key)::FLOAT8,
                    ST_Z(spatial_key)::FLOAT8,
                    0.0  -- Default M = 0 (raw/unencoded)
                )::geometry(PointZM, 0)
            ELSE NULL
        END;
    """)
    
    # 3. Populate hilbert_index for existing data using Z-order (Morton) as placeholder
    # Will be replaced with true Hilbert encoding once functions are ready
    op.execute("""
        UPDATE atom
        SET hilbert_index = (
            -- Simple Morton code placeholder (Z-order)
            -- Will be replaced by proper Hilbert encoding
            (floor(ST_X(spatial_key)::numeric * 1000000)::bigint << 42) |
            (floor(ST_Y(spatial_key)::numeric * 1000000)::bigint << 21) |
            (floor(ST_Z(spatial_key)::numeric * 1000000)::bigint)
        )
        WHERE spatial_key IS NOT NULL AND hilbert_index IS NULL;
    """)
    
    # 4. Set default for new rows (will be properly calculated by trigger)
    op.execute("""
        UPDATE atom
        SET hilbert_index = 0
        WHERE hilbert_index IS NULL;
    """)
    
    # 5. Make hilbert_index NOT NULL now that all rows have values
    op.alter_column('atom', 'hilbert_index', nullable=False)
    
    # 6. Drop old spatial index
    op.drop_index('idx_atom_spatial', table_name='atom')
    
    # 7. Create new optimized indexes
    # 4D GiST index using N-dimensional operator class
    op.execute("""
        CREATE INDEX idx_atom_spatial_4d 
        ON atom 
        USING GIST (spatial_key gist_geometry_ops_nd)
        WHERE spatial_key IS NOT NULL;
    """)
    
    # B-tree index for Hilbert range queries
    op.create_index('idx_atom_hilbert', 'atom', ['hilbert_index'])
    
    # B-tree index for M dimension (encoding type) filtering
    op.execute("""
        CREATE INDEX idx_atom_encoding 
        ON atom ((ST_M(spatial_key)))
        WHERE spatial_key IS NOT NULL;
    """)
    
    # Combined index for Hilbert + encoding filtering
    op.execute("""
        CREATE INDEX idx_atom_hilbert_encoding 
        ON atom (hilbert_index, (ST_M(spatial_key)));
    """)
    
    # 8. Add comment documentation
    op.execute("""
        COMMENT ON COLUMN atom.spatial_key IS 
        'PointZM geometry where:
        - X = Modality dimension (code/text/image/audio/video)
        - Y = Category dimension (class/method/field/pixel/sample)
        - Z = Specificity dimension (abstract → concrete)
        - M = Encoding metadata (0=raw, 1=sparse, 2=delta, 3=RLE, 4+=LOD_level)';
    """)
    
    op.execute("""
        COMMENT ON COLUMN atom.hilbert_index IS 
        '1D Hilbert curve index for O(log n) spatial range queries. 
        Provides locality-preserving ordering superior to Morton/Z-order.';
    """)


def downgrade():
    # Remove new indexes
    op.drop_index('idx_atom_hilbert_encoding', table_name='atom')
    op.drop_index('idx_atom_encoding', table_name='atom')
    op.drop_index('idx_atom_hilbert', table_name='atom')
    op.drop_index('idx_atom_spatial_4d', table_name='atom')
    
    # Restore old spatial index
    op.execute("""
        CREATE INDEX idx_atom_spatial 
        ON atom 
        USING GIST (spatial_key)
        WHERE spatial_key IS NOT NULL;
    """)
    
    # Remove hilbert_index column
    op.drop_column('atom', 'hilbert_index')
    
    # Downgrade spatial_key from PointZM back to PointZ
    op.execute("""
        ALTER TABLE atom 
        ALTER COLUMN spatial_key 
        TYPE geometry(PointZ, 0)
        USING CASE 
            WHEN spatial_key IS NOT NULL THEN
                ST_MakePoint(
                    ST_X(spatial_key)::FLOAT8,
                    ST_Y(spatial_key)::FLOAT8,
                    ST_Z(spatial_key)::FLOAT8
                )::geometry(PointZ, 0)
            ELSE NULL
        END;
    """)
