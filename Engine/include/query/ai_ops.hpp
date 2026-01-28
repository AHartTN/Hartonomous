/**
 * @file ai_ops.hpp
 * @brief AI/ML Operations Query Interface
 *
 * Unified query interface for AI model operations:
 * - Text generation (LLMs)
 * - Image generation (Diffusion models)
 * - Audio generation (Audio models)
 * - Video generation (Video models)
 *
 * All queries go through the semantic graph to leverage
 * knowledge composition and model consensus.
 */

#pragma once

#include <database/postgres_connection.hpp>
#include <vector>
#include <string>
#include <optional>
#include <map>

namespace Hartonomous {

struct GenerationConfig {
    int max_tokens = 100;
    double temperature = 0.7;
    double top_p = 0.9;
    int num_results = 1;
    std::string model_preference;  // Optional: prefer specific model
};

struct TextGenerationResult {
    std::string text;
    double confidence;
    std::vector<std::string> reasoning_path;  // Chain of thought
    std::string model_used;
};

struct ImageGenerationResult {
    std::vector<uint8_t> image_data;
    int width;
    int height;
    std::string format;  // "png", "jpeg", etc.
    double confidence;
    std::string prompt_used;
};

struct AudioGenerationResult {
    std::vector<uint8_t> audio_data;
    int sample_rate;
    int duration_ms;
    std::string format;  // "wav", "mp3", etc.
    double confidence;
};

struct VideoGenerationResult {
    std::vector<uint8_t> video_data;
    int width;
    int height;
    int fps;
    int duration_ms;
    std::string format;  // "mp4", etc.
    double confidence;
};

/**
 * @brief AI/ML Operations Query Interface
 *
 * Provides unified interface for querying AI models through
 * the semantic graph. All generation uses composition and
 * consensus from stored model knowledge.
 */
class AIOps {
public:
    explicit AIOps(PostgresConnection& db);

    // Text Generation
    std::vector<TextGenerationResult> generate_text(
        const std::string& prompt,
        const GenerationConfig& config = {}
    );

    std::vector<TextGenerationResult> complete_text(
        const std::string& prefix,
        const GenerationConfig& config = {}
    );

    std::string answer_question(
        const std::string& question,
        const std::string& context = ""
    );

    // Image Generation
    std::vector<ImageGenerationResult> generate_image(
        const std::string& prompt,
        int width = 512,
        int height = 512,
        const GenerationConfig& config = {}
    );

    std::vector<ImageGenerationResult> edit_image(
        const std::vector<uint8_t>& image,
        const std::string& prompt,
        const GenerationConfig& config = {}
    );

    // Audio Generation
    std::vector<AudioGenerationResult> generate_audio(
        const std::string& prompt,
        int duration_ms = 5000,
        const GenerationConfig& config = {}
    );

    std::vector<AudioGenerationResult> text_to_speech(
        const std::string& text,
        const std::string& voice_style = "neutral"
    );

    // Video Generation
    std::vector<VideoGenerationResult> generate_video(
        const std::string& prompt,
        int duration_ms = 5000,
        const GenerationConfig& config = {}
    );

    std::vector<VideoGenerationResult> animate_image(
        const std::vector<uint8_t>& image,
        const std::string& motion_prompt
    );

    // Model Management
    std::vector<std::string> list_available_models(const std::string& modality = "all");
    void set_default_model(const std::string& modality, const std::string& model_name);

    // Performance
    struct ModelStats {
        std::string model_name;
        int queries_served;
        double avg_confidence;
        double avg_user_rating;
    };
    std::vector<ModelStats> get_model_statistics();

private:
    PostgresConnection& db_;

    // Internal helpers
    std::string find_relevant_context(const std::string& prompt);
    std::vector<std::string> extract_reasoning_path(const std::string& query_hash);
    double calculate_consensus_confidence(const std::vector<std::string>& model_results);
};

} // namespace Hartonomous
