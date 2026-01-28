/**
 * @file ai_ops.cpp
 * @brief AI/ML Operations implementation
 */

#include <query/ai_ops.hpp>
#include <query/semantic_query.hpp>
#include <hashing/blake3_pipeline.hpp>

namespace Hartonomous {

AIOps::AIOps(PostgresConnection& db) : db_(db) {}

std::vector<TextGenerationResult> AIOps::generate_text(
    const std::string& prompt,
    const GenerationConfig& config
) {
    std::vector<TextGenerationResult> results;

    // Use semantic query to find relevant context
    SemanticQuery query(db_);
    auto related = query.find_related(prompt, config.max_tokens);

    for (const auto& result : related) {
        TextGenerationResult gen;
        gen.text = result.text;
        gen.confidence = result.confidence;
        gen.model_used = "hartonomous";
        gen.reasoning_path = extract_reasoning_path(BLAKE3Pipeline::to_hex(BLAKE3Pipeline::hash(prompt)));
        results.push_back(gen);

        if (results.size() >= config.num_results) break;
    }

    return results;
}

std::vector<TextGenerationResult> AIOps::complete_text(
    const std::string& prefix,
    const GenerationConfig& config
) {
    return generate_text(prefix, config);
}

std::string AIOps::answer_question(
    const std::string& question,
    const std::string& context
) {
    SemanticQuery query(db_);
    auto answer = query.answer_question(question);

    if (answer) {
        return answer->text;
    }

    return "Unable to answer";
}

std::vector<ImageGenerationResult> AIOps::generate_image(
    const std::string& prompt,
    int width,
    int height,
    const GenerationConfig& config
) {
    std::vector<ImageGenerationResult> results;

    // Placeholder: Would integrate with actual image generation models
    ImageGenerationResult result;
    result.width = width;
    result.height = height;
    result.format = "png";
    result.confidence = 0.0;
    result.prompt_used = prompt;
    results.push_back(result);

    return results;
}

std::vector<ImageGenerationResult> AIOps::edit_image(
    const std::vector<uint8_t>& image,
    const std::string& prompt,
    const GenerationConfig& config
) {
    return generate_image(prompt, 512, 512, config);
}

std::vector<AudioGenerationResult> AIOps::generate_audio(
    const std::string& prompt,
    int duration_ms,
    const GenerationConfig& config
) {
    std::vector<AudioGenerationResult> results;

    AudioGenerationResult result;
    result.sample_rate = 44100;
    result.duration_ms = duration_ms;
    result.format = "wav";
    result.confidence = 0.0;
    results.push_back(result);

    return results;
}

std::vector<AudioGenerationResult> AIOps::text_to_speech(
    const std::string& text,
    const std::string& voice_style
) {
    return generate_audio(text, 5000);
}

std::vector<VideoGenerationResult> AIOps::generate_video(
    const std::string& prompt,
    int duration_ms,
    const GenerationConfig& config
) {
    std::vector<VideoGenerationResult> results;

    VideoGenerationResult result;
    result.width = 512;
    result.height = 512;
    result.fps = 24;
    result.duration_ms = duration_ms;
    result.format = "mp4";
    result.confidence = 0.0;
    results.push_back(result);

    return results;
}

std::vector<VideoGenerationResult> AIOps::animate_image(
    const std::vector<uint8_t>& image,
    const std::string& motion_prompt
) {
    return generate_video(motion_prompt, 5000);
}

std::vector<std::string> AIOps::list_available_models(const std::string& modality) {
    std::vector<std::string> models;

    std::string sql = "SELECT DISTINCT value->>'model_name' FROM hartonomous.metadata WHERE key = 'model_info'";

    db_.query(sql, [&](const std::vector<std::string>& row) {
        models.push_back(row[0]);
    });

    return models;
}

void AIOps::set_default_model(const std::string& modality, const std::string& model_name) {
    db_.execute(
        "INSERT INTO hartonomous.metadata (hash, entity_type, key, value) VALUES ($1, 'config', 'default_model', jsonb_build_object('modality', $2, 'model', $3))",
        {BLAKE3Pipeline::to_hex(BLAKE3Pipeline::hash(modality)), modality, model_name}
    );
}

std::vector<AIOps::ModelStats> AIOps::get_model_statistics() {
    std::vector<ModelStats> stats;

    std::string sql = R"(
        SELECT
            value->>'model_name' as model,
            COUNT(*) as queries,
            AVG((value->>'confidence')::double precision) as avg_conf,
            AVG((value->>'rating')::int) as avg_rating
        FROM hartonomous.metadata
        WHERE key = 'model_query'
        GROUP BY value->>'model_name'
    )";

    db_.query(sql, [&](const std::vector<std::string>& row) {
        ModelStats stat;
        stat.model_name = row[0];
        stat.queries_served = std::stoi(row[1]);
        stat.avg_confidence = std::stod(row[2]);
        stat.avg_user_rating = std::stod(row[3]);
        stats.push_back(stat);
    });

    return stats;
}

std::string AIOps::find_relevant_context(const std::string& prompt) {
    SemanticQuery query(db_);
    auto comp = query.get_composition_info(prompt);

    if (comp) {
        return comp->text;
    }

    return "";
}

std::vector<std::string> AIOps::extract_reasoning_path(const std::string& query_hash) {
    std::vector<std::string> path;

    std::string sql = "SELECT value->>'steps' FROM hartonomous.metadata WHERE hash = $1 AND key = 'reasoning_trace'";

    db_.query(sql, {query_hash}, [&](const std::vector<std::string>& row) {
        path.push_back(row[0]);
    });

    return path;
}

double AIOps::calculate_consensus_confidence(const std::vector<std::string>& model_results) {
    if (model_results.empty()) return 0.0;

    // Simple consensus: more models agreeing = higher confidence
    return static_cast<double>(model_results.size()) / 10.0;
}

} // namespace Hartonomous
