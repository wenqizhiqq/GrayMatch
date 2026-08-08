#pragma once

#include <cstdint>

#ifdef _WIN32
  #ifdef GRAYMODEL_EXPORTS
    #define GRAYMODEL_API __declspec(dllexport)
  #else
    #define GRAYMODEL_API __declspec(dllimport)
  #endif
#else
  #define GRAYMODEL_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

struct GmMatchResult {
    double score;
    double centerX;
    double centerY;
    double angle;
    int templateWidth;
    int templateHeight;
    int leftTopX;
    int leftTopY;
    int level;
};

// Opaque handle to the matcher instance.
GRAYMODEL_API void* gm_create();

// Releases the matcher instance.
GRAYMODEL_API void gm_destroy(void* handle);

// Sets the source (scene) image. data points to a row-major pixel buffer.
// channels: 1 (grayscale) or 3 (BGR). step: row stride in bytes.
GRAYMODEL_API int gm_set_source(void* handle, const unsigned char* data,
                                int w, int h, int step, int channels);

// Sets the template image (grayscale or BGR, like gm_set_source).
GRAYMODEL_API int gm_set_template(void* handle, const unsigned char* data,
                                  int w, int h, int step, int channels);

// Runs rotation-invariant NCC matching.
// matchMode: 0 = grayscale NCC (raw intensity), 1 = shape NCC (Sobel edge map).
// Results are written into outResults (up to maxResults). Returns the number written.
// Returns -1 if source/template not set.
GRAYMODEL_API int gm_match(void* handle,
                           int pyramidLevels,
                           double angleStart,
                           double angleEnd,
                           double angleStep,
                           double nccThreshold,
                           double maxOverlap,
                           int topN,
                           int matchMode,
                           GmMatchResult* outResults,
                           int maxResults);

// Returns the pure matching time (milliseconds) of the last gm_match call,
// EXCLUDING template-cache construction (rotated-template warps). This is the
// figure the UI should report so template creation / drawing aren't counted.
GRAYMODEL_API double gm_get_last_match_ms(void* handle);

#ifdef __cplusplus
}
#endif
