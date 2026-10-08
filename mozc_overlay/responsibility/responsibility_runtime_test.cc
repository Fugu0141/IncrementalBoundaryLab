#include "responsibility/responsibility_runtime.h"

#include <string>
#include <string_view>
#include <unordered_map>

#include "responsibility/japanese_oracle.h"
#include "testing/gunit.h"

namespace boundarylab {
namespace {

class RuntimeFakeOracle final : public JapaneseOracle {
 public:
  JapaneseProbeResult Probe(std::string_view raw) override {
    JapaneseProbeResult result;
    result.available = true;
    result.ok = true;
    result.raw = std::string(raw);

    if (raw == "de-ta" || raw == "de-tawo") {
      result.preedit = "データ";
      result.candidates = {"データ", std::string(raw)};
      result.quality = 0.98;
    } else {
      result.preedit = result.raw;
      result.candidates = {result.raw};
      result.quality = 0.20;
    }

    return result;
  }
};

TEST(ResponsibilityRuntimeTest, DoesNotFlushIncompleteCommitPrefix) {
  RuntimeFakeOracle oracle;
  ResponsibilityDecoder decoder(&oracle);
  ResponsibilityRuntime runtime(&decoder);

  const ResponsibilityRuntimeUpdate update = runtime.Push("commi");
  EXPECT_TRUE(update.flushes.empty());
  EXPECT_EQ(update.pending_raw, "commi");
}

TEST(ResponsibilityRuntimeTest, FlushesCommitAfterJapaneseBoundaryEvidence) {
  RuntimeFakeOracle oracle;
  ResponsibilityDecoder decoder(&oracle);
  ResponsibilityRuntime runtime(&decoder);

  const ResponsibilityRuntimeUpdate update = runtime.Push("commitha");

  ASSERT_FALSE(update.flushes.empty());
  EXPECT_EQ(update.flushes[0].raw, "commit");
  EXPECT_EQ(update.flushes[0].responsibility, Responsibility::kLiteral);
  EXPECT_EQ(runtime.pending_raw().find("commit"), std::string::npos);
}

TEST(ResponsibilityRuntimeTest, StreamsJapaneseWithSmallLookahead) {
  RuntimeFakeOracle oracle;
  ResponsibilityDecoder decoder(&oracle);
  ResponsibilityRuntime runtime(&decoder);

  const ResponsibilityRuntimeUpdate update = runtime.Push("nihongo");

  std::string flushed;
  for (const ResponsibilityFlush& span : update.flushes) {
    EXPECT_EQ(span.responsibility, Responsibility::kJapanese);
    flushed += span.raw;
  }

  EXPECT_EQ(flushed + update.pending_raw, "nihongo");
  EXPECT_LE(update.pending_raw.size(), 1);
}

TEST(ResponsibilityRuntimeTest, DoesNotSpeculateAcrossBindingSymbol) {
  RuntimeFakeOracle oracle;
  ResponsibilityDecoder decoder(&oracle);
  ResponsibilityRuntime runtime(&decoder);

  const ResponsibilityRuntimeUpdate update = runtime.Push("de-ta");

  std::string flushed;
  for (const ResponsibilityFlush& span : update.flushes) {
    flushed += span.raw;
  }

  EXPECT_EQ(flushed + update.pending_raw, "de-ta");
  // The binding token must not be chopped by the one-char Japanese lookahead
  // optimization. If Mozc owns it, it stays whole.
  for (const ResponsibilityFlush& span : update.flushes) {
    EXPECT_NE(span.raw, "de-t");
  }
}

TEST(ResponsibilityRuntimeTest, BackspaceOnlyTouchesLocalPendingSuffix) {
  RuntimeFakeOracle oracle;
  ResponsibilityDecoder decoder(&oracle);
  ResponsibilityRuntime runtime(&decoder);

  runtime.Push("commi");
  const ResponsibilityRuntimeUpdate update = runtime.Backspace();

  EXPECT_EQ(update.pending_raw, "comm");
  EXPECT_TRUE(update.flushes.empty());
}

}  // namespace
}  // namespace boundarylab
