#include "responsibility/responsibility_runtime.h"

#include <algorithm>
#include <cctype>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace boundarylab {

ResponsibilityRuntime::ResponsibilityRuntime(ResponsibilityDecoder* decoder)
    : decoder_(decoder) {}

ResponsibilityRuntimeUpdate ResponsibilityRuntime::Push(char c) {
  pending_raw_.push_back(c);
  return Drain();
}

ResponsibilityRuntimeUpdate ResponsibilityRuntime::Push(
    std::string_view text) {
  ResponsibilityRuntimeUpdate combined;
  for (const char c : text) {
    ResponsibilityRuntimeUpdate step = Push(c);
    combined.flushes.insert(
        combined.flushes.end(),
        std::make_move_iterator(step.flushes.begin()),
        std::make_move_iterator(step.flushes.end()));
  }

  combined.pending_raw = pending_raw_;
  if (decoder_ != nullptr) {
    combined.pending_analysis = decoder_->Analyze(pending_raw_);
  }
  return combined;
}

ResponsibilityRuntimeUpdate ResponsibilityRuntime::Backspace() {
  if (!pending_raw_.empty()) {
    pending_raw_.pop_back();
  }
  return Drain();
}

ResponsibilityRuntimeUpdate ResponsibilityRuntime::ClosePending() {
  ResponsibilityRuntimeUpdate update = Drain();
  if (pending_raw_.empty() || decoder_ == nullptr) {
    update.pending_raw = pending_raw_;
    return update;
  }

  const ResponsibilityAnalysis analysis = decoder_->Analyze(pending_raw_);
  Responsibility responsibility = Responsibility::kJapanese;
  std::string evidence = "command-boundary-japanese-close";

  if (!analysis.spans.empty()) {
    const ResponsibilitySpan& first = analysis.spans.front();
    if (first.responsibility == Responsibility::kLiteral ||
        first.responsibility == Responsibility::kOpen) {
      responsibility = Responsibility::kLiteral;
      evidence = "command-boundary-literal-close";
    }
  }

  update.flushes.push_back(ResponsibilityFlush{
      .raw = pending_raw_,
      .responsibility = responsibility,
      .evidence = evidence,
  });
  pending_raw_.clear();
  update.pending_raw.clear();
  update.pending_analysis = ResponsibilityAnalysis{};
  return update;
}

void ResponsibilityRuntime::Reset() {
  pending_raw_.clear();
}

bool ResponsibilityRuntime::CanSpeculativelyFlushJapanese(
    const ResponsibilitySpan& span) const {
  if (span.responsibility != Responsibility::kJapanese ||
      span.stable ||
      span.start != 0 ||
      span.end != pending_raw_.size()) {
    return false;
  }

  // Binding symbols can still flip the ownership of the whole token after
  // more input arrives (de-ta vs node-core), so never speculative-flush them.
  if (std::any_of(
          pending_raw_.begin(), pending_raw_.end(),
          ResponsibilityDecoder::IsBindingSymbol)) {
    return false;
  }

  return true;
}

ResponsibilityRuntimeUpdate ResponsibilityRuntime::Drain() {
  ResponsibilityRuntimeUpdate update;

  if (decoder_ == nullptr) {
    update.pending_raw = pending_raw_;
    return update;
  }

  while (!pending_raw_.empty()) {
    ResponsibilityAnalysis analysis = decoder_->Analyze(pending_raw_);
    if (analysis.spans.empty()) {
      break;
    }

    const ResponsibilitySpan& first = analysis.spans.front();
    const bool flushable =
        first.stable &&
        (first.responsibility == Responsibility::kJapanese ||
         first.responsibility == Responsibility::kLiteral);

    if (flushable) {
      update.flushes.push_back(ResponsibilityFlush{
          .raw = pending_raw_.substr(0, first.end),
          .responsibility = first.responsibility,
          .evidence = first.evidence,
      });
      pending_raw_.erase(0, first.end);
      continue;
    }

    if (CanSpeculativelyFlushJapanese(first)) {
      // The decoder has already ruled out an active English prefix for this
      // entire pending token. Flush all of it to Mozc immediately. Future
      // English ambiguity starts from the next character and remains local.
      update.flushes.push_back(ResponsibilityFlush{
          .raw = pending_raw_,
          .responsibility = Responsibility::kJapanese,
          .evidence = "japanese-default-immediate-flush",
      });
      pending_raw_.clear();
      continue;
    }

    break;
  }

  update.pending_raw = pending_raw_;
  update.pending_analysis = decoder_->Analyze(pending_raw_);
  return update;
}

}  // namespace boundarylab
