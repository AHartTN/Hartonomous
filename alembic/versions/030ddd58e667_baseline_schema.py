"""baseline_schema

Revision ID: 030ddd58e667
Revises: 
Create Date: 2025-11-25 14:45:35.577178

"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision: str = '030ddd58e667'
down_revision: Union[str, None] = None
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    # Mark as baseline - existing schema already deployed
    # Extensions
    op.execute('CREATE EXTENSION IF NOT EXISTS postgis')
    op.execute('CREATE EXTENSION IF NOT EXISTS pg_trgm')
    op.execute('CREATE EXTENSION IF NOT EXISTS btree_gin')
    op.execute('CREATE EXTENSION IF NOT EXISTS pgcrypto')


def downgrade() -> None:
    # Cannot downgrade baseline
    pass
