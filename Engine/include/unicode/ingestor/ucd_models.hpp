#pragma once

#include <string>
#include <vector>
#include <array>
#include <cstdint>
#include <unordered_map>
#include <Eigen/Core>

namespace Hartonomous::unicode {

struct UCAWeights {
    uint32_t primary = 0;
    uint32_t secondary = 0;
    uint32_t tertiary = 0;
    uint32_t quaternary = 0;
};

struct SemanticEdge {
    uint32_t target_cp;
    uint32_t weight;
    std::string type;
};

struct NameAlias {
    std::string alias;
    std::string type;  // abbreviation, control, figment, alternate, correction
};

struct CodepointMetadata {
    uint32_t codepoint = 0;

    // Core properties from UCD XML
    std::string name;                    // na
    std::string name1;                   // na1 (Unicode 1.0 name)
    std::string general_category;        // gc
    uint8_t combining_class = 0;         // ccc
    std::string script;                  // sc
    std::string script_extensions;       // scx (semicolon-separated)
    std::string block;                   // blk
    std::string age;                     // age

    // Decomposition
    std::string decomposition_type;      // dt
    std::string decomposition_mapping;   // dm

    // Case mappings
    std::string uppercase_mapping;       // uc
    std::string lowercase_mapping;       // lc
    std::string titlecase_mapping;       // tc
    std::string simple_uppercase;        // suc
    std::string simple_lowercase;        // slc
    std::string simple_titlecase;        // stc
    std::string simple_case_folding;     // scf
    std::string case_folding;            // cf

    // Numeric
    std::string numeric_type;            // nt
    std::string numeric_value;           // nv

    // Bidi
    std::string bidi_class;              // bc
    std::string bidi_paired_bracket_type; // bpt
    std::string bidi_paired_bracket;     // bpb
    std::string bidi_mirroring_glyph;    // bmg
    bool bidi_mirrored = false;          // Bidi_M
    bool bidi_control = false;           // Bidi_C

    // Joining
    std::string joining_type;            // jt
    std::string joining_group;           // jg
    bool join_control = false;           // Join_C

    // East Asian Width
    std::string east_asian_width;        // ea

    // Line/Word/Sentence/Grapheme breaking
    std::string line_break;              // lb
    std::string word_break;              // WB
    std::string sentence_break;          // SB
    std::string grapheme_cluster_break;  // GCB
    std::string indic_syllabic_category; // InSC
    std::string indic_positional_category; // InPC
    std::string vertical_orientation;    // vo

    // Hangul
    std::string hangul_syllable_type;    // hst
    std::string jamo_short_name;         // JSN

    // Boolean properties (packed for efficiency)
    bool is_alphabetic = false;          // Alpha
    bool is_uppercase = false;           // Upper
    bool is_lowercase = false;           // Lower
    bool is_cased = false;               // Cased
    bool is_math = false;                // Math
    bool is_hex_digit = false;           // Hex
    bool is_ascii_hex_digit = false;     // AHex
    bool is_ideographic = false;         // Ideo
    bool is_unified_ideograph = false;   // UIdeo
    bool is_radical = false;             // Radical
    bool is_dash = false;                // Dash
    bool is_whitespace = false;          // WSpace
    bool is_quotation_mark = false;      // QMark
    bool is_terminal_punctuation = false; // Term
    bool is_sentence_terminal = false;   // STerm
    bool is_diacritic = false;           // Dia
    bool is_extender = false;            // Ext
    bool is_soft_dotted = false;         // SD
    bool is_deprecated = false;          // Dep
    bool is_default_ignorable = false;   // DI
    bool is_variation_selector = false;  // VS
    bool is_noncharacter = false;        // NChar
    bool is_pattern_whitespace = false;  // Pat_WS
    bool is_pattern_syntax = false;      // Pat_Syn
    bool is_grapheme_base = false;       // Gr_Base
    bool is_grapheme_extend = false;     // Gr_Ext
    bool is_id_start = false;            // IDS
    bool is_id_continue = false;         // IDC
    bool is_xid_start = false;           // XIDS
    bool is_xid_continue = false;        // XIDC
    bool composition_exclusion = false;  // CE
    bool full_composition_exclusion = false; // Comp_Ex
    bool changes_when_lowercased = false; // CWL
    bool changes_when_uppercased = false; // CWU
    bool changes_when_titlecased = false; // CWT
    bool changes_when_casefolded = false; // CWCF
    bool changes_when_casemapped = false; // CWCM
    bool changes_when_nfkc_casefolded = false; // CWKCF
    bool prepended_concatenation_mark = false; // PCM
    bool regional_indicator = false;     // RI

    // Emoji properties
    bool is_emoji = false;               // Emoji
    bool is_emoji_presentation = false;  // EPres
    bool is_emoji_modifier = false;      // EMod
    bool is_emoji_modifier_base = false; // EBase
    bool is_emoji_component = false;     // EComp
    bool is_extended_pictographic = false; // ExtPict

    // Normalization
    std::string nfc_quick_check;         // NFC_QC
    std::string nfd_quick_check;         // NFD_QC
    std::string nfkc_quick_check;        // NFKC_QC
    std::string nfkd_quick_check;        // NFKD_QC
    std::string nfkc_casefold;           // NFKC_CF
    std::string nfkc_simple_casefold;    // NFKC_SCF

    // Name aliases
    std::vector<NameAlias> name_aliases;

    // Han Radical/Stroke
    uint32_t radical = 0;
    int32_t strokes = 0;

    // Semantic clustering
    uint32_t base_codepoint = 0;

    // UCA Weights from DUCET
    std::vector<UCAWeights> uca_elements;

    // Graph Adjacency
    std::vector<SemanticEdge> edges;

    // Semantic Buckets (for 1D Sequence)
    uint32_t primary_group = 0;
    uint32_t script_group = 0;

    // Deterministic Sequence Index
    uint32_t sequence_index = 0;

    // 4D Embedding on S3
    Eigen::Vector4d position = Eigen::Vector4d::Zero();

    // Raw XML attributes for any properties not explicitly modeled
    std::unordered_map<std::string, std::string> extra_properties;
};

// Weight Tiers for the Semantic Graph
enum class EdgeWeight : uint32_t {
    CasePair = 100,
    CanonicalDecomp = 95,
    UCAPrimary = 90,
    UCASecondary = 85,
    Confusable = 80,
    ScriptAdjacency = 70,
    RadicalStroke = 65,
    EmojiZWJ = 60,
    NumericAdjacency = 50,
    BlockAdjacency = 40,
    CompatibilityDecomp = 30,
    Default = 1
};

} // namespace Hartonomous::unicode
