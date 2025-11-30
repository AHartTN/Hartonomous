#!/usr/bin/env python3
"""Validate test suite structure and discovery.

This script validates that:
1. All tests are properly discovered by pytest
2. Test markers are correctly applied
3. No orphaned test files exist outside the test structure
4. All test categories have expected test files
"""

import subprocess
import sys
from pathlib import Path


def run_command(cmd: list[str], description: str) -> tuple[int, str]:
    """Run a shell command and return exit code and output."""
    print(f"\n{'='*80}")
    print(f"Running: {description}")
    print(f"Command: {' '.join(cmd)}")
    print(f"{'='*80}")
    
    result = subprocess.run(cmd, capture_output=True, text=True)
    output = result.stdout + result.stderr
    print(output)
    return result.returncode, output


def validate_test_discovery():
    """Validate pytest can discover all tests."""
    code, output = run_command(
        [sys.executable, "-m", "pytest", "--collect-only", "tests/", "-q"],
        "Test Discovery Validation"
    )
    
    if code != 0:
        print("❌ FAILED: Test discovery has errors")
        return False
    
    # Count collected tests
    if "collected" in output:
        for line in output.split('\n'):
            if 'collected' in line:
                print(f"✅ {line}")
                return True
    
    print("❌ FAILED: Could not determine collected test count")
    return False


def validate_marker_groups():
    """Validate that marker groups work correctly."""
    markers = {
        "smoke": "Quick validation tests",
        "unit": "Fast isolated tests",
        "integration": "Database/service tests",
        "functional": "End-to-end tests",
        "sql": "PostgreSQL function tests"
    }
    
    all_passed = True
    for marker, description in markers.items():
        code, output = run_command(
            [sys.executable, "-m", "pytest", "--collect-only", "-m", marker, "-q"],
            f"Marker Group: {marker} - {description}"
        )
        
        if code != 0 and "no tests ran" not in output.lower():
            print(f"❌ FAILED: Marker '{marker}' has collection errors")
            all_passed = False
        elif "collected" in output or "deselected" in output:
            print(f"✅ Marker '{marker}' validated")
        else:
            print(f"⚠️  WARNING: No tests found for marker '{marker}'")
    
    return all_passed


def check_orphaned_tests():
    """Check for test files outside the tests/ directory."""
    root = Path.cwd()
    
    orphaned = []
    
    # Check root directory
    for test_file in root.glob("test_*.py"):
        orphaned.append(test_file)
    
    # Check scripts directory
    scripts_dir = root / "scripts"
    if scripts_dir.exists():
        for test_file in scripts_dir.glob("test_*.py"):
            orphaned.append(test_file)
    
    # Check api/tests directory (should be removed after consolidation)
    api_tests = root / "api" / "tests"
    if api_tests.exists():
        print(f"⚠️  WARNING: api/tests/ directory still exists (should be removed)")
        orphaned.append(api_tests)
    
    if orphaned:
        print("\n❌ FAILED: Found orphaned test files:")
        for path in orphaned:
            print(f"  - {path}")
        return False
    
    print("\n✅ No orphaned test files found")
    return True


def validate_test_structure():
    """Validate expected test directory structure."""
    tests_dir = Path("tests")
    
    expected_dirs = {
        "smoke": "Quick validation tests",
        "unit": "Fast isolated tests",
        "integration": "Database/service tests",
        "functional": "End-to-end tests",
        "performance": "Load and performance tests",
        "sql": "PostgreSQL function tests"
    }
    
    print("\n" + "="*80)
    print("Test Structure Validation")
    print("="*80)
    
    all_exist = True
    for dir_name, description in expected_dirs.items():
        dir_path = tests_dir / dir_name
        if dir_path.exists():
            test_count = len(list(dir_path.glob("test_*.py")))
            print(f"✅ {dir_name}/ ({test_count} test files) - {description}")
        else:
            print(f"❌ {dir_name}/ MISSING - {description}")
            all_exist = False
    
    return all_exist


def main():
    """Run all validation checks."""
    print("="*80)
    print("Hartonomous Test Suite Validation")
    print("="*80)
    
    checks = [
        ("Test Structure", validate_test_structure),
        ("Test Discovery", validate_test_discovery),
        ("Marker Groups", validate_marker_groups),
        ("Orphaned Files", check_orphaned_tests),
    ]
    
    results = {}
    for check_name, check_func in checks:
        print(f"\n\n{'#'*80}")
        print(f"# {check_name}")
        print(f"{'#'*80}")
        results[check_name] = check_func()
    
    # Print summary
    print("\n\n" + "="*80)
    print("VALIDATION SUMMARY")
    print("="*80)
    
    for check_name, passed in results.items():
        status = "✅ PASSED" if passed else "❌ FAILED"
        print(f"{status}: {check_name}")
    
    all_passed = all(results.values())
    
    print("="*80)
    if all_passed:
        print("✅ ALL VALIDATIONS PASSED")
        return 0
    else:
        print("❌ SOME VALIDATIONS FAILED")
        return 1


if __name__ == "__main__":
    sys.exit(main())
