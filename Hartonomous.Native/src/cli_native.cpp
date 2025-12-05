// cli_native.cpp - Command-line interface for Hartonomous native Hilbert curve functions
// Usage: Hartonomous.Native.CLI.exe {functionName} {param1} {param2} ...
//
// Available functions:
//   HilbertEncode {x} {y} {z} {m} - Encode 4D coordinates to Hilbert index
//   HilbertDecode {index}         - Decode Hilbert index to 4D coordinates
//
// Examples:
//   Hartonomous.Native.CLI.exe HilbertEncode 100 200 300 400
//   Hartonomous.Native.CLI.exe HilbertDecode 123456789

#include <iostream>
#include <string>
#include <cstdint>
#include <stdexcept>

// Function prototypes from native library
extern "C" {
    void HilbertEncode4D(uint32_t x, uint32_t y, uint32_t z, uint32_t m, int precision, uint64_t* resultHigh, uint64_t* resultLow);
    void HilbertDecode4D(uint64_t indexHigh, uint64_t indexLow, int precision, uint32_t* resultCoords);
}

static constexpr int B = 21;  // Bits per dimension (21-bit quantization)
static constexpr int N = 4;   // Number of dimensions (POINTZM)

void PrintUsage(const char* programName) {
    std::cout << "Hartonomous Native CLI - Hilbert Curve Operations\n\n";
    std::cout << "Usage: " << programName << " {functionName} {params...}\n\n";
    std::cout << "Available Functions:\n";
    std::cout << "  HilbertEncode {x} {y} {z} {m}\n";
    std::cout << "    - Encode 4D POINTZM coordinates to 128-bit Hilbert index\n";
    std::cout << "    - Parameters: x, y, z, m (0 to 2097151 for 21-bit quantized values)\n";
    std::cout << "    - Returns: high64 low64 (two 64-bit values as hex)\n\n";
    std::cout << "  HilbertDecode {high64} {low64}\n";
    std::cout << "    - Decode 128-bit Hilbert index to 4D coordinates\n";
    std::cout << "    - Parameters: high64, low64 (64-bit hex values)\n";
    std::cout << "    - Returns: x y z m\n\n";
    std::cout << "Examples:\n";
    std::cout << "  " << programName << " HilbertEncode 100 200 300 400\n";
    std::cout << "  " << programName << " HilbertDecode 0x1234567890ABCDEF 0xFEDCBA0987654321\n";
}

void ExecuteHilbertEncode(int argc, char* argv[]) {
    if (argc != 6) {
        throw std::invalid_argument("HilbertEncode requires 4 parameters: x y z m");
    }

    // Parse coordinates
    uint32_t x = std::stoul(argv[2]);
    uint32_t y = std::stoul(argv[3]);
    uint32_t z = std::stoul(argv[4]);
    uint32_t m = std::stoul(argv[5]);

    // Validate range (21-bit max = 2,097,151)
    constexpr uint32_t MAX_VAL = (1u << B) - 1;
    if (x > MAX_VAL || y > MAX_VAL || z > MAX_VAL || m > MAX_VAL) {
        throw std::out_of_range("Coordinates must be in range [0, 2097151] for 21-bit quantization");
    }

    // Encode to Hilbert index
    uint64_t high64, low64;
    HilbertEncode4D(x, y, z, m, B, &high64, &low64);

    // Output results
    std::cout << "Input: x=" << x << " y=" << y << " z=" << z << " m=" << m << "\n";
    std::cout << "Hilbert Index (128-bit):\n";
    std::cout << "  High64: 0x" << std::hex << high64 << "\n";
    std::cout << "  Low64:  0x" << std::hex << low64 << std::dec << "\n";
}

void ExecuteHilbertDecode(int argc, char* argv[]) {
    if (argc != 4) {
        throw std::invalid_argument("HilbertDecode requires 2 parameters: high64 low64 (as hex)");
    }

    // Parse Hilbert index
    uint64_t high64 = std::stoull(argv[2], nullptr, 16);
    uint64_t low64 = std::stoull(argv[3], nullptr, 16);

    // Decode to coordinates
    uint32_t coords[N];
    HilbertDecode4D(high64, low64, B, coords);

    // Output results
    std::cout << "Hilbert Index (128-bit): High=0x" << std::hex << high64 << " Low=0x" << low64 << std::dec << "\n";
    std::cout << "Decoded Coordinates:\n";
    std::cout << "  X: " << coords[0] << "\n";
    std::cout << "  Y: " << coords[1] << "\n";
    std::cout << "  Z: " << coords[2] << "\n";
    std::cout << "  M: " << coords[3] << "\n";
}

int main(int argc, char* argv[]) {
    try {
        if (argc < 2) {
            PrintUsage(argv[0]);
            return 1;
        }

        std::string command = argv[1];

        if (command == "HilbertEncode") {
            ExecuteHilbertEncode(argc, argv);
        }
        else if (command == "HilbertDecode") {
            ExecuteHilbertDecode(argc, argv);
        }
        else if (command == "--help" || command == "-h") {
            PrintUsage(argv[0]);
            return 0;
        }
        else {
            std::cerr << "Error: Unknown command '" << command << "'\n\n";
            PrintUsage(argv[0]);
            return 1;
        }

        return 0;
    }
    catch (const std::exception& ex) {
        std::cerr << "Error: " << ex.what() << "\n";
        return 1;
    }
}
