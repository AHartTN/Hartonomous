"""Infer specificity level from node type."""


def infer_specificity(
    node_type: str, is_abstract: bool = False, has_value: bool = False
) -> str:
    """Infer specificity level from node type and context."""
    if is_abstract:
        return "abstract"

    node_lower = node_type.lower()

    if node_lower in ("interface", "abstract-class", "protocol"):
        return "abstract"
    elif node_lower in ("generic-parameter", "type-parameter", "template"):
        return "generic"
    elif node_lower in ("class", "method", "function", "field"):
        return "concrete"
    elif node_lower in ("variable", "parameter", "instance"):
        return "instance"
    elif node_lower in ("literal", "constant") or has_value:
        return "literal"
    else:
        return "concrete"
