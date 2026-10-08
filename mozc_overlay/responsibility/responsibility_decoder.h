#ifndef BOUNDARYLAB_RESPONSIBILITY_RESPONSIBILITY_DECODER_H_
#define BOUNDARYLAB_RESPONSIBILITY_RESPONSIBILITY_DECODER_H_

#include <cstddef>
#include <string>
#include <string_view>
#include <vector>

#include "responsibility/japanese_oracle.h"

namespace boundarylab {

enum class Responsibility {
  kOpen,
  kJapanese,
  kLiteral,
  kBoundary,
  kUnknown,
};

struct ResponsibilitySpan {
  std::size_t start = 0;
  std::size_t end = 0;
  std::string raw;
  Responsibility responsibility = Responsibility::kUnknown;
  bool stable = false;
  std::string evidence;
  double mozc_quality = -1.0;
  std::string mozc_top_candidate;
};

struct ResponsibilityAnalysis {
  std::string raw;
  std::vector<ResponsibilitySpan> spans;
};

class ResponsibilityDecoder {
 public:
  explicit ResponsibilityDecoder(JapaneseOracle* oracle = nullptr);

  ResponsibilityAnalysis Analyze(std::string_view raw);

  static bool IsBindingSymbol(char c);
  static bool IsHardBoundary(char c);
  bool PrefersJapaneseAtCommandBoundary(std::string_view raw) const;

 private:
  struct LiteralCandidate {
    std::size_t end = 0;
    bool structural = false;
    std::string evidence;
  };

  std::string Normalize(std::string_view raw) const;
  bool IsEnglishExact(std::string_view raw) const;
  bool IsEnglishPrefix(std::string_view raw) const;
  bool IsJapaneseContinuation(std::string_view raw) const;
  LiteralCandidate FindLiteralAt(std::string_view raw,
                                 std::size_t start) const;
  std::size_t FindNextLiteralAnchor(std::string_view raw,
                                    std::size_t start) const;
  ResponsibilitySpan MakeJapaneseSpan(std::string_view raw,
                                      std::size_t start,
                                      std::size_t end,
                                      bool stable);

  JapaneseOracle* oracle_;
};

const char* ResponsibilityName(Responsibility responsibility);

}  // namespace boundarylab

#endif  // BOUNDARYLAB_RESPONSIBILITY_RESPONSIBILITY_DECODER_H_
