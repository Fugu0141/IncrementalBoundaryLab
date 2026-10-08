#include "responsibility/responsibility_output.h"

#include <cstdint>
#include <string>
#include <string_view>

#include "protocol/commands.pb.h"

namespace boundarylab {

mozc::commands::Output MakeOpenPreedit(std::string_view raw) {
  mozc::commands::Output output;
  output.set_consumed(true);

  mozc::commands::Preedit* preedit = output.mutable_preedit();
  preedit->set_cursor(static_cast<uint32_t>(raw.size()));

  mozc::commands::Preedit::Segment* segment = preedit->add_segment();
  segment->set_annotation(mozc::commands::Preedit::Segment::UNDERLINE);
  segment->set_value(std::string(raw));
  segment->set_value_length(static_cast<uint32_t>(raw.size()));
  segment->set_key(std::string(raw));

  return output;
}

mozc::commands::Output MakeLiteralCommit(std::string_view raw) {
  mozc::commands::Output output;
  output.set_consumed(true);

  mozc::commands::Result* result = output.mutable_result();
  result->set_type(mozc::commands::Result::STRING);
  result->set_value(std::string(raw));
  result->set_key(std::string(raw));

  return output;
}

}  // namespace boundarylab
