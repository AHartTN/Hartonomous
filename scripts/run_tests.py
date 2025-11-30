"""Enterprise test runner for Hartonomous.

Provides multiple test execution modes for CI/CD and local development.
"""

import argparse
import subprocess
import sys


def run_smoke_tests(verbose: bool = False) -> int:
    """Run smoke tests only (< 1 min).
    
    Quick validation for imports, connections, and basic functionality.
    """
    cmd = [sys.executable, "-m", "pytest", "tests/smoke", "-m", "smoke"]
    if verbose:
        cmd.append("-v")
    result = subprocess.run(cmd, check=False)
    return result.returncode


def run_unit_tests(verbose: bool = False, coverage: bool = False) -> int:
    """Run unit tests (< 5 min).
    
    Fast isolated tests with optional coverage reporting.
    """
    cmd = [sys.executable, "-m", "pytest", "tests/unit", "-m", "unit", "-n", "auto"]
    if verbose:
        cmd.append("-v")
    if coverage:
        cmd.extend(["--cov=api", "--cov=src", "--cov-report=html", "--cov-report=term-missing"])
    result = subprocess.run(cmd, check=False)
    return result.returncode


def run_integration_tests(verbose: bool = False) -> int:
    """Run integration tests (< 15 min).
    
    Database and service integration tests.
    """
    cmd = [sys.executable, "-m", "pytest", "tests/integration", "-m", "integration"]
    if verbose:
        cmd.append("-v")
    result = subprocess.run(cmd, check=False)
    return result.returncode


def run_functional_tests(verbose: bool = False) -> int:
    """Run functional tests (< 30 min).
    
    End-to-end functional tests including compression, Hilbert curves, positioning.
    """
    cmd = [sys.executable, "-m", "pytest", "tests/functional", "-m", "functional"]
    if verbose:
        cmd.append("-v")
    result = subprocess.run(cmd, check=False)
    return result.returncode


def run_sql_tests(verbose: bool = False) -> int:
    """Run SQL tests.
    
    PostgreSQL function and schema tests.
    """
    cmd = [sys.executable, "-m", "pytest", "tests/sql", "-m", "sql"]
    if verbose:
        cmd.append("-v")
    result = subprocess.run(cmd, check=False)
    return result.returncode


def run_performance_tests(verbose: bool = False) -> int:
    """Run performance tests (variable duration).
    
    Load tests and performance benchmarks.
    """
    cmd = [sys.executable, "-m", "pytest", "tests/performance", "-v"]
    if verbose:
        cmd.append("-vv")
    result = subprocess.run(cmd, check=False)
    return result.returncode


def run_ci_tests(verbose: bool = False) -> int:
    """Run CI/CD suitable tests (smoke + unit).
    
    Fast tests suitable for continuous integration (< 6 min total).
    """
    print("Running CI/CD test suite (smoke + unit)...")
    print("\n[1/2] Smoke Tests...")
    smoke_result = run_smoke_tests(verbose)
    if smoke_result != 0:
        print("❌ Smoke tests failed!")
        return smoke_result
    
    print("\n[2/2] Unit Tests...")
    unit_result = run_unit_tests(verbose, coverage=True)
    if unit_result != 0:
        print("❌ Unit tests failed!")
        return unit_result
    
    print("\n✅ All CI/CD tests passed!")
    return 0


def run_all_tests(verbose: bool = False, coverage: bool = False) -> int:
    """Run all tests with coverage.
    
    Complete test suite including smoke, unit, integration, functional, and SQL tests.
    """
    cmd = [sys.executable, "-m", "pytest", "tests/"]
    if verbose:
        cmd.append("-v")
    else:
        cmd.append("-v")  # Keep verbose for full runs
    
    if coverage:
        cmd.extend([
            "--cov=api",
            "--cov=src",
            "--cov-report=html",
            "--cov-report=term-missing",
            "--tb=short"
        ])
    else:
        cmd.append("--tb=short")
    
    result = subprocess.run(cmd, check=False)
    return result.returncode


def run_tests():
    """Legacy function for backward compatibility."""
    return run_all_tests(verbose=True, coverage=True)


def main():
    """Run tests based on command-line arguments."""
    parser = argparse.ArgumentParser(
        description="Hartonomous Enterprise Test Runner",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Test Modes:
  smoke         Quick validation tests (< 1 min)
  unit          Fast isolated tests (< 5 min)
  integration   Database and service tests (< 15 min)
  functional    End-to-end tests (< 30 min)
  sql           PostgreSQL function tests
  performance   Load and performance tests (variable)
  ci            CI/CD suitable tests (smoke + unit)
  all           Complete test suite (default)

Examples:
  python run_tests.py smoke                    # Quick smoke tests
  python run_tests.py ci -v                    # CI/CD tests with verbose output
  python run_tests.py all --coverage           # Full suite with coverage
  python run_tests.py unit --coverage -v       # Unit tests with coverage and verbose
        """
    )
    
    parser.add_argument(
        "mode",
        nargs="?",
        default="all",
        choices=["smoke", "unit", "integration", "functional", "sql", "performance", "ci", "all"],
        help="Test execution mode (default: all)"
    )
    parser.add_argument(
        "-v", "--verbose",
        action="store_true",
        help="Verbose output"
    )
    parser.add_argument(
        "--coverage",
        action="store_true",
        help="Generate coverage report (applies to unit and all modes)"
    )
    
    args = parser.parse_args()
    
    # Map modes to functions
    mode_map = {
        "smoke": lambda: run_smoke_tests(args.verbose),
        "unit": lambda: run_unit_tests(args.verbose, args.coverage),
        "integration": lambda: run_integration_tests(args.verbose),
        "functional": lambda: run_functional_tests(args.verbose),
        "sql": lambda: run_sql_tests(args.verbose),
        "performance": lambda: run_performance_tests(args.verbose),
        "ci": lambda: run_ci_tests(args.verbose),
        "all": lambda: run_all_tests(args.verbose, args.coverage),
    }
    
    print(f"🧪 Running {args.mode} tests...")
    return mode_map[args.mode]()


if __name__ == "__main__":
    sys.exit(main())

