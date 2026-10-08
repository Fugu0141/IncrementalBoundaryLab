#include "responsibility/responsibility_decoder.h"

#include <string>
#include <string_view>
#include <unordered_map>
#include <utility>

#include "testing/gunit.h"

namespace boundarylab {
namespace {

class FakeOracle final : public JapaneseOracle {
 public:
  FakeOracle() {
    conversions_.emplace("de-ta", "データ");
  }

  JapaneseProbeResult Probe(std::string_view raw) override {
    JapaneseProbeResult result;
    result.available = true;
    result.ok = true;
    result.raw = std::string(raw);

    const auto it = conversions_.find(result.raw);
    if (it != conversions_.end()) {
      result.preedit = it->second;
      result.candidates = {it->second, result.raw};
      result.quality = 0.98;
      return result;
    }

    result.preedit = result.raw;
    result.candidates = {result.raw};
    result.quality = 0.20;
    return result;
  }

 private:
  std::unordered_map<std::string, std::string> conversions_;
};

TEST(ResponsibilityDecoderTest, KeepsIncompleteEnglishPrefixOpen) {
  FakeOracle oracle;
  ResponsibilityDecoder decoder(&oracle);
  const ResponsibilityAnalysis analysis = decoder.Analyze("commi");

  ASSERT_EQ(analysis.spans.size(), 1);
  EXPECT_EQ(analysis.spans[0].responsibility, Responsibility::kOpen);
  EXPECT_FALSE(analysis.spans[0].stable);
  EXPECT_EQ(analysis.spans[0].raw, "commi");
}

TEST(ResponsibilityDecoderTest, SplitsCommitFromJapaneseContinuation) {
  FakeOracle oracle;
  ResponsibilityDecoder decoder(&oracle);
  const ResponsibilityAnalysis analysis = decoder.Analyze("commitha");

  ASSERT_EQ(analysis.spans.size(), 2);
  EXPECT_EQ(analysis.spans[0].raw, "commit");
  EXPECT_EQ(analysis.spans[0].responsibility, Responsibility::kLiteral);
  EXPECT_TRUE(analysis.spans[0].stable);
  EXPECT_EQ(analysis.spans[1].raw, "ha");
  EXPECT_EQ(analysis.spans[1].responsibility, Responsibility::kJapanese);
}

TEST(ResponsibilityDecoderTest, LetsMozcOwnJapaneseHyphenReading) {
  FakeOracle oracle;
  ResponsibilityDecoder decoder(&oracle);
  const ResponsibilityAnalysis analysis = decoder.Analyze("de-ta");

  ASSERT_EQ(analysis.spans.size(), 1);
  EXPECT_EQ(analysis.spans[0].responsibility, Responsibility::kJapanese);
  EXPECT_EQ(analysis.spans[0].evidence, "mozc-japanese");
  EXPECT_GE(analysis.spans[0].mozc_quality, 0.9);
  EXPECT_EQ(analysis.spans[0].mozc_top_candidate, "データ");
}

TEST(ResponsibilityDecoderTest, KeepsNodeJsLiteral) {
  FakeOracle oracle;
  ResponsibilityDecoder decoder(&oracle);
  const ResponsibilityAnalysis analysis = decoder.Analyze("node.js");

  ASSERT_EQ(analysis.spans.size(), 1);
  EXPECT_EQ(analysis.spans[0].raw, "node.js");
  EXPECT_EQ(analysis.spans[0].responsibility, Responsibility::kLiteral);
  EXPECT_EQ(analysis.spans[0].evidence, "structural-literal");
}

TEST(ResponsibilityDecoderTest, FindsLiteralAnchorsInsideMixedSentence) {
  FakeOracle oracle;
  ResponsibilityDecoder decoder(&oracle);
  const ResponsibilityAnalysis analysis =
      decoder.Analyze("commitsitade-tawogithubnipushsitekudasai");

  ASSERT_GE(analysis.spans.size(), 5);

  EXPECT_EQ(analysis.spans[0].raw, "commit");
  EXPECT_EQ(analysis.spans[0].responsibility, Responsibility::kLiteral);

  bool saw_github = false;
  bool saw_push = false;
  for (const ResponsibilitySpan& span : analysis.spans) {
    if (span.raw == "github") {
      saw_github = true;
      EXPECT_EQ(span.responsibility, Responsibility::kLiteral);
    }
    if (span.raw == "push") {
      saw_push = true;
      EXPECT_EQ(span.responsibility, Responsibility::kLiteral);
    }
  }

  EXPECT_TRUE(saw_github);
  EXPECT_TRUE(saw_push);
}

TEST(ResponsibilityDecoderTest, HardBoundaryClosesOpenToken) {
  FakeOracle oracle;
  ResponsibilityDecoder decoder(&oracle);
  const ResponsibilityAnalysis analysis = decoder.Analyze("commi ");

  ASSERT_EQ(analysis.spans.size(), 2);
  EXPECT_EQ(analysis.spans[0].raw, "commi");
  EXPECT_EQ(analysis.spans[0].responsibility, Responsibility::kJapanese);
  EXPECT_TRUE(analysis.spans[0].stable);
  EXPECT_EQ(analysis.spans[1].responsibility, Responsibility::kBoundary);
}

}  // namespace
}  // namespace boundarylab
