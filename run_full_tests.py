"""Comprehensive test runner with detailed reporting."""

import subprocess
import sys
import os
from pathlib import Path


def setup_test_env():
    """Setup test environment."""
    env = os.environ.copy()
    env["TESTING"] = "1"
    env["DATABASE_URL"] = (
        "postgresql://postgres:postgres@localhost:5432/hartonomous_test"
    )
    return env


def run_unit_tests():
    """Run unit tests (no DB required)."""
    print("\n" + "=" * 80)
    print("RUNNING UNIT TESTS")
    print("=" * 80 + "\n")

    cmd = [
        sys.executable,
        "-m",
        "pytest",
        "tests/unit/",
        "-v",
        "--tb=short",
        "--cov=src.core",
        "--cov-report=term-missing:skip-covered",
    ]

    result = subprocess.run(cmd, env=setup_test_env())
    return result.returncode


def run_sql_tests():
    """Run SQL function tests (requires DB)."""
    print("\n" + "=" * 80)
    print("RUNNING SQL FUNCTION TESTS")
    print("=" * 80 + "\n")

    cmd = [
        sys.executable,
        "-m",
        "pytest",
        "tests/sql/",
        "-v",
        "--tb=short",
    ]

    result = subprocess.run(cmd, env=setup_test_env())
    return result.returncode


def run_integration_tests():
    """Run integration tests (requires DB + dependencies)."""
    print("\n" + "=" * 80)
    print("RUNNING INTEGRATION TESTS")
    print("=" * 80 + "\n")

    cmd = [
        sys.executable,
        "-m",
        "pytest",
        "tests/integration/",
        "-v",
        "--tb=short",
        "--cov=api.services",
        "--cov=src.ingestion",
        "--cov-append",
    ]

    result = subprocess.run(cmd, env=setup_test_env())
    return result.returncode


def run_coverage_report():
    """Generate coverage report."""
    print("\n" + "=" * 80)
    print("GENERATING COVERAGE REPORT")
    print("=" * 80 + "\n")

    cmd = [sys.executable, "-m", "coverage", "html"]

    subprocess.run(cmd)

    cmd = [sys.executable, "-m", "coverage", "report", "--skip-covered"]

    subprocess.run(cmd)

    print("\nHTML report: htmlcov/index.html")


def main():
    """Run all tests."""
    print("Hartonomous Test Suite")
    print("=" * 80)

    results = {
        "unit": run_unit_tests(),
        "sql": run_sql_tests(),
        "integration": run_integration_tests(),
    }

    run_coverage_report()

    print("\n" + "=" * 80)
    print("TEST SUMMARY")
    print("=" * 80)

    for test_type, code in results.items():
        status = "? PASS" if code == 0 else "? FAIL"
        print(f"{test_type.upper():15s}: {status}")

    failed = sum(1 for code in results.values() if code != 0)

    if failed > 0:
        print(f"\n? {failed} test suite(s) failed")
        return 1
    else:
        print(f"\n? All test suites passed!")
        return 0


if __name__ == "__main__":
    sys.exit(main())
