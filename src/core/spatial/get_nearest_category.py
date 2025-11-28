"""Get nearest category landmark for Y coordinate."""

from .category_landmarks import CATEGORY_LANDMARKS


def get_nearest_category(y: float) -> str:
    """Get nearest landmark category for a given Y coordinate."""
    return min(CATEGORY_LANDMARKS.items(), key=lambda kv: abs(kv[1] - y))[0]
