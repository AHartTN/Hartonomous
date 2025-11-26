"""
Training service for in-database machine learning.

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import logging
from typing import Any, Dict, List

from psycopg import AsyncConnection
from psycopg.rows import dict_row

logger = logging.getLogger(__name__)


class TrainingService:
    """Service for in-database ML training."""

    @staticmethod
    async def train_batch(
        conn: AsyncConnection,
        samples: List[Dict[str, Any]],
        learning_rate: float,
        epochs: int = 1,
    ) -> Dict[str, Any]:
        """
        Train on batch of samples using vectorized SGD.

        Args:
            conn: Database connection
            samples: List of {input_atom_ids, target_atom_id}
            learning_rate: Learning rate for gradient descent
            epochs: Number of training epochs

        Returns:
            dict: {
                sample_results: List[{sample_index, loss, converged}],
                average_loss: float,
                min_loss: float,
                max_loss: float,
                convergence_rate: float
            }
        """
        try:
            all_losses = []
            converged_count = 0

            for epoch in range(epochs):
                logger.info(f"Training epoch {epoch + 1}/{epochs}...")

                # Convert samples to JSONB array for SQL function
                import json

                samples_jsonb = [json.dumps(s) for s in samples]

                async with conn.cursor(row_factory=dict_row) as cur:
                    # Call vectorized training function
                    await cur.execute(
                        """
                        SELECT * FROM train_batch_vectorized(
                            %s::jsonb[],
                            %s
                        );
                    """,
                        (samples_jsonb, learning_rate),
                    )

                    epoch_results = []
                    async for row in cur:
                        epoch_results.append(dict(row))

                    # Collect losses
                    epoch_losses = [r["loss"] for r in epoch_results]
                    all_losses.extend(epoch_losses)

                    # Count converged (loss < 0.01)
                    epoch_converged = sum(1 for loss in epoch_losses if loss < 0.01)
                    converged_count += epoch_converged

                    logger.info(
                        f"Epoch {epoch + 1} complete: "
                        f"avg_loss={sum(epoch_losses)/len(epoch_losses):.6f}, "
                        f"converged={epoch_converged}/{len(samples)}"
                    )

            # Calculate statistics
            total_samples = len(samples) * epochs
            average_loss = sum(all_losses) / len(all_losses) if all_losses else 0.0
            min_loss = min(all_losses) if all_losses else 0.0
            max_loss = max(all_losses) if all_losses else 0.0
            convergence_rate = (
                converged_count / total_samples if total_samples > 0 else 0.0
            )

            logger.info(
                f"Training complete: {total_samples} samples, "
                f"avg_loss={average_loss:.6f}, "
                f"convergence_rate={convergence_rate:.2%}"
            )

            return {
                "total_samples": total_samples,
                "average_loss": average_loss,
                "min_loss": min_loss,
                "max_loss": max_loss,
                "convergence_rate": convergence_rate,
            }

        except Exception as e:
            logger.error(f"Batch training failed: {e}", exc_info=True)
            raise


__all__ = ["TrainingService"]
