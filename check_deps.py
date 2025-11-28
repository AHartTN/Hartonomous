"""Check parser dependencies."""
import importlib

deps = {
    'PIL': 'Pillow (pip install pillow)',
    'cv2': 'opencv-python (pip install opencv-python)', 
    'soundfile': 'soundfile (pip install soundfile)',
    'librosa': 'librosa (pip install librosa)',
    'safetensors': 'safetensors (pip install safetensors)',
    'torch': 'pytorch (pip install torch)',
    'onnx': 'onnx (pip install onnx)',
    'httpx': 'httpx (pip install httpx)',
}

print("Checking parser dependencies:\n")
for module, install in deps.items():
    try:
        importlib.import_module(module)
        print(f"? {module}: OK")
    except ImportError:
        print(f"? {module}: MISSING - {install}")
