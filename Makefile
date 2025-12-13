.PHONY: all clean test setup

all: setup build test

setup:
	@echo "=== Setting up Hartonomous ==="
	pwsh -File scripts/setup_database.ps1

build-shader:
	@echo "=== Building Shader ==="
	cd shader && cargo build --release

build-cortex:
	@echo "=== Building Cortex ==="
	cd cortex && make && sudo make install

build: build-shader build-cortex

test-shader:
	cd shader && cargo test

test-python:
	python -m unittest discover -s tests -p "test_*.py"

test: test-shader test-python

integration:
	pwsh -File scripts/run_integration_test.ps1

clean:
	cd shader && cargo clean
	cd cortex && make clean
	find . -type d -name __pycache__ -exec rm -rf {} +
	find . -type f -name "*.pyc" -delete

format:
	cd shader && cargo fmt
	black connector/ tests/ examples/

lint:
	cd shader && cargo clippy
	pylint connector/ tests/

help:
	@echo "Hartonomous Build System"
	@echo ""
	@echo "Targets:"
	@echo "  all          - Full setup, build, and test"
	@echo "  setup        - Initialize database"
	@echo "  build        - Build Shader and Cortex"
	@echo "  test         - Run all tests"
	@echo "  integration  - End-to-end integration test"
	@echo "  clean        - Remove build artifacts"
	@echo "  format       - Format code"
	@echo "  lint         - Run linters"
