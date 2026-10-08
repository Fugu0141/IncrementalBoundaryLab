#include "responsibility/responsibility_output.h"

#include "protocol/commands.pb.h"
#include "testing/gunit.h"

namespace boundarylab {
namespace {

TEST(ResponsibilityOutputTest, OpenUsesUnderlinedRawPreedit) {
  const mozc::commands::Output output = MakeOpenPreedit("commi");

  EXPECT_TRUE(output.consumed());
  ASSERT_TRUE(output.has_preedit());
  EXPECT_FALSE(output.has_result());
  EXPECT_EQ(output.preedit().cursor(), 5);

  ASSERT_EQ(output.preedit().segment_size(), 1);
  const auto& segment = output.preedit().segment(0);
  EXPECT_EQ(segment.annotation(), mozc::commands::Preedit::Segment::UNDERLINE);
  EXPECT_EQ(segment.value(), "commi");
  EXPECT_EQ(segment.value_length(), 5);
  EXPECT_EQ(segment.key(), "commi");
}

TEST(ResponsibilityOutputTest, LiteralUsesMozcResultCommit) {
  const mozc::commands::Output output = MakeLiteralCommit("node.js");

  EXPECT_TRUE(output.consumed());
  EXPECT_FALSE(output.has_preedit());
  ASSERT_TRUE(output.has_result());
  EXPECT_EQ(output.result().type(), mozc::commands::Result::STRING);
  EXPECT_EQ(output.result().value(), "node.js");
  EXPECT_EQ(output.result().key(), "node.js");
}

}  // namespace
}  // namespace boundarylab
