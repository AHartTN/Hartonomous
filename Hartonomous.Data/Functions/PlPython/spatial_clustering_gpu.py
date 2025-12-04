"""
GPU-accelerated spatial clustering using DBSCAN algorithm with CuPy.
Identifies dense regions in 3D spatial data for landmark detection.
"""

def spatial_clustering_gpu(coordinates, eps, min_samples):
    """
    Perform DBSCAN clustering on 3D spatial coordinates using GPU acceleration.
    
    Args:
        coordinates: Array of tuples [(id, x, y, z), ...]
        eps: Maximum distance between two samples for one to be considered as in the neighborhood of the other
        min_samples: Minimum number of samples in a neighborhood for a point to be considered as a core point
    
    Returns:
        Array of tuples [(id, cluster_id), ...] where cluster_id is -1 for noise points
    """
    try:
        import cupy as cp
        import numpy as np
        from cuml.cluster import DBSCAN
        
        # Extract data
        ids = [coord[0] for coord in coordinates]
        points = np.array([[coord[1], coord[2], coord[3]] for coord in coordinates], dtype=np.float32)
        
        # Perform DBSCAN clustering on GPU
        dbscan = DBSCAN(eps=eps, min_samples=min_samples, metric='euclidean')
        cluster_labels = dbscan.fit_predict(points)
        
        # Move results to CPU
        if hasattr(cluster_labels, 'get'):
            cluster_labels = cluster_labels.get()
        
        # Build result array
        result = [(ids[i], int(label)) for i, label in enumerate(cluster_labels)]
        
        return result
        
    except ImportError:
        # Fallback to sklearn CPU implementation
        import numpy as np
        from sklearn.cluster import DBSCAN
        
        ids = [coord[0] for coord in coordinates]
        points = np.array([[coord[1], coord[2], coord[3]] for coord in coordinates], dtype=np.float32)
        
        dbscan = DBSCAN(eps=eps, min_samples=min_samples, metric='euclidean')
        cluster_labels = dbscan.fit_predict(points)
        
        result = [(ids[i], int(label)) for i, label in enumerate(cluster_labels)]
        
        return result
    
    except Exception as e:
        import plpy
        plpy.warning(f"Error in spatial_clustering_gpu: {str(e)}")
        return []
