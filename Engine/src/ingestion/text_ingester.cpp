/**
 * @file text_ingester.cpp
 * @brief Text ingestion implementation
 */

#include <ingestion/text_ingester.hpp>
#include <fstream>
#include <sstream>
#include <codecvt>
#include <locale>
#include <algorithm>
#include <cctype>

namespace Hartonomous {

TextIngester::TextIngester(PostgresConnection& db) : db_(db) {}

IngestionStats TextIngester::ingest(const std::string& text) {
    IngestionStats stats;
    stats.original_bytes = text.size();

    // Convert to UTF-32
    std::u32string utf32_text = utf8_to_utf32(text);

    // Decompose into atoms
    auto atoms = decompose_atoms(utf32_text);
    stats.atoms_total = atoms.size();

    // Decompose into compositions (words)
    auto compositions = decompose_compositions(utf32_text);
    stats.compositions_total = compositions.size();

    // Decompose into relations (sentences)
    auto relations = decompose_relations(compositions);
    stats.relations_total = relations.size();

    // Store in database
    PostgresConnection::Transaction txn(db_);

    store_atoms(atoms, stats);
    store_compositions(compositions, stats);
    store_relations(relations, stats);

    txn.commit();

    // Estimate stored bytes (32 bytes per hash, minimal overhead)
    stats.stored_bytes = (stats.atoms_new + stats.compositions_new + stats.relations_total) * 32;

    return stats;
}

IngestionStats TextIngester::ingest_file(const std::string& path) {
    std::ifstream file(path);
    if (!file) {
        throw std::runtime_error("Failed to open file: " + path);
    }

    std::ostringstream buffer;
    buffer << file.rdbuf();

    return ingest(buffer.str());
}

std::vector<TextIngester::Atom> TextIngester::decompose_atoms(const std::u32string& text) {
    std::vector<Atom> atoms;
    atoms.reserve(text.size());

    for (char32_t cp : text) {
        Atom atom;
        atom.codepoint = cp;

        // Project to 4D
        auto result = CodepointProjection::project(cp);
        atom.hash = result.hash;
        atom.s3_position = result.s3_position;
        atom.hilbert_index = result.hilbert_index;

        atoms.push_back(atom);
    }

    return atoms;
}

std::vector<TextIngester::Composition> TextIngester::decompose_compositions(const std::u32string& text) {
    std::vector<Composition> compositions;

    // Tokenize into words
    auto words = tokenize_words(text);

    for (const auto& word : words) {
        Composition comp;
        comp.text = std::wstring_convert<std::codecvt_utf8<char32_t>, char32_t>().to_bytes(word);

        // Get atom hashes for this word
        std::vector<Vec4> positions;
        for (char32_t cp : word) {
            auto result = CodepointProjection::project(cp);
            comp.atom_hashes.push_back(result.hash);
            positions.push_back(result.s3_position);
        }

        // Compute composition hash (hash of concatenated atom hashes)
        comp.hash = compute_composition_hash(comp.atom_hashes);

        // Compute centroid
        comp.centroid = compute_centroid(positions);

        // Compute Hilbert index
        comp.hilbert_index = HilbertCurve4D::encode(comp.centroid, 16);

        compositions.push_back(comp);
    }

    return compositions;
}

std::vector<TextIngester::Relation> TextIngester::decompose_relations(
    const std::vector<Composition>& compositions
) {
    std::vector<Relation> relations;

    // Simple: Create one relation per sentence (for now, just group all compositions)
    // TODO: Better sentence detection

    if (compositions.empty()) return relations;

    Relation rel;

    std::vector<Vec4> centroids;
    for (const auto& comp : compositions) {
        rel.composition_hashes.push_back(comp.hash);
        centroids.push_back(comp.centroid);
    }

    // Hash of all composition hashes
    rel.hash = compute_composition_hash(rel.composition_hashes);

    // Centroid of all composition centroids
    rel.centroid = compute_centroid(centroids);
    
    // Compute Hilbert index
    rel.hilbert_index = HilbertCurve4D::encode(rel.centroid, 16);

    relations.push_back(rel);

    return relations;
}

void TextIngester::store_atoms(const std::vector<Atom>& atoms, IngestionStats& stats) {
    for (const auto& atom : atoms) {
        std::string hash_hex = BLAKE3Pipeline::to_hex(atom.hash);

        // Check if already stored
        if (seen_atom_hashes_.count(hash_hex)) {
            stats.atoms_existing++;
            continue;
        }

        auto exists = db_.query_single(
            "SELECT 1 FROM hartonomous.atoms WHERE hash = $1",
            {hash_hex}
        );

        if (exists) {
            stats.atoms_existing++;
            seen_atom_hashes_.insert(hash_hex);
            continue;
        }

        // Insert new atom
        db_.execute(
            "INSERT INTO hartonomous.atoms (hash, codepoint, centroid_x, centroid_y, centroid_z, centroid_w, centroid, hilbert_hi, hilbert_lo) "
            "VALUES ($1, $2, $3, $4, $5, $6, ST_SetSRID(ST_MakePoint($3::float8, $4::float8, $5::float8, $6::float8), 0), $7, $8)",
            {
                hash_hex,
                std::to_string((int)atom.codepoint),
                std::to_string(atom.s3_position[0]),
                std::to_string(atom.s3_position[1]),
                std::to_string(atom.s3_position[2]),
                std::to_string(atom.s3_position[3]),
                std::to_string((int64_t)atom.hilbert_index.hi),
                std::to_string((int64_t)atom.hilbert_index.lo)
            }
        );

        stats.atoms_new++;
        seen_atom_hashes_.insert(hash_hex);
    }
}

void TextIngester::store_compositions(const std::vector<Composition>& compositions, IngestionStats& stats) {
    for (const auto& comp : compositions) {
        std::string hash_hex = BLAKE3Pipeline::to_hex(comp.hash);

        // Check if already stored
        if (seen_composition_hashes_.count(hash_hex)) {
            stats.compositions_existing++;
            continue;
        }

        auto exists = db_.query_single(
            "SELECT 1 FROM hartonomous.compositions WHERE hash = $1",
            {hash_hex}
        );

        if (exists) {
            stats.compositions_existing++;
            seen_composition_hashes_.insert(hash_hex);
            continue;
        }

        // Insert composition
        db_.execute(
            "INSERT INTO hartonomous.compositions (hash, text, centroid_x, centroid_y, centroid_z, centroid_w, centroid, hilbert_hi, hilbert_lo, length) "
            "VALUES ($1, $2, $3, $4, $5, $6, ST_SetSRID(ST_MakePoint($3::float8, $4::float8, $5::float8, $6::float8), 0), $7, $8, $9)",
            {
                hash_hex,
                comp.text,
                std::to_string(comp.centroid[0]),
                std::to_string(comp.centroid[1]),
                std::to_string(comp.centroid[2]),
                std::to_string(comp.centroid[3]),
                std::to_string((int64_t)comp.hilbert_index.hi),
                std::to_string((int64_t)comp.hilbert_index.lo),
                std::to_string(comp.text.size())
            }
        );

        // Insert composition_atoms links
        int position = 0;
        for (const auto& atom_hash : comp.atom_hashes) {
            db_.execute(
                "INSERT INTO hartonomous.composition_atoms (composition_hash, atom_hash, position) VALUES ($1, $2, $3)",
                {hash_hex, BLAKE3Pipeline::to_hex(atom_hash), std::to_string(position++)}
            );
        }

        stats.compositions_new++;
        seen_composition_hashes_.insert(hash_hex);
    }
}

void TextIngester::store_relations(const std::vector<Relation>& relations, IngestionStats& stats) {
    for (const auto& rel : relations) {
        std::string hash_hex = BLAKE3Pipeline::to_hex(rel.hash);

        // Insert relation
        db_.execute(
            "INSERT INTO hartonomous.relations (hash, level, length, centroid_x, centroid_y, centroid_z, centroid_w, centroid, parent_type, hilbert_hi, hilbert_lo) "
            "VALUES ($1, $2, $3, $4, $5, $6, $7, ST_SetSRID(ST_MakePoint($4::float8, $5::float8, $6::float8, $7::float8), 0), $8, $9, $10)",
            {
                hash_hex,
                "1",  // Level 1 relation
                std::to_string(rel.composition_hashes.size()),
                std::to_string(rel.centroid[0]),
                std::to_string(rel.centroid[1]),
                std::to_string(rel.centroid[2]),
                std::to_string(rel.centroid[3]),
                "composition",
                std::to_string((int64_t)rel.hilbert_index.hi),
                std::to_string((int64_t)rel.hilbert_index.lo)
            }
        );

        // Insert relation_children links
        size_t position = 0;
        for (const auto& comp_hash : rel.composition_hashes) {
            db_.execute(
                "INSERT INTO hartonomous.relation_children (relation_hash, child_hash, child_type, position) "
                "VALUES ($1, $2, $3, $4)",
                {
                    hash_hex,
                    BLAKE3Pipeline::to_hex(comp_hash),
                    "composition",
                    std::to_string(position++)
                }
            );
        }
    }
}

std::u32string TextIngester::utf8_to_utf32(const std::string& utf8) {
    std::wstring_convert<std::codecvt_utf8<char32_t>, char32_t> converter;
    return converter.from_bytes(utf8);
}

std::vector<std::u32string> TextIngester::tokenize_words(const std::u32string& text) {
    std::vector<std::u32string> words;
    std::u32string current_word;

    for (char32_t cp : text) {
        // Simple tokenization: split on whitespace and punctuation
        if (std::isspace(cp) || std::ispunct(cp)) {
            if (!current_word.empty()) {
                words.push_back(current_word);
                current_word.clear();
            }
        } else {
            current_word.push_back(cp);
        }
    }

    if (!current_word.empty()) {
        words.push_back(current_word);
    }

    return words;
}

Vec4 TextIngester::compute_centroid(const std::vector<Vec4>& positions) {
    if (positions.empty()) {
        return Vec4(0, 0, 0, 0);
    }

    Vec4 sum(0, 0, 0, 0);
    for (const auto& pos : positions) {
        sum += pos;
    }

    Vec4 centroid = sum / (double)positions.size();
    centroid.normalize();  // Project back to S³ surface

    return centroid;
}

BLAKE3Pipeline::Hash TextIngester::compute_composition_hash(
    const std::vector<BLAKE3Pipeline::Hash>& atom_hashes
) {
    // Concatenate all hashes and hash the result
    std::vector<uint8_t> data;
    data.reserve(atom_hashes.size() * 32);

    for (const auto& hash : atom_hashes) {
        data.insert(data.end(), hash.begin(), hash.end());
    }

    return BLAKE3Pipeline::hash(data);
}

} // namespace Hartonomous
