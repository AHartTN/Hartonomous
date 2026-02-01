#include <unicode/ingestor/ucd_parser.hpp>
#include <fstream>
#include <iostream>
#include <algorithm>
#include <vector>
#include <cstring>
#include <regex>

namespace Hartonomous::unicode {

UCDParser::UCDParser(const std::string& data_dir) : data_dir_(data_dir) {}

void UCDParser::parse_all() {
    // Primary source: ucd.all.flat.xml contains ALL properties
    parse_xml();

    // Supplementary data
    parse_all_keys();      // DUCET collation
    parse_confusables();   // Confusable mappings
    parse_unihan();        // CJK readings/radicals

    // Post-processing
    find_base_characters();
    build_semantic_edges();
}

void UCDParser::parse_xml() {
    std::string path = data_dir_ + "/ucd.all.flat.xml";
    std::ifstream file(path);
    if (!file) {
        std::cerr << "Warning: Could not open " << path << "\n";
        return;
    }

    std::cout << "Parsing ucd.all.flat.xml (this may take a moment)...\n";

    std::string line;
    size_t count = 0;
    while (std::getline(file, line)) {
        // Look for <char elements
        size_t char_pos = line.find("<char ");
        if (char_pos != std::string::npos) {
            parse_char_element(line);
            count++;
            if (count % 50000 == 0) {
                std::cout << "  Parsed " << count << " codepoints...\n";
            }
        }
    }

    std::cout << "Parsed " << codepoints_.size() << " codepoints from ucd.all.flat.xml\n";
}

void UCDParser::parse_char_element(const std::string& line) {
    CodepointMetadata meta;

    // Extract attributes using simple parsing (faster than regex for this format)
    auto get_attr = [&line](const char* name) -> std::string {
        std::string search = std::string(name) + "=\"";
        size_t start = line.find(search);
        if (start == std::string::npos) return "";
        start += search.length();
        size_t end = line.find('"', start);
        if (end == std::string::npos) return "";
        return line.substr(start, end - start);
    };

    // Get codepoint
    std::string cp_str = get_attr("cp");
    if (cp_str.empty()) return;

    try {
        meta.codepoint = std::stoul(cp_str, nullptr, 16);
    } catch (...) {
        return;
    }

    // Core properties
    meta.name = get_attr("na");
    meta.name1 = get_attr("na1");
    meta.general_category = get_attr("gc");
    meta.script = get_attr("sc");
    meta.script_extensions = get_attr("scx");
    meta.block = get_attr("blk");
    meta.age = get_attr("age");

    // Combining class
    std::string ccc = get_attr("ccc");
    if (!ccc.empty()) {
        try { meta.combining_class = static_cast<uint8_t>(std::stoul(ccc)); }
        catch (...) {}
    }

    // Decomposition
    meta.decomposition_type = get_attr("dt");
    meta.decomposition_mapping = get_attr("dm");

    // Case mappings
    meta.uppercase_mapping = get_attr("uc");
    meta.lowercase_mapping = get_attr("lc");
    meta.titlecase_mapping = get_attr("tc");
    meta.simple_uppercase = get_attr("suc");
    meta.simple_lowercase = get_attr("slc");
    meta.simple_titlecase = get_attr("stc");
    meta.simple_case_folding = get_attr("scf");
    meta.case_folding = get_attr("cf");

    // Numeric
    meta.numeric_type = get_attr("nt");
    meta.numeric_value = get_attr("nv");

    // Bidi
    meta.bidi_class = get_attr("bc");
    meta.bidi_paired_bracket_type = get_attr("bpt");
    meta.bidi_paired_bracket = get_attr("bpb");
    meta.bidi_mirroring_glyph = get_attr("bmg");
    meta.bidi_mirrored = get_attr("Bidi_M") == "Y";
    meta.bidi_control = get_attr("Bidi_C") == "Y";

    // Joining
    meta.joining_type = get_attr("jt");
    meta.joining_group = get_attr("jg");
    meta.join_control = get_attr("Join_C") == "Y";

    // Width and breaking
    meta.east_asian_width = get_attr("ea");
    meta.line_break = get_attr("lb");
    meta.word_break = get_attr("WB");
    meta.sentence_break = get_attr("SB");
    meta.grapheme_cluster_break = get_attr("GCB");
    meta.indic_syllabic_category = get_attr("InSC");
    meta.indic_positional_category = get_attr("InPC");
    meta.vertical_orientation = get_attr("vo");

    // Hangul
    meta.hangul_syllable_type = get_attr("hst");
    meta.jamo_short_name = get_attr("JSN");

    // Boolean properties
    meta.is_alphabetic = get_attr("Alpha") == "Y";
    meta.is_uppercase = get_attr("Upper") == "Y";
    meta.is_lowercase = get_attr("Lower") == "Y";
    meta.is_cased = get_attr("Cased") == "Y";
    meta.is_math = get_attr("Math") == "Y";
    meta.is_hex_digit = get_attr("Hex") == "Y";
    meta.is_ascii_hex_digit = get_attr("AHex") == "Y";
    meta.is_ideographic = get_attr("Ideo") == "Y";
    meta.is_unified_ideograph = get_attr("UIdeo") == "Y";
    meta.is_radical = get_attr("Radical") == "Y";
    meta.is_dash = get_attr("Dash") == "Y";
    meta.is_whitespace = get_attr("WSpace") == "Y";
    meta.is_quotation_mark = get_attr("QMark") == "Y";
    meta.is_terminal_punctuation = get_attr("Term") == "Y";
    meta.is_sentence_terminal = get_attr("STerm") == "Y";
    meta.is_diacritic = get_attr("Dia") == "Y";
    meta.is_extender = get_attr("Ext") == "Y";
    meta.is_soft_dotted = get_attr("SD") == "Y";
    meta.is_deprecated = get_attr("Dep") == "Y";
    meta.is_default_ignorable = get_attr("DI") == "Y";
    meta.is_variation_selector = get_attr("VS") == "Y";
    meta.is_noncharacter = get_attr("NChar") == "Y";
    meta.is_pattern_whitespace = get_attr("Pat_WS") == "Y";
    meta.is_pattern_syntax = get_attr("Pat_Syn") == "Y";
    meta.is_grapheme_base = get_attr("Gr_Base") == "Y";
    meta.is_grapheme_extend = get_attr("Gr_Ext") == "Y";
    meta.is_id_start = get_attr("IDS") == "Y";
    meta.is_id_continue = get_attr("IDC") == "Y";
    meta.is_xid_start = get_attr("XIDS") == "Y";
    meta.is_xid_continue = get_attr("XIDC") == "Y";
    meta.composition_exclusion = get_attr("CE") == "Y";
    meta.full_composition_exclusion = get_attr("Comp_Ex") == "Y";
    meta.changes_when_lowercased = get_attr("CWL") == "Y";
    meta.changes_when_uppercased = get_attr("CWU") == "Y";
    meta.changes_when_titlecased = get_attr("CWT") == "Y";
    meta.changes_when_casefolded = get_attr("CWCF") == "Y";
    meta.changes_when_casemapped = get_attr("CWCM") == "Y";
    meta.changes_when_nfkc_casefolded = get_attr("CWKCF") == "Y";
    meta.prepended_concatenation_mark = get_attr("PCM") == "Y";
    meta.regional_indicator = get_attr("RI") == "Y";

    // Emoji properties
    meta.is_emoji = get_attr("Emoji") == "Y";
    meta.is_emoji_presentation = get_attr("EPres") == "Y";
    meta.is_emoji_modifier = get_attr("EMod") == "Y";
    meta.is_emoji_modifier_base = get_attr("EBase") == "Y";
    meta.is_emoji_component = get_attr("EComp") == "Y";
    meta.is_extended_pictographic = get_attr("ExtPict") == "Y";

    // Han Radical/Stroke
    std::string rad = get_attr("Radical");
    if (!rad.empty() && rad != "N" && rad != "Y") {
        try { meta.radical = std::stoul(rad); } catch (...) {}
    }
    std::string strokes = get_attr("Strokes");
    if (!strokes.empty()) {
        try { meta.strokes = std::stoi(strokes); } catch (...) {}
    }

    // Normalization
    meta.nfc_quick_check = get_attr("NFC_QC");
    meta.nfd_quick_check = get_attr("NFD_QC");
    meta.nfkc_quick_check = get_attr("NFKC_QC");
    meta.nfkd_quick_check = get_attr("NFKD_QC");
    meta.nfkc_casefold = get_attr("NFKC_CF");
    meta.nfkc_simple_casefold = get_attr("NFKC_SCF");

    // Parse name-alias elements if present
    size_t alias_pos = 0;
    while ((alias_pos = line.find("<name-alias", alias_pos)) != std::string::npos) {
        size_t alias_end = line.find("/>", alias_pos);
        if (alias_end == std::string::npos) break;

        std::string alias_elem = line.substr(alias_pos, alias_end - alias_pos + 2);

        auto get_alias_attr = [&alias_elem](const char* name) -> std::string {
            std::string search = std::string(name) + "=\"";
            size_t start = alias_elem.find(search);
            if (start == std::string::npos) return "";
            start += search.length();
            size_t end = alias_elem.find('"', start);
            if (end == std::string::npos) return "";
            return alias_elem.substr(start, end - start);
        };

        NameAlias na;
        na.alias = get_alias_attr("alias");
        na.type = get_alias_attr("type");
        if (!na.alias.empty()) {
            meta.name_aliases.push_back(na);
        }

        alias_pos = alias_end + 2;
    }

    codepoints_[meta.codepoint] = std::move(meta);
}

void UCDParser::parse_all_keys() {
    std::string path = data_dir_ + "/allkeys.txt";
    std::ifstream file(path);
    if (!file) return;

    std::string line;
    while (std::getline(file, line)) {
        if (line.empty() || line[0] == '@' || line[0] == '#') continue;

        size_t semi = line.find(';');
        if (semi == std::string::npos) continue;

        std::string cp_str = line.substr(0, semi);

        // Trim
        size_t first = cp_str.find_first_not_of(" \t");
        if (first == std::string::npos) continue;
        size_t last = cp_str.find_last_not_of(" \t");
        cp_str = cp_str.substr(first, (last - first + 1));

        // Skip multi-codepoint sequences
        if (cp_str.find(' ') != std::string::npos) continue;

        try {
            uint32_t cp = std::stoul(cp_str, nullptr, 16);
            if (codepoints_.count(cp) == 0) continue;

            size_t start = line.find('[', semi);
            while (start != std::string::npos) {
                size_t dot1 = line.find('.', start);
                if (dot1 == std::string::npos) break;
                size_t dot2 = line.find('.', dot1 + 1);
                if (dot2 == std::string::npos) break;
                size_t dot3 = line.find('.', dot2 + 1);
                if (dot3 == std::string::npos) break;
                size_t end = line.find(']', dot3 + 1);
                if (end == std::string::npos) break;

                UCAWeights weights;
                weights.primary = std::stoul(line.substr(start + 2, dot1 - start - 2), nullptr, 16);
                weights.secondary = std::stoul(line.substr(dot1 + 1, dot2 - dot1 - 1), nullptr, 16);
                weights.tertiary = std::stoul(line.substr(dot2 + 1, dot3 - dot2 - 1), nullptr, 16);
                codepoints_[cp].uca_elements.push_back(weights);

                start = line.find('[', end + 1);
            }
        } catch (...) { continue; }
    }
}

void UCDParser::parse_confusables() {
    std::string path = data_dir_ + "/confusables.txt";
    std::ifstream file(path);
    if (!file) return;

    std::string line;
    while (std::getline(file, line)) {
        if (line.empty() || line[0] == '#') continue;

        size_t semi1 = line.find(';');
        if (semi1 == std::string::npos) continue;
        size_t semi2 = line.find(';', semi1 + 1);
        if (semi2 == std::string::npos) continue;

        std::string source_str = line.substr(0, semi1);
        std::string target_str = line.substr(semi1 + 1, semi2 - semi1 - 1);

        // Trim
        auto trim = [](std::string& s) {
            size_t f = s.find_first_not_of(" \t");
            size_t l = s.find_last_not_of(" \t");
            if (f != std::string::npos && l != std::string::npos)
                s = s.substr(f, l - f + 1);
        };
        trim(source_str);
        trim(target_str);

        // Skip multi-codepoint confusables for now
        if (source_str.find(' ') != std::string::npos) continue;
        if (target_str.find(' ') != std::string::npos) continue;

        try {
            uint32_t source = std::stoul(source_str, nullptr, 16);
            uint32_t target = std::stoul(target_str, nullptr, 16);

            if (codepoints_.count(source)) {
                SemanticEdge edge;
                edge.target_cp = target;
                edge.weight = static_cast<uint32_t>(EdgeWeight::Confusable);
                edge.type = "confusable";
                codepoints_[source].edges.push_back(edge);
            }
        } catch (...) { continue; }
    }
}

void UCDParser::parse_unihan() {
    // Parse all Unihan files for CJK data
    const char* unihan_files[] = {
        "/Unihan_RadicalStrokeCounts.txt",
        "/Unihan_Readings.txt",
        "/Unihan_Variants.txt",
        nullptr
    };

    for (int i = 0; unihan_files[i]; ++i) {
        std::string path = data_dir_ + unihan_files[i];
        std::ifstream file(path);
        if (!file) continue;

        std::string line;
        while (std::getline(file, line)) {
            if (line.empty() || line[0] == '#') continue;

            // Format: U+XXXX<tab>kField<tab>value
            if (line.size() < 7 || line[0] != 'U' || line[1] != '+') continue;

            size_t tab1 = line.find('\t');
            if (tab1 == std::string::npos) continue;
            size_t tab2 = line.find('\t', tab1 + 1);
            if (tab2 == std::string::npos) continue;

            try {
                uint32_t cp = std::stoul(line.substr(2, tab1 - 2), nullptr, 16);
                if (codepoints_.count(cp) == 0) continue;

                std::string field = line.substr(tab1 + 1, tab2 - tab1 - 1);
                std::string value = line.substr(tab2 + 1);

                if (field == "kRSUnicode") {
                    size_t dot = value.find('.');
                    if (dot != std::string::npos) {
                        std::string rad_str = value.substr(0, dot);
                        if (!rad_str.empty() && rad_str.back() == '\'') rad_str.pop_back();
                        codepoints_[cp].radical = std::stoul(rad_str);
                        codepoints_[cp].strokes = std::stoi(value.substr(dot + 1));
                    }
                }
                // Store other Unihan properties in extra_properties
                codepoints_[cp].extra_properties[field] = value;
            } catch (...) { continue; }
        }
    }
}

void UCDParser::parse_emoji_sequences() {
    // Emoji sequences are multi-codepoint - stored as relations, not in atom metadata
    // This is handled at a higher level during ingestion
}

void UCDParser::find_base_characters() {
    for (auto& pair : codepoints_) {
        pair.second.base_codepoint = trace_decomposition(pair.first);
    }
}

uint32_t UCDParser::trace_decomposition(uint32_t cp) {
    constexpr int MAX_DEPTH = 20;
    uint32_t current = cp;

    for (int depth = 0; depth < MAX_DEPTH; ++depth) {
        auto it = codepoints_.find(current);
        if (it == codepoints_.end()) return current;

        const std::string& decomp = it->second.decomposition_mapping;
        if (decomp.empty() || decomp == "#") return current;

        // Parse first non-tag codepoint from decomposition
        uint32_t first_cp = 0;
        size_t start = 0;
        while (start < decomp.length()) {
            size_t end = decomp.find(' ', start);
            if (end == std::string::npos) end = decomp.length();
            std::string part = decomp.substr(start, end - start);
            start = end + 1;

            if (part.empty()) continue;
            if (part[0] == '<') continue; // Skip <compat> tags
            try {
                first_cp = std::stoul(part, nullptr, 16);
                break;
            } catch (...) { continue; }
        }

        if (first_cp == 0 || first_cp == current) return current;
        current = first_cp;
    }

    return current;
}

void UCDParser::build_semantic_edges() {
    // Build edges from case mappings, decompositions, etc.
    for (auto& [cp, meta] : codepoints_) {
        // Case pair edges
        auto add_case_edge = [&](const std::string& mapping, const char* type) {
            if (mapping.empty() || mapping == "#") return;
            try {
                uint32_t target = std::stoul(mapping, nullptr, 16);
                if (target != cp && codepoints_.count(target)) {
                    SemanticEdge edge;
                    edge.target_cp = target;
                    edge.weight = static_cast<uint32_t>(EdgeWeight::CasePair);
                    edge.type = type;
                    meta.edges.push_back(edge);
                }
            } catch (...) {}
        };

        add_case_edge(meta.simple_uppercase, "uppercase");
        add_case_edge(meta.simple_lowercase, "lowercase");
        add_case_edge(meta.simple_titlecase, "titlecase");

        // Decomposition edges
        if (!meta.decomposition_mapping.empty() && meta.decomposition_mapping != "#") {
            size_t start = 0;
            while (start < meta.decomposition_mapping.length()) {
                size_t end = meta.decomposition_mapping.find(' ', start);
                if (end == std::string::npos) end = meta.decomposition_mapping.length();
                std::string part = meta.decomposition_mapping.substr(start, end - start);
                start = end + 1;

                if (part.empty() || part[0] == '<') continue;
                try {
                    uint32_t target = std::stoul(part, nullptr, 16);
                    if (target != cp && codepoints_.count(target)) {
                        SemanticEdge edge;
                        edge.target_cp = target;
                        edge.weight = (meta.decomposition_type == "can")
                            ? static_cast<uint32_t>(EdgeWeight::CanonicalDecomp)
                            : static_cast<uint32_t>(EdgeWeight::CompatibilityDecomp);
                        edge.type = "decomp_" + meta.decomposition_type;
                        meta.edges.push_back(edge);
                    }
                } catch (...) {}
            }
        }

        // Base character edge
        if (meta.base_codepoint != cp && meta.base_codepoint != 0 && codepoints_.count(meta.base_codepoint)) {
            SemanticEdge edge;
            edge.target_cp = meta.base_codepoint;
            edge.weight = static_cast<uint32_t>(EdgeWeight::CanonicalDecomp);
            edge.type = "base_character";
            meta.edges.push_back(edge);
        }
    }
}

} // namespace Hartonomous::unicode
