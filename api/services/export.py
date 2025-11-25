"""
Export service for model serialization (ONNX, etc.).

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import logging
import os
from typing import List, Dict, Any
from psycopg import AsyncConnection
from psycopg.rows import dict_row

logger = logging.getLogger(__name__)


class ExportService:
    """Service for exporting models to various formats."""
    
    @staticmethod
    async def export_to_onnx(
        conn: AsyncConnection,
        atom_ids: List[int],
        model_name: str,
        output_path: str = None
    ) -> Dict[str, Any]:
        """
        Export atoms and relations to ONNX format.
        
        Args:
            conn: Database connection
            atom_ids: List of atom IDs to export
            model_name: Model name
            output_path: Output file path (optional)
        
        Returns:
            dict: {
                output_path: str,
                atom_count: int,
                relation_count: int,
                file_size_bytes: int
            }
        """
        try:
            # Default output path - use secure temporary directory
            if not output_path:
                import tempfile
                temp_dir = tempfile.gettempdir()  # nosec B108 - Using system temp dir
                output_path = os.path.join(temp_dir, f"{model_name}.onnx")
            
            # Ensure directory exists
            os.makedirs(os.path.dirname(output_path), exist_ok=True)
            
            async with conn.cursor(row_factory=dict_row) as cur:
                # Call SQL export function
                await cur.execute("""
                    SELECT export_to_onnx(
                        %s::bigint[],
                        %s
                    ) as success;
                """, (atom_ids, output_path))
                
                result = await cur.fetchone()
                success = result["success"] if result else False
                
                if not success:
                    raise RuntimeError("ONNX export failed (SQL function returned false)")
                
                # Get file size
                file_size = os.path.getsize(output_path) if os.path.exists(output_path) else 0
                
                # Count exported atoms
                await cur.execute("""
                    SELECT COUNT(*) as count
                    FROM atom
                    WHERE atom_id = ANY(%s);
                """, (atom_ids,))
                
                atom_count_result = await cur.fetchone()
                atom_count = atom_count_result["count"] if atom_count_result else 0
                
                # Count exported relations
                await cur.execute("""
                    SELECT COUNT(*) as count
                    FROM atom_relation
                    WHERE source_atom_id = ANY(%s)
                    AND target_atom_id = ANY(%s);
                """, (atom_ids, atom_ids))
                
                relation_count_result = await cur.fetchone()
                relation_count = relation_count_result["count"] if relation_count_result else 0
                
                logger.info(
                    f"Exported ONNX model '{model_name}': "
                    f"{atom_count} atoms, {relation_count} relations "
                    f"({file_size} bytes) ? {output_path}"
                )
                
                return {
                    "output_path": output_path,
                    "atom_count": atom_count,
                    "relation_count": relation_count,
                    "file_size_bytes": file_size
                }
        
        except Exception as e:
            logger.error(f"ONNX export failed: {e}", exc_info=True)
            raise


__all__ = ["ExportService"]
