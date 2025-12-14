import struct

# Sample hex values from database
hex_vals = [
    '000000bc', '000000be', '0000013a', '000001be', '0000033c', 
    '0000033d', '0000043d', '000006bb', '000006bd', '0000073e',
    '00000c3e', '00001c3d', '000020bd', '000023bd', '00002d3d'
]

print("Hex Value    | Float Value      | Quantized (0.01) | Quantized (0.001)")
print("-" * 75)

for hex_val in hex_vals:
    # Decode float32 little-endian
    float_val = struct.unpack('<f', bytes.fromhex(hex_val))[0]
    quant_01 = round(float_val / 0.01) * 0.01
    quant_001 = round(float_val / 0.001) * 0.001
    
    print(f"{hex_val} | {float_val:16.10f} | {quant_01:16.10f} | {quant_001:16.10f}")
