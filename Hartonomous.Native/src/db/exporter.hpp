#pragma once

#include "../atoms/pair_encoding_cascade.hpp"
#include "../atoms/semantic_decompose.hpp"
#include <fstream>
#include <filesystem>
#include <string>

namespace hartonomous::db {

namespace fs = std::filesystem;

/// Exports compositions to PostgreSQL COPY format files.
/// Load with: psql -U hartonomous -d hartonomous -f load.sql
class DatabaseExporter {
public:
    static void export_all(const GlobalCompositionStore& store, const fs::path& output_dir) {
        fs::create_directories(output_dir);

        export_atoms(output_dir / "atoms.copy");
        export_atom_table(store, output_dir / "atom.copy");
        export_composition_relations(store, output_dir / "composition_relation.copy");
        export_load_script(output_dir / "load.sql");
    }

private:
    static void export_atoms(const fs::path& path) {
        std::ofstream f(path);
        for (int i = 0; i < 256; ++i) {
            AtomId id = SemanticDecompose::get_atom_id(i);
            auto coord = SemanticDecompose::get_coord(i);
            // hilbert_high, hilbert_low, codepoint, child_count, semantic_position
            f << id.high << '\t' << id.low << '\t' << i << '\t' << 0 << '\t'
              << "SRID=0;POINTZM(" << coord.page << ' ' << coord.type << ' '
              << coord.base << ' ' << coord.variant << ")\n";
        }
    }

    static void export_atom_table(const GlobalCompositionStore& store, const fs::path& path) {
        std::ofstream f(path);
        for (const auto& c : store.compositions()) {
            // hilbert_high, hilbert_low, codepoint (NULL for compositions), child_count, semantic_position (NULL)
            f << c.parent.id_high << '\t' << c.parent.id_low << '\t'
              << "\\N\t2\t\\N\n";
        }
    }

    static void export_composition_relations(const GlobalCompositionStore& store, const fs::path& path) {
        std::ofstream f(path);
        for (const auto& c : store.compositions()) {
            // parent_hilbert_high, parent_hilbert_low, child_index, child_hilbert_high, child_hilbert_low, repetition_count
            f << c.parent.id_high << '\t' << c.parent.id_low << '\t'
              << 0 << '\t' << c.left.id_high << '\t' << c.left.id_low << '\t' << 1 << '\n';
            f << c.parent.id_high << '\t' << c.parent.id_low << '\t'
              << 1 << '\t' << c.right.id_high << '\t' << c.right.id_low << '\t' << 1 << '\n';
        }
    }

    static void export_load_script(const fs::path& path) {
        std::ofstream f(path);
        f << "-- Hartonomous Database Load Script\n";
        f << "-- Run: psql -U hartonomous -d hartonomous -f load.sql\n\n";
        f << "BEGIN;\n\n";
        f << "TRUNCATE composition_relation, atom CASCADE;\n\n";
        f << "\\copy atom (hilbert_high, hilbert_low, codepoint, child_count, semantic_position) FROM 'atoms.copy'\n";
        f << "\\copy atom (hilbert_high, hilbert_low, codepoint, child_count, semantic_position) FROM 'atom.copy'\n";
        f << "\\copy composition_relation (parent_hilbert_high, parent_hilbert_low, child_index, child_hilbert_high, child_hilbert_low, repetition_count) FROM 'composition_relation.copy'\n\n";
        f << "COMMIT;\n";
        f << "\\echo 'Load complete'\n";
    }
};

} // namespace hartonomous::db