#include <ingestion/text_ingester.hpp>
#include <storage/physicality_store.hpp>
#include <storage/atom_store.hpp>
#include <fstream>
#include <codecvt>
#include <locale>
#include <iomanip>
#include <sstream>

namespace Hartonomous {

TextIngester::TextIngester(PostgresConnection& db) : db_(db) {}

std::string TextIngester::hash_to_uuid(const BLAKE3Pipeline::Hash& hash) {
    std::ostringstream ss;
    ss << std::hex << std::setfill('0');
    for (int i = 0; i < 16; ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) ss << '-';
        ss << std::setw(2) << static_cast<int>(hash[i]);
    }
    return ss.str();
}

IngestionStats TextIngester::ingest(const std::string& text) {
    IngestionStats stats;
    stats.original_bytes = text.size();

    std::u32string utf32_text = utf8_to_utf32(text);

    auto atoms = decompose_atoms(utf32_text);
    stats.atoms_total = atoms.size();

    PostgresConnection::Transaction txn(db_);

    PhysicalityStore phys_store(db_);
    AtomStore atom_store(db_);

    for (const auto& a : atoms) {
        PhysicalityRecord phys_rec;
        phys_rec.id = a.physicality.id;
        phys_rec.hilbert_index = a.physicality.hilbert_index;
        phys_rec.centroid = a.physicality.centroid;
        phys_store.store(phys_rec);

        AtomRecord atom_rec;
        atom_rec.id = a.id;
        atom_rec.physicality_id = a.physicality.id;
        atom_rec.codepoint = a.codepoint;
        atom_store.store(atom_rec);
    }

    phys_store.flush();
    atom_store.flush();
    stats.atoms_new = atom_store.count();

    txn.commit();
    return stats;
}

IngestionStats TextIngester::ingest_file(const std::string& path) {
    std::ifstream file(path);
    if (!file) throw std::runtime_error("Failed to open file: " + path);
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
        auto result = CodepointProjection::project(cp);
        atom.physicality.centroid = result.s3_position;
        atom.physicality.hilbert_index = result.hilbert_index;

        std::vector<uint8_t> phys_data(4 * sizeof(double));
        std::memcpy(phys_data.data(), result.s3_position.data(), 4 * sizeof(double));
        atom.physicality.id = BLAKE3Pipeline::hash(phys_data);
        atom.id = result.hash;
        atoms.push_back(atom);
    }
    return atoms;
}

std::u32string TextIngester::utf8_to_utf32(const std::string& utf8) {
    std::wstring_convert<std::codecvt_utf8<char32_t>, char32_t> converter;
    return converter.from_bytes(utf8);
}

}

