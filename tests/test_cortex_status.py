"""
Test Cortex background worker - verify LMDS refinement is working
"""

import psycopg2
import time

conn = psycopg2.connect(
    host='localhost',
    dbname='hartonomous',
    user='hartonomous'
)

cursor = conn.cursor()

print("=== Cortex Background Worker Status ===\n")

# Check Cortex state
cursor.execute("SELECT * FROM cortex_state LIMIT 1")
state = cursor.fetchone()

if state:
    print(f"Cortex operational:")
    print(f"  Atoms processed: {state[0]:,}")
    print(f"  Last recalibration: {state[1]}")
    print(f"  Recalibrations: {state[2]:,}")
    print(f"  Background worker: {state[3]}")
else:
    print("Cortex not initialized")

# Check if background worker is running
cursor.execute("""
    SELECT pid, application_name, state, state_change 
    FROM pg_stat_activity 
    WHERE application_name LIKE '%cortex%'
""")

workers = cursor.fetchall()

if workers:
    print(f"\nBackground workers found:")
    for pid, app_name, state, state_change in workers:
        print(f"  PID {pid}: {app_name} ({state}) - last change {state_change}")
else:
    print(f"\n⚠ No Cortex background workers running")
    print(f"  Expected: cortex_background_worker")

# Test manual recalibration
print(f"\n=== Manual Recalibration Test ===")

# Get atom count before
cursor.execute("SELECT COUNT(*) FROM atom WHERE atom_class = 0")
atom_count = cursor.fetchone()[0]

print(f"Database has {atom_count:,} constant atoms")

# Sample atom positions before recalibration
cursor.execute("""
    SELECT atom_id, ST_X(geom), ST_Y(geom), ST_Z(geom)
    FROM atom
    WHERE atom_class = 0
    ORDER BY random()
    LIMIT 5
""")

before_positions = cursor.fetchall()

print(f"\nSample positions BEFORE recalibration:")
for atom_id, x, y, z in before_positions:
    print(f"  {atom_id.hex()[:16]}: ({x:.4f}, {y:.4f}, Z={z})")

# Trigger manual recalibration
print(f"\nTriggering manual cortex cycle...")
start = time.time()

try:
    cursor.execute("SELECT cortex_cycle_once()")
    result = cursor.fetchone()
    elapsed = time.time() - start
    
    print(f"Recalibration completed in {elapsed:.2f}s")
    
    if result:
        print(f"  Result: {result[0]}")
    
except Exception as e:
    print(f"⚠ Recalibration failed: {e}")
    elapsed = time.time() - start

# Sample atom positions after recalibration
cursor.execute("""
    SELECT atom_id, ST_X(geom), ST_Y(geom), ST_Z(geom)
    FROM atom
    WHERE atom_id = ANY(%s)
""", ([aid for aid, _, _, _ in before_positions],))

after_positions = {row[0]: (row[1], row[2], row[3]) for row in cursor.fetchall()}

print(f"\nSample positions AFTER recalibration:")
max_movement = 0.0
for atom_id, x_before, y_before, z_before in before_positions:
    if atom_id in after_positions:
        x_after, y_after, z_after = after_positions[atom_id]
        distance = ((x_after - x_before)**2 + (y_after - y_before)**2)**0.5
        max_movement = max(max_movement, distance)
        
        print(f"  {atom_id.hex()[:16]}: ({x_after:.4f}, {y_after:.4f}, Z={z_after})")
        if distance > 0.001:
            print(f"    → Moved {distance:.4f} units")

if max_movement < 0.001:
    print(f"\n⚠ Atoms did not move during recalibration")
    print(f"  This suggests LMDS is not implemented or positions are already optimal")
else:
    print(f"\n✓ Atoms repositioned - max movement: {max_movement:.4f} units")

# Check updated Cortex state
cursor.execute("SELECT * FROM cortex_state LIMIT 1")
state_after = cursor.fetchone()

if state_after and state:
    recal_delta = state_after[2] - state[2]
    if recal_delta > 0:
        print(f"\n✓ Cortex state updated:")
        print(f"  Recalibrations: {state[2]:,} → {state_after[2]:,} (+{recal_delta})")
    else:
        print(f"\n⚠ Cortex state not updated - recalibration may not be working")

conn.close()

print(f"\n=== Cortex Test Complete ===")
