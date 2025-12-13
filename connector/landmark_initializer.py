"""
Landmark initialization: MaxMin selection for LMDS

Landmarks are a subset of atoms used for LMDS projection.
They must be selected ONCE during initial setup, not as background process.

MaxMin Algorithm:
1. Select first landmark randomly
2. Iteratively select atom with maximum minimum distance to existing landmarks
3. Result: k landmarks spanning the semantic space
"""

import psycopg2
from typing import Dict


class LandmarkInitializer:
    def __init__(self, conn):
        self.conn = conn
    
    def initialize_landmarks(self, k: int = 100) -> Dict[str, int]:
        """
        Initialize landmarks using MaxMin selection
        
        Args:
            k: Number of landmarks to select (default 100)
        
        Returns: Statistics
        """
        cursor = self.conn.cursor()
        
        # Clear existing landmarks
        cursor.execute("DELETE FROM cortex_landmarks")
        self.conn.commit()
        
        print(f"Initializing {k} landmarks using MaxMin selection...")
        
        # Step 1: Select first landmark randomly from Constants
        cursor.execute("""
            INSERT INTO cortex_landmarks (atom_id, landmark_index)
            SELECT atom_id, 0
            FROM atom
            WHERE atom_class = 0  -- Constants only
            ORDER BY random()
            LIMIT 1
            RETURNING atom_id
        """)
        
        first_landmark = cursor.fetchone()[0]
        self.conn.commit()
        print(f"  Landmark 0: {first_landmark.hex()[:16]}...")
        
        # Step 2: Iteratively select maximally distant landmarks
        for i in range(1, k):
            cursor.execute("""
                WITH candidate_distances AS (
                    SELECT
                        a.atom_id,
                        MIN(ST_Distance(a.geom, l.geom)) as min_dist_to_landmarks
                    FROM atom a
                    CROSS JOIN cortex_landmarks cl
                    JOIN atom l ON l.atom_id = cl.atom_id
                    WHERE a.atom_class = 0  -- Constants only
                      AND NOT EXISTS (
                          SELECT 1 FROM cortex_landmarks cl2
                          WHERE cl2.atom_id = a.atom_id
                      )
                    GROUP BY a.atom_id
                )
                INSERT INTO cortex_landmarks (atom_id, landmark_index)
                SELECT atom_id, %s
                FROM candidate_distances
                ORDER BY min_dist_to_landmarks DESC
                LIMIT 1
                RETURNING atom_id
            """, (i,))
            
            landmark_id = cursor.fetchone()[0]
            self.conn.commit()
            
            if i % 10 == 0:
                print(f"  Landmark {i}: {landmark_id.hex()[:16]}...")
        
        # Verify landmark count
        cursor.execute("SELECT COUNT(*) FROM cortex_landmarks")
        count = cursor.fetchone()[0]
        
        # Get landmark distribution by subtype
        cursor.execute("""
            SELECT a.subtype, COUNT(*) as count
            FROM cortex_landmarks cl
            JOIN atom a ON a.atom_id = cl.atom_id
            GROUP BY a.subtype
            ORDER BY count DESC
        """)
        
        distribution = cursor.fetchall()
        
        cursor.close()
        
        print(f"\nLandmark selection complete:")
        print(f"  Total landmarks: {count}")
        print(f"  Distribution by subtype:")
        for subtype, cnt in distribution:
            print(f"    {subtype}: {cnt}")
        
        return {
            'landmarks_selected': count,
            'distribution': {subtype: cnt for subtype, cnt in distribution}
        }


if __name__ == "__main__":
    # Test landmark initialization
    import os
    
    conn = psycopg2.connect(
        host=os.getenv('POSTGRES_HOST', '127.0.0.1'),
        port=int(os.getenv('POSTGRES_PORT', 5432)),
        user=os.getenv('POSTGRES_USER', 'postgres'),
        password=os.getenv('POSTGRES_PASSWORD', 'postgres'),
        database=os.getenv('POSTGRES_DB', 'hartonomous')
    )
    
    initializer = LandmarkInitializer(conn)
    stats = initializer.initialize_landmarks(k=100)
    
    conn.close()
    
    print(f"\n✓ Landmarks initialized: {stats['landmarks_selected']}")
