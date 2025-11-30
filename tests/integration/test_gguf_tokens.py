"""Test script to understand GGUF token parsing."""

from pathlib import Path

import gguf
import pytest

pytestmark = [pytest.mark.integration, pytest.mark.gguf]

model_path = Path(
    r"D:\Models\blobs\sha256-1194192cf2a187eb02722edcc3f77b11d21f537048ce04b67ccf8ba78863006a"
)
reader = gguf.GGUFReader(model_path)

# Check the token field
token_field = reader.fields.get("tokenizer.ggml.tokens")
if token_field:
    print(f"Token field type: {type(token_field)}")
    print(f"Token field data type: {type(token_field.data)}")
    print(f"Token field data length: {len(token_field.data)}")
    print(f"Token field types: {token_field.types}")

    # Official gguf reader example uses: field.parts[field.data[0]]
    # Let's test this approach
    print(f"\n=== Testing Official Approach ===")
    print(f"field.data[0] = {token_field.data[0]}")
    print(
        f"field.parts[field.data[0]] type: {type(token_field.parts[token_field.data[0]])}"
    )

    # Try accessing first few tokens using data as indices
    print(f"\nFirst 10 tokens using field.parts[field.data[i]]:")
    for i in range(min(10, len(token_field.data))):
        idx = token_field.data[i]
        token_data = token_field.parts[idx]
        print(
            f"  [{i}] data[{i}]={idx}, parts[{idx}] type={type(token_data)}, shape={getattr(token_data, 'shape', 'N/A')}"
        )

        # Try to decode if it's bytes
        if hasattr(token_data, "__iter__"):
            try:
                token_str = bytes(token_data).decode("utf-8", errors="replace")
                print(f"       decoded: {repr(token_str[:50])}")
            except:
                print(f"       raw: {token_data}")
