#ifndef BOUNDARYLAB_RESPONSIBILITY_JAPANESE_ORACLE_H_
#define BOUNDARYLAB_RESPONSIBILITY_JAPANESE_ORACLE_H_

#include <string>
#include <string_view>
#include <vector>

namespace boundarylab {

struct JapaneseProbeResult {
  bool available = false;
  bool ok = false;
  std::string raw;
  std::string preedit;
  std::vector<std::string> candidates;
  double quality = 0.0;
  std::string error;

  const std::string* TopCandidate() const {
    return candidates.empty() ? nullptr : &candidates.front();
  }
};

class JapaneseOracle {
 public:
  virtual ~JapaneseOracle() = default;
  virtual JapaneseProbeResult Probe(std::string_view raw) = 0;
};

}  // namespace boundarylab

#endif  // BOUNDARYLAB_RESPONSIBILITY_JAPANESE_ORACLE_H_
