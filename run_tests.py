"""Run all tests with coverage."""

import subprocess
import sys


def run_tests():
    """Run pytest with coverage."""
    cmd = [
        sys.executable,
        "-m",
        "pytest",
        "tests/",
        "-v",
        "--cov=src",
        "--cov=api",
        "--cov-report=html",
        "--cov-report=term-missing",
        "--tb=short",
    ]

    result = subprocess.run(cmd)
    return result.returncode


if __name__ == "__main__":
    sys.exit(run_tests())
