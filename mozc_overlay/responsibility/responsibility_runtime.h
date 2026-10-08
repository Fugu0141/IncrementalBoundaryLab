#ifndef BOUNDARYLAB_RESPONSIBILITY_RESPONSIBILITY_RUNTIME_H_
#define BOUNDARYLAB_RESPONSIBILITY_RESPONSIBILITY_RUNTIME_H_

#include <string>
#include <string_view>
#include <vector>

#include "responsibility/responsibility_decoder.h"

namespace boundarylab {

struct ResponsibilityFlush {
  std::string raw;
  Responsibility responsibility = Responsibility::kUnknown;
  std::string evidence;
};

struct ResponsibilityRuntimeUpdate {
  std::vector<ResponsibilityFlush> flushes;
  std::string pending_raw;
  ResponsibilityAnalysis pending_analysis;
};

class ResponsibilityRuntime {
 public:
  explicit ResponsibilityRuntime(ResponsibilityDecoder* decoder);

  ResponsibilityRuntimeUpdate Push(char c);
  ResponsibilityRuntimeUpdate Push(std::string_view text);
  ResponsibilityRuntimeUpdate Backspace();
  ResponsibilityRuntimeUpdate ClosePending();
  void Reset();

  const std::string& pending_raw() const { return pending_raw_; }

 private:
  ResponsibilityRuntimeUpdate Drain();
  bool CanSpeculativelyFlushJapanese(
      const ResponsibilitySpan& span) const;

  ResponsibilityDecoder* decoder_;
  std::string pending_raw_;
};

}  // namespace boundarylab

#endif  // BOUNDARYLAB_RESPONSIBILITY_RESPONSIBILITY_RUNTIME_H_
