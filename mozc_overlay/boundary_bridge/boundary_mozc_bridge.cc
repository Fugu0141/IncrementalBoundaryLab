// BoundaryLab Mozc bridge.
// This file is copied into a pinned Mozc source checkout by setup_mozc_bridge.ps1.
// It intentionally uses Mozc's public client/session protocol instead of
// duplicating Mozc conversion logic.

#include <cstdint>
#include <iostream>
#include <memory>
#include <string>
#include <vector>

#include "client/client.h"
#include "client/client_interface.h"
#include "protocol/commands.pb.h"

namespace {

std::string JsonEscape(const std::string& input) {
  std::string out;
  out.reserve(input.size() + 16);
  for (const unsigned char c : input) {
    switch (c) {
      case '\\': out += "\\\\"; break;
      case '"': out += "\\\""; break;
      case '\n': out += "\\n"; break;
      case '\r': out += "\\r"; break;
      case '\t': out += "\\t"; break;
      default:
        if (c < 0x20) {
          const char hex[] = "0123456789abcdef";
          out += "\\u00";
          out += hex[(c >> 4) & 0xf];
          out += hex[c & 0xf];
        } else {
          out.push_back(static_cast<char>(c));
        }
    }
  }
  return out;
}

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

class MozcProbeEngine {
 public:
  MozcProbeEngine()
      : client_(mozc::client::ClientFactory::NewClient()) {
    client_->set_suppress_error_dialog(true);
  }

  bool Ready() {
    return client_ && client_->EnsureSession();
  }

  bool Probe(const std::string& raw,
             std::string* preedit,
             std::vector<std::string>* candidates,
             std::string* error) {
    if (!Ready()) {
      *error = "Mozc session is not available";
      return false;
    }

    mozc::commands::Output output;

    mozc::commands::SessionCommand revert;
    revert.set_type(mozc::commands::SessionCommand::REVERT);
    client_->SendCommand(revert, &output);

    mozc::commands::SessionCommand on;
    on.set_type(mozc::commands::SessionCommand::TURN_ON_IME);
    on.set_composition_mode(mozc::commands::HIRAGANA);
    if (!client_->SendCommand(on, &output)) {
      *error = "TURN_ON_IME failed";
      return false;
    }

    for (const unsigned char c : raw) {
      mozc::commands::KeyEvent key;
      key.set_key_code(static_cast<uint32_t>(c));
      key.set_mode(mozc::commands::HIRAGANA);
      key.set_activated(true);
      if (!client_->SendKey(key, &output)) {
        *error = "SendKey failed";
        return false;
      }
    }

    *preedit = PreeditText(output);

    mozc::commands::KeyEvent space;
    space.set_special_key(mozc::commands::KeyEvent::SPACE);
    space.set_mode(mozc::commands::HIRAGANA);
    space.set_activated(true);
    if (!client_->SendKey(space, &output)) {
      *error = "conversion key failed";
      return false;
    }

    if (output.has_candidate_window()) {
      const auto& window = output.candidate_window();
      const int limit = std::min(window.candidate_size(), 12);
      for (int i = 0; i < limit; ++i) {
        candidates->push_back(window.candidate(i).value());
      }
    }

    if (candidates->empty() && output.has_preedit()) {
      const std::string converted = PreeditText(output);
      if (!converted.empty()) {
        candidates->push_back(converted);
      }
    }

    mozc::commands::SessionCommand cleanup;
    cleanup.set_type(mozc::commands::SessionCommand::REVERT);
    client_->SendCommand(cleanup, &output);

    return true;
  }

 private:
  std::unique_ptr<mozc::client::ClientInterface> client_;
};

void PrintResult(const std::string& raw,
                 bool ok,
                 const std::string& preedit,
                 const std::vector<std::string>& candidates,
                 const std::string& error) {
  std::cout << "{\"ok\":" << (ok ? "true" : "false")
            << ",\"raw\":\"" << JsonEscape(raw) << "\""
            << ",\"preedit\":\"" << JsonEscape(preedit) << "\""
            << ",\"candidates\":[";

  for (size_t i = 0; i < candidates.size(); ++i) {
    if (i != 0) {
      std::cout << ",";
    }
    std::cout << "\"" << JsonEscape(candidates[i]) << "\"";
  }

  std::cout << "]"
            << ",\"error\":\"" << JsonEscape(error) << "\"}"
            << std::endl;
}

}  // namespace

int main(int argc, char** argv) {
  const bool stdio_mode =
      argc >= 2 && std::string(argv[1]) == "--stdio";

  MozcProbeEngine engine;

  if (!stdio_mode) {
    if (argc < 2) {
      std::cerr << "usage: boundary_mozc_bridge --stdio | <raw-reading>\n";
      return 2;
    }

    std::string preedit;
    std::vector<std::string> candidates;
    std::string error;
    const std::string raw = argv[1];
    const bool ok = engine.Probe(raw, &preedit, &candidates, &error);
    PrintResult(raw, ok, preedit, candidates, error);
    return ok ? 0 : 1;
  }

  std::string line;
  while (std::getline(std::cin, line)) {
    if (line == ":quit") {
      break;
    }

    std::string preedit;
    std::vector<std::string> candidates;
    std::string error;
    const bool ok = engine.Probe(
        line, &preedit, &candidates, &error);
    PrintResult(line, ok, preedit, candidates, error);
  }

  return 0;
}
