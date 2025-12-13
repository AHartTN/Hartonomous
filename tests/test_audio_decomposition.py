"""
Test audio decomposition with actual WAV file
"""

import psycopg2
import numpy as np
import wave
import struct
from pathlib import Path

# Generate test WAV file
output_dir = Path("test_data")
output_dir.mkdir(exist_ok=True)

wav_path = output_dir / "test_tone.wav"

# Generate 1-second 440Hz sine wave
sample_rate = 44100
duration = 1.0
frequency = 440.0

t = np.linspace(0, duration, int(sample_rate * duration))
samples = np.sin(2 * np.pi * frequency * t)

# Convert to 16-bit PCM
samples_16bit = (samples * 32767).astype(np.int16)

# Write WAV
with wave.open(str(wav_path), 'wb') as wav:
    wav.setnchannels(1)
    wav.setsampwidth(2)
    wav.setframerate(sample_rate)
    wav.writeframes(samples_16bit.tobytes())

print(f"Generated test WAV: {wav_path}")
print(f"  Duration: {duration}s")
print(f"  Sample rate: {sample_rate}Hz")
print(f"  Frequency: {frequency}Hz")
print(f"  Samples: {len(samples_16bit)}")

# Test decomposition
import sys
sys.path.insert(0, str(Path(__file__).parent.parent))

from connector.audio_decomposer import AudioDecomposer

conn = psycopg2.connect(
    host='localhost',
    dbname='hartonomous',
    user='hartonomous'
)

decomposer = AudioDecomposer(conn)

print("\nDecomposing WAV file...")
comp_id = decomposer.decompose_wav(wav_path)

print(f"Composition ID: {comp_id.hex()[:16]}...")

# Check results
cursor = conn.cursor()

cursor.execute("""
    SELECT COUNT(*) FROM atom_compositions WHERE parent_atom_id = %s
""", (comp_id,))

component_count = cursor.fetchone()[0]
print(f"Components: {component_count}")

cursor.execute("""
    SELECT metadata FROM atom WHERE atom_id = %s
""", (comp_id,))

metadata = cursor.fetchone()[0]
print(f"Metadata: {metadata}")

# Test reconstruction
print("\nReconstructing audio...")
reconstructed_path = output_dir / "reconstructed.wav"
decomposer.reconstruct_wav(comp_id, reconstructed_path)

print(f"Reconstructed to: {reconstructed_path}")

# Verify file
with wave.open(str(reconstructed_path), 'rb') as wav:
    params = wav.getparams()
    print(f"  Channels: {params.nchannels}")
    print(f"  Sample rate: {params.framerate}")
    print(f"  Frames: {params.nframes}")

conn.close()
print("\n✓ Audio decomposition test complete")
