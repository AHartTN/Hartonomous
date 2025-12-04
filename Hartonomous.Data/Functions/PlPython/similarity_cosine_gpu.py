"""
GPU-accelerated cosine similarity computation for constant embeddings.
Used for content similarity detection and deduplication optimization.
"""

def similarity_cosine_gpu(target_embedding, embeddings, top_k):
    """
    Compute cosine similarity between target embedding and batch of embeddings using GPU.
    
    Args:
        target_embedding: Array of floats representing target embedding vector
        embeddings: Array of tuples [(id, embedding_array), ...]
        top_k: Number of most similar embeddings to return
    
    Returns:
        Array of tuples [(id, similarity_score), ...] sorted by similarity descending
    """
    try:
        import cupy as cp
        import numpy as np
        
        # Convert target to CuPy array and normalize
        target = cp.array(target_embedding, dtype=cp.float32)
        target_norm = cp.linalg.norm(target)
        if target_norm > 0:
            target = target / target_norm
        
        # Extract IDs and embeddings
        ids = [emb[0] for emb in embeddings]
        embedding_matrix = cp.array([emb[1] for emb in embeddings], dtype=cp.float32)
        
        # Normalize embedding matrix rows
        norms = cp.linalg.norm(embedding_matrix, axis=1, keepdims=True)
        norms = cp.where(norms > 0, norms, 1.0)  # Avoid division by zero
        embedding_matrix = embedding_matrix / norms
        
        # Compute cosine similarity: dot product of normalized vectors
        similarities = cp.dot(embedding_matrix, target)
        
        # Find top k most similar
        if top_k >= len(similarities):
            top_k = len(similarities)
        
        # Get indices of top k values
        top_k_indices = cp.argpartition(-similarities, top_k)[:top_k]
        top_k_indices_sorted = top_k_indices[cp.argsort(-similarities[top_k_indices])]
        
        # Move results to CPU
        top_k_indices_cpu = cp.asnumpy(top_k_indices_sorted)
        top_k_similarities_cpu = cp.asnumpy(similarities[top_k_indices_sorted])
        
        # Build result array
        result = [(ids[int(idx)], float(sim)) for idx, sim in zip(top_k_indices_cpu, top_k_similarities_cpu)]
        
        return result
        
    except ImportError:
        # Fallback to NumPy CPU implementation
        import numpy as np
        
        target = np.array(target_embedding, dtype=np.float32)
        target_norm = np.linalg.norm(target)
        if target_norm > 0:
            target = target / target_norm
        
        ids = [emb[0] for emb in embeddings]
        embedding_matrix = np.array([emb[1] for emb in embeddings], dtype=np.float32)
        
        norms = np.linalg.norm(embedding_matrix, axis=1, keepdims=True)
        norms = np.where(norms > 0, norms, 1.0)
        embedding_matrix = embedding_matrix / norms
        
        similarities = np.dot(embedding_matrix, target)
        
        if top_k >= len(similarities):
            top_k = len(similarities)
        
        top_k_indices = np.argpartition(-similarities, top_k)[:top_k]
        top_k_indices_sorted = top_k_indices[np.argsort(-similarities[top_k_indices])]
        
        result = [(ids[int(idx)], float(similarities[int(idx)])) for idx in top_k_indices_sorted]
        
        return result
    
    except Exception as e:
        import plpy
        plpy.warning(f"Error in similarity_cosine_gpu: {str(e)}")
        return []
