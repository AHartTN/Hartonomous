#include "ml/s3_hnsw.hpp"
#include <hnswlib/hnswlib.h>
#include <vector>
#include <mutex>

namespace s3::ann
{
    struct HnswIndexHandle {
        hnswlib::L2Space* space;
        hnswlib::HierarchicalNSW<float>* index;
        int dim;

        HnswIndexHandle(int d, size_t max_elements) : dim(d) {
            space = new hnswlib::L2Space(d);
            index = new hnswlib::HierarchicalNSW<float>(space, max_elements, 16, 200);
        }

        ~HnswIndexHandle() {
            delete index;
            delete space;
        }
    };

    HnswIndexHandle* build_index(const std::vector<Vec4>& points)
    {
        if (points.empty()) return nullptr;

        size_t n = points.size();
        HnswIndexHandle* h = new HnswIndexHandle(4, n);

        #pragma omp parallel for schedule(dynamic, 1024)
        for (size_t i = 0; i < n; ++i) {
            float data[4] = {
                static_cast<float>(points[i][0]),
                static_cast<float>(points[i][1]),
                static_cast<float>(points[i][2]),
                static_cast<float>(points[i][3])
            };
            h->index->addPoint(data, i);
        }

        return h;
    }

    void free_index(HnswIndexHandle* h)
    {
        delete h;
    }

    std::vector<std::pair<int, double>> query_index(HnswIndexHandle* h, const Vec4& q, int k)
    {
        if (!h) return {};

        float query_data[4] = {
            static_cast<float>(q[0]),
            static_cast<float>(q[1]),
            static_cast<float>(q[2]),
            static_cast<float>(q[3])
        };

        auto result_pq = h->index->searchKnn(query_data, k);
        std::vector<std::pair<int, double>> results;
        results.reserve(result_pq.size());

        while (!result_pq.empty()) {
            auto& top = result_pq.top();
            results.push_back({static_cast<int>(top.second), static_cast<double>(top.first)});
            result_pq.pop();
        }

        // searchKnn returns results in a priority queue, often furthest first. 
        // We reverse to get nearest first for the caller.
        std::reverse(results.begin(), results.end());
        return results;
    }
}