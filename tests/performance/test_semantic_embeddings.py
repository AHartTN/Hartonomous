"""Test semantic embeddings for vocabulary tokens.

This script verifies that semantically similar tokens cluster together
in 3D space using the embedding service.
"""

import asyncio
import sys
from pathlib import Path

import pytest

# Add parent directory to path
sys.path.insert(0, str(Path(__file__).parent.parent))

import numpy as np

from api.services.embedding_service import generate_semantic_coordinates

pytestmark = [pytest.mark.performance, pytest.mark.spatial]


def calculate_distance(coord1, coord2):
    """Calculate Euclidean distance between two 3D coordinates."""
    return np.sqrt(sum((a - b) ** 2 for a, b in zip(coord1, coord2)))


async def test_semantic_embeddings():
    """Test that semantic embeddings create meaningful clusters."""

    print("\n" + "=" * 60)
    print("Semantic Embedding Test")
    print("=" * 60)

    # Test tokens: animals, vehicles, colors, actions
    test_tokens = [
        # Animals
        "cat",
        "dog",
        "lion",
        "tiger",
        "elephant",
        # Vehicles
        "car",
        "truck",
        "automobile",
        "vehicle",
        "bus",
        # Colors
        "red",
        "blue",
        "green",
        "yellow",
        "purple",
        # Actions
        "run",
        "jump",
        "walk",
        "sprint",
        "jog",
    ]

    print(f"\n📝 Test tokens ({len(test_tokens)}):")
    print(f"   Animals: cat, dog, lion, tiger, elephant")
    print(f"   Vehicles: car, truck, automobile, vehicle, bus")
    print(f"   Colors: red, blue, green, yellow, purple")
    print(f"   Actions: run, jump, walk, sprint, jog")

    # Generate embeddings
    print("\n🧠 Generating semantic embeddings...")
    coords = generate_semantic_coordinates(test_tokens, fit_pca=True)

    print(f"✓ Generated {len(coords)} 3D coordinates")

    # Create coordinate map
    token_coords = {token: coords[i] for i, token in enumerate(test_tokens)}

    # Test: Animals should cluster together
    print("\n📊 Testing semantic clustering...")
    print("\n1. Animal cluster (should be close):")
    cat_coord = token_coords["cat"]
    dog_coord = token_coords["dog"]
    lion_coord = token_coords["lion"]

    cat_dog_dist = calculate_distance(cat_coord, dog_coord)
    cat_lion_dist = calculate_distance(cat_coord, lion_coord)
    dog_lion_dist = calculate_distance(dog_coord, lion_coord)

    print(f"   cat ↔ dog: {cat_dog_dist:.4f}")
    print(f"   cat ↔ lion: {cat_lion_dist:.4f}")
    print(f"   dog ↔ lion: {dog_lion_dist:.4f}")
    avg_animal_dist = (cat_dog_dist + cat_lion_dist + dog_lion_dist) / 3
    print(f"   Average intra-animal distance: {avg_animal_dist:.4f}")

    # Test: Vehicles should cluster together
    print("\n2. Vehicle cluster (should be close):")
    car_coord = token_coords["car"]
    truck_coord = token_coords["truck"]
    auto_coord = token_coords["automobile"]

    car_truck_dist = calculate_distance(car_coord, truck_coord)
    car_auto_dist = calculate_distance(car_coord, auto_coord)
    truck_auto_dist = calculate_distance(truck_coord, auto_coord)

    print(f"   car ↔ truck: {car_truck_dist:.4f}")
    print(f"   car ↔ automobile: {car_auto_dist:.4f}")
    print(f"   truck ↔ automobile: {truck_auto_dist:.4f}")
    avg_vehicle_dist = (car_truck_dist + car_auto_dist + truck_auto_dist) / 3
    print(f"   Average intra-vehicle distance: {avg_vehicle_dist:.4f}")

    # Test: Animals vs Vehicles should be far apart
    print("\n3. Cross-category distances (should be large):")
    cat_car_dist = calculate_distance(cat_coord, car_coord)
    dog_truck_dist = calculate_distance(dog_coord, truck_coord)
    lion_auto_dist = calculate_distance(lion_coord, auto_coord)

    print(f"   cat ↔ car: {cat_car_dist:.4f}")
    print(f"   dog ↔ truck: {dog_truck_dist:.4f}")
    print(f"   lion ↔ automobile: {lion_auto_dist:.4f}")
    avg_cross_dist = (cat_car_dist + dog_truck_dist + lion_auto_dist) / 3
    print(f"   Average cross-category distance: {avg_cross_dist:.4f}")

    # Test: Action verbs should cluster
    print("\n4. Action cluster (should be close):")
    run_coord = token_coords["run"]
    jump_coord = token_coords["jump"]
    walk_coord = token_coords["walk"]

    run_jump_dist = calculate_distance(run_coord, jump_coord)
    run_walk_dist = calculate_distance(run_coord, walk_coord)

    print(f"   run ↔ jump: {run_jump_dist:.4f}")
    print(f"   run ↔ walk: {run_walk_dist:.4f}")
    avg_action_dist = (run_jump_dist + run_walk_dist) / 2
    print(f"   Average intra-action distance: {avg_action_dist:.4f}")

    # Summary
    print("\n" + "=" * 60)
    print("📈 Summary:")
    print(
        f"   Within-category avg: {np.mean([avg_animal_dist, avg_vehicle_dist, avg_action_dist]):.4f}"
    )
    print(f"   Cross-category avg: {avg_cross_dist:.4f}")

    ratio = avg_cross_dist / np.mean(
        [avg_animal_dist, avg_vehicle_dist, avg_action_dist]
    )
    print(f"   Separation ratio: {ratio:.2f}x")

    if ratio > 1.5:
        print("   ✓ Semantic clustering WORKING - categories are well-separated!")
    elif ratio > 1.0:
        print("   ⚠ Semantic clustering PARTIAL - some separation detected")
    else:
        print("   ✗ Semantic clustering FAILED - no clear separation")

    # Show sample coordinates
    print("\n📍 Sample coordinates:")
    print(f"   cat:   {cat_coord}")
    print(f"   dog:   {dog_coord}")
    print(f"   car:   {car_coord}")
    print(f"   truck: {truck_coord}")

    print("\n" + "=" * 60)


if __name__ == "__main__":
    asyncio.run(test_semantic_embeddings())
