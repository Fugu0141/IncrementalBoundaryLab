#include "responsibility/mozc_japanese_oracle.h"

#include <algorithm>
#include <cstdint>
#include <string>
#include <string_view>
#include <vector>

#include "client/client.h"
#include "protocol/commands.pb.h"

namespace boundarylab {
namespace {

std::string PreeditText(const mozc::commands::Output& output) {
  std::string text;
  if (!output.has_preedit()) {
    return text;
  }
  for (int i = 0; i < output.preedit().segment_size(); ++i) {
    text += output.preedit().segment(i).value();
  }
  return text;
}

bool HasNonAscii(std::string_view value) {
  for (const unsigned char c : value) {
    if (c >= 0x80) {
      return true;
    }
  }
  return false;
}

double EstimateQuality(std::string_view raw, std::string_view preedit,
                       const std::vector<std::string>& candidates) {
  if (candidates.empty()) {
    return preedit.empty() ? 0.05 : 0.45;
  }

  const std::string& top = candidates.front();
  if (top == raw) {
    return 0.20;
  }

  if (HasNonAscii(top)) {
    return candidates.size() >= 2 ? 0.98 : 0.92;
  }

  if (top != preedit) {
    return 0.70;
  }

  return 0.58;
}

}  // namespace

MozcJapaneseOracle::MozcJapaneseOracle()
    : client_(mozc::client::ClientFactory::NewClient()) {
  if (client_) {
    client_->set_suppress_error_dialog(true);
  }
}

bool MozcJapaneseOracle::EnsureReady() {
  return client_ && client_->EnsureSession();
}

JapaneseProbeResult MozcJapaneseOracle::Probe(std::string_view raw_view) {
  JapaneseProbeResult result;
  result.available = EnsureReady();
  result.raw = std::string(raw_view);

  if (!result.available) {
    result.error = "Mozc session is not available";
    return result;
  }

  mozc::commands::Output output;

  mozc::commands::SessionCommand revert;
  revert.set_type(mozc::commands::SessionCommand::REVERT);
  client_->SendCommand(revert, &output);

  mozc::commands::SessionCommand on;
  on.set_type(mozc::commands::SessionCommand::TURN_ON_IME);
  on.set_composition_mode(mozc::commands::HIRAGANA);
  if (!client_->SendCommand(on, &output)) {
    result.error = "TURN_ON_IME failed";
    return result;
  }

  for (const unsigned char c : result.raw) {
    mozc::commands::KeyEvent key;
    key.set_key_code(static_cast<uint32_t>(c));
    key.set_mode(mozc::commands::HIRAGANA);
    key.set_activated(true);
    if (!client_->SendKey(key, &output)) {
      result.error = "SendKey failed";
      return result;
    }
  }

  result.preedit = PreeditText(output);

  mozc::commands::KeyEvent space;
  space.set_special_key(mozc::commands::KeyEvent::SPACE);
  space.set_mode(mozc::commands::HIRAGANA);
  space.set_activated(true);
  if (!client_->SendKey(space, &output)) {
    result.error = "conversion key failed";
    return result;
  }

  if (output.has_candidate_window()) {
    const auto& window = output.candidate_window();
    const int limit = std::min(window.candidate_size(), 12);
    for (int i = 0; i < limit; ++i) {
      result.candidates.push_back(window.candidate(i).value());
    }
  }

  if (result.candidates.empty() && output.has_preedit()) {
    const std::string converted = PreeditText(output);
    if (!converted.empty()) {
      result.candidates.push_back(converted);
    }
  }

  mozc::commands::SessionCommand cleanup;
  cleanup.set_type(mozc::commands::SessionCommand::REVERT);
  client_->SendCommand(cleanup, &output);

  result.ok = true;
  result.quality =
      EstimateQuality(result.raw, result.preedit, result.candidates);
  return result;
}

}  // namespace boundarylab
