"""Test Model parser."""

import pytest

from src.ingestion.parsers.model_parser import ModelParser

pytestmark = [pytest.mark.asyncio, pytest.mark.integration]


class TestModelParser:
    """Test model parsing and routing."""

    async def test_detect_gguf_format(self):
        """Test GGUF format detection."""
        parser = ModelParser()

        format_type = parser._detect_format(".gguf")
        assert format_type == "gguf"

    async def test_detect_safetensors_format(self):
        """Test SafeTensors format detection."""
        parser = ModelParser()

        format_type = parser._detect_format(".safetensors")
        assert format_type == "safetensors"

    async def test_detect_pytorch_format(self):
        """Test PyTorch format detection."""
        parser = ModelParser()

        assert parser._detect_format(".pt") == "pytorch"
        assert parser._detect_format(".pth") == "pt"

    async def test_detect_onnx_format(self):
        """Test ONNX format detection."""
        parser = ModelParser()

        format_type = parser._detect_format(".onnx")
        assert format_type == "onnx"
