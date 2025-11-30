"""fractal composition - composition_ids in atom table

Revision ID: 033_fractal_composition
Revises: 032_multi_layer_compression
Create Date: 2025-11-30

BREAKTHROUGH: Compositions ARE atoms (recursive)
- Add composition_ids BIGINT[] to atom table
- Add is_stable BOOLEAN flag
- Add constraint: atom_value XOR composition_ids
- Add GIN index on composition_ids for fast child lookup
- Deprecate atom_composition table (legacy)

This enables:
- O(1) deduplication via coordinate collision
- "Lorem Ipsum" 1000x = one atom referenced 1000 times
- Fractal compression of repetitive content

"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa
from sqlalchemy.dialects import postgresql

# revision identifiers, used by Alembic.
revision: str = '033_fractal_composition'
down_revision: Union[str, None] = '032_multi_layer_compression'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    """Add fractal composition support to atom table."""
    
    # Add composition_ids column (BIGINT array)
    op.add_column('atom', sa.Column('composition_ids', postgresql.ARRAY(sa.BigInteger()), nullable=True))
    
    # Add is_stable column (transient vs permanent)
    op.add_column('atom', sa.Column('is_stable', sa.Boolean(), nullable=False, server_default='false'))
    
    # Rename atomic_value to atom_value (consistency)
    op.alter_column('atom', 'atomic_value', new_column_name='atom_value')
    
    # Add constraint: must have EITHER atom_value OR composition_ids (not both, not neither)
    op.create_check_constraint(
        'atom_value_xor_composition',
        'atom',
        '(atom_value IS NOT NULL AND composition_ids IS NULL) OR (atom_value IS NULL AND composition_ids IS NOT NULL)'
    )
    
    # Create GIN index on composition_ids for fast child lookup
    op.create_index(
        'idx_atom_composition_gin',
        'atom',
        ['composition_ids'],
        unique=False,
        postgresql_using='gin',
        postgresql_where=sa.text('composition_ids IS NOT NULL')
    )
    
    # Create btree index on is_stable for filtering
    op.create_index(
        'idx_atom_stable',
        'atom',
        ['is_stable'],
        unique=False
    )
    
    # Update table comment
    op.execute("""
        COMMENT ON TABLE atom IS 'Fractal recursive composition table. EVERYTHING is an atom - primitives AND compositions. Enables O(1) deduplication via spatial coordinates.'
    """)
    
    # Update column comments
    op.execute("""
        COMMENT ON COLUMN atom.atom_value IS 'Primitive value (≤64 bytes). NULL for compositions.';
        COMMENT ON COLUMN atom.composition_ids IS 'Array of child atom IDs. NULL for primitives. This IS the composition!';
        COMMENT ON COLUMN atom.is_stable IS 'FALSE: transient (streaming buffer). TRUE: stable (permanent, reusable concept).';
        COMMENT ON COLUMN atom.content_hash IS 'SHA-256 hash - for primitives: hash(atom_value), for compositions: hash(composition_ids)';
    """)
    
    # Mark atom_composition table as deprecated
    op.execute("""
        COMMENT ON TABLE atom_composition IS '[DEPRECATED] Use atom.composition_ids instead. Legacy table for explicit parent-child tracking.'
    """)


def downgrade() -> None:
    """Remove fractal composition support."""
    
    # Drop indexes
    op.drop_index('idx_atom_stable', table_name='atom')
    op.drop_index('idx_atom_composition_gin', table_name='atom')
    
    # Drop constraint
    op.drop_constraint('atom_value_xor_composition', 'atom', type_='check')
    
    # Rename column back
    op.alter_column('atom', 'atom_value', new_column_name='atomic_value')
    
    # Drop columns
    op.drop_column('atom', 'is_stable')
    op.drop_column('atom', 'composition_ids')
    
    # Restore old comments
    op.execute("""
        COMMENT ON TABLE atom IS 'Content-addressable storage for all unique values ≤64 bytes. Every piece of knowledge atomized to this table.';
        COMMENT ON COLUMN atom.atomic_value IS 'The actual value stored (≤64 bytes). Larger values must be decomposed.';
        COMMENT ON COLUMN atom.content_hash IS 'SHA-256 hash of atomic_value - ensures global deduplication';
        COMMENT ON TABLE atom_composition IS 'Hierarchical composition: defines structure (what contains what, in what order). Sparse by default - missing sequence_index = implicit zero.';
    """)
