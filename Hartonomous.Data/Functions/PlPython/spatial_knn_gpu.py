"""
GPU-accelerated k-nearest neighbors spatial query using CuPy.
This function performs efficient k-NN search on 3D spatial coordinates using GPU parallelization.
"""

def spatial_knn_gpu(target_x, target_y, target_z, k, coordinates):
    """
    Find k-nearest neighbors to target point using GPU-accelerated computation.
    
    Args:
        target_x: X coordinate of target point
        target_y: Y coordinate of target point
        target_z: Z coordinate of target point
        k: Number of nearest neighbors to find
        coordinates: Array of tuples [(id, x, y, z), ...]
    
    Returns:
        Array of tuples [(id, distance), ...] for k nearest neighbors, sorted by distance
    """
    try:
        import cupy as cp
        import numpy as np
        
        # Convert target to CuPy array
        target = cp.array([target_x, target_y, target_z], dtype=cp.float64)
        
        # Extract IDs and coordinates
        ids = [coord[0] for coord in coordinates]
        points = cp.array([[coord[1], coord[2], coord[3]] for coord in coordinates], dtype=cp.float64)
        
        # Compute Euclidean distances on GPU
        # Using broadcasting: ||a - b||^2 = sum((a - b)^2)
        diff = points - target
        distances_squared = cp.sum(diff * diff, axis=1)
        distances = cp.sqrt(distances_squared)
        
        # Find k smallest distances
        if k >= len(distances):
            k = len(distances)
        
        # Use argpartition for O(n) average complexity instead of full sort
        k_indices = cp.argpartition(distances, k)[:k]
        k_indices_sorted = k_indices[cp.argsort(distances[k_indices])]
        
        # Move results back to CPU
        k_indices_cpu = cp.asnumpy(k_indices_sorted)
        k_distances_cpu = cp.asnumpy(distances[k_indices_sorted])
        
        # Build result array
        result = [(ids[int(idx)], float(dist)) for idx, dist in zip(k_indices_cpu, k_distances_cpu)]
        
        return result
        
    except ImportError:
        # Fallback to NumPy CPU implementation if CuPy not available
        import numpy as np
        
        target = np.array([target_x, target_y, target_z], dtype=np.float64)
        ids = [coord[0] for coord in coordinates]
        points = np.array([[coord[1], coord[2], coord[3]] for coord in coordinates], dtype=np.float64)
        
        diff = points - target
        distances = np.sqrt(np.sum(diff * diff, axis=1))
        
        if k >= len(distances):
            k = len(distances)
        
        k_indices = np.argpartition(distances, k)[:k]
        k_indices_sorted = k_indices[np.argsort(distances[k_indices])]
        
        result = [(ids[int(idx)], float(distances[int(idx)])) for idx in k_indices_sorted]
        
        return result
    
    except Exception as e:
        # Log error and return empty result
        import plpy
        plpy.warning(f"Error in spatial_knn_gpu: {str(e)}")
        return []
