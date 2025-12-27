#pragma once

#include "node_ref.hpp"
#include <cstdint>

namespace hartonomous {

/// Run-length encoded child reference.
/// Instead of [A, A, A, A], store [A, count=4].
struct RLEChild {
    NodeRef ref;
    std::uint32_t count;  // How many times this appears consecutively

    constexpr RLEChild() noexcept : ref(), count(1) {}
    constexpr RLEChild(NodeRef r, std::uint32_t c = 1) noexcept : ref(r), count(c) {}
};

} // namespace hartonomous
