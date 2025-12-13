"""
Query result caching layer for Hartonomous connector.
Implements LRU cache with spatial invalidation.
"""

import time
import hashlib
from typing import Dict, List, Tuple, Optional, Any
from collections import OrderedDict
from dataclasses import dataclass

@dataclass
class CacheEntry:
    """Single cache entry with metadata."""
    key: str
    result: List[Tuple]
    timestamp: float
    hit_count: int = 0
    spatial_bbox: Optional[Tuple[float, float, float, float]] = None

class SpatialQueryCache:
    """
    LRU cache for spatial query results with intelligent invalidation.
    
    Features:
    - Size-based eviction (LRU policy)
    - Time-based expiration
    - Spatial invalidation (when atoms change in query region)
    - Hit rate tracking
    """
    
    def __init__(self, max_size: int = 1000, ttl_seconds: int = 300):
        """
        Initialize cache.
        
        Args:
            max_size: Maximum number of cached queries
            ttl_seconds: Time-to-live for cache entries
        """
        self.max_size = max_size
        self.ttl_seconds = ttl_seconds
        self._cache: OrderedDict[str, CacheEntry] = OrderedDict()
        self._hits = 0
        self._misses = 0
    
    def _make_key(self, query: str, params: Tuple = ()) -> str:
        """Generate cache key from query and parameters."""
        combined = f"{query}:{params}"
        return hashlib.sha256(combined.encode()).hexdigest()[:16]
    
    def get(self, query: str, params: Tuple = ()) -> Optional[List[Tuple]]:
        """
        Retrieve cached query result.
        
        Args:
            query: SQL query string
            params: Query parameters tuple
            
        Returns:
            Cached result list or None if not found/expired
        """
        key = self._make_key(query, params)
        
        if key not in self._cache:
            self._misses += 1
            return None
        
        entry = self._cache[key]
        
        # Check expiration
        if time.time() - entry.timestamp > self.ttl_seconds:
            del self._cache[key]
            self._misses += 1
            return None
        
        # Move to end (LRU)
        self._cache.move_to_end(key)
        entry.hit_count += 1
        self._hits += 1
        
        return entry.result
    
    def set(self, query: str, params: Tuple, result: List[Tuple], 
            spatial_bbox: Optional[Tuple[float, float, float, float]] = None):
        """
        Store query result in cache.
        
        Args:
            query: SQL query string
            params: Query parameters tuple
            result: Query result list
            spatial_bbox: Optional bounding box (xmin, ymin, xmax, ymax)
        """
        key = self._make_key(query, params)
        
        # Evict oldest if at capacity
        if len(self._cache) >= self.max_size and key not in self._cache:
            self._cache.popitem(last=False)
        
        entry = CacheEntry(
            key=key,
            result=result,
            timestamp=time.time(),
            spatial_bbox=spatial_bbox
        )
        
        self._cache[key] = entry
        self._cache.move_to_end(key)
    
    def invalidate_spatial(self, bbox: Tuple[float, float, float, float]):
        """
        Invalidate cache entries that intersect with spatial region.
        
        Args:
            bbox: Bounding box (xmin, ymin, xmax, ymax) that changed
        """
        xmin, ymin, xmax, ymax = bbox
        
        to_remove = []
        for key, entry in self._cache.items():
            if entry.spatial_bbox is None:
                continue
            
            ex_min, ey_min, ex_max, ey_max = entry.spatial_bbox
            
            # Check bbox intersection
            if not (xmax < ex_min or xmin > ex_max or 
                   ymax < ey_min or ymin > ey_max):
                to_remove.append(key)
        
        for key in to_remove:
            del self._cache[key]
    
    def invalidate_all(self):
        """Clear entire cache."""
        self._cache.clear()
    
    def get_stats(self) -> Dict[str, Any]:
        """
        Get cache performance statistics.
        
        Returns:
            Dictionary with hit rate, size, etc.
        """
        total = self._hits + self._misses
        hit_rate = (self._hits / total * 100) if total > 0 else 0
        
        return {
            "size": len(self._cache),
            "max_size": self.max_size,
            "hits": self._hits,
            "misses": self._misses,
            "hit_rate": hit_rate,
            "avg_hit_count": sum(e.hit_count for e in self._cache.values()) / len(self._cache) if self._cache else 0
        }

class CachedConnector:
    """
    Wrapper for Hartonomous connector with integrated caching.
    """
    
    def __init__(self, connector, cache: Optional[SpatialQueryCache] = None):
        """
        Initialize cached connector.
        
        Args:
            connector: Underlying Hartonomous connector instance
            cache: Optional cache instance (creates default if None)
        """
        self.connector = connector
        self.cache = cache or SpatialQueryCache()
    
    def query(self, atom_hash: str, k: int = 10, z_level: Optional[int] = None) -> List[Tuple]:
        """k-NN query with caching."""
        cache_key = f"knn:{atom_hash}:{k}:{z_level}"
        
        cached = self.cache.get(cache_key, ())
        if cached is not None:
            return cached
        
        result = self.connector.query(atom_hash, k, z_level)
        self.cache.set(cache_key, (), result)
        
        return result
    
    def search(self, center_hash: str, radius: float, z_level: Optional[int] = None) -> List[Tuple]:
        """Radius search with spatial caching."""
        cache_key = f"radius:{center_hash}:{radius}:{z_level}"
        
        cached = self.cache.get(cache_key, ())
        if cached is not None:
            return cached
        
        result = self.connector.search(center_hash, radius, z_level)
        
        # Compute bounding box for spatial invalidation
        # TODO: Extract actual bbox from center atom coordinates
        self.cache.set(cache_key, (), result, spatial_bbox=None)
        
        return result
    
    def invalidate_atom(self, atom_hash: str):
        """Invalidate cache entries related to specific atom."""
        # Simple implementation: invalidate all (could be more targeted)
        self.cache.invalidate_all()
    
    def get_cache_stats(self) -> Dict[str, Any]:
        """Get cache performance metrics."""
        return self.cache.get_stats()
