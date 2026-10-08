#include "responsibility/responsibility_decoder.h"

#include <algorithm>
#include <array>
#include <cctype>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace boundarylab {
namespace {

constexpr std::array<std::string_view, 50> kEnglishWords = {
    "javascript", "typescript", "network", "windows", "reflect", "function",
    "commit", "github", "kernel", "server", "deploy", "method", "branch",
    "ubuntu", "invite", "google", "object", "client", "output", "input",
    "issue", "linux", "build", "cache", "debug", "merge", "python", "string",
    "token", "node", "json", "html", "class", "push", "pull", "code", "test",
    "repo", "with", "from", "this", "that", "then", "rust", "jsx", "tsx",
    "css", "the", "and", "for"};

constexpr std::array<std::string_view, 35> kEnglishShortWords = {
    "api", "bug", "fix", "not", "yes", "can", "but", "node", "json",
    "html", "class", "push", "pull", "code", "test", "repo", "with",
    "from", "this", "that", "then", "rust", "jsx", "tsx", "css", "the",
    "and", "for", "or", "js", "ts", "cpp", "csharp", "win", "git"};

// Strong evidence that may safely split an English literal from following
// Japanese while the user is still typing. Keep one-letter kana out of this
// table: otherwise ordinary English such as "theory" can become "the | ory".
constexpr std::array<std::string_view, 34> kJapaneseContinuations = {
    "shimashita", "simashita", "shimasita", "simasita", "shimasu", "simasu",
    "sareta", "shitai", "sitai", "shite", "site", "shita", "sita", "suru",
    "kara", "made", "yori", "miru", "tsukau", "tukau", "okuru", "tateru",
    "kakunin", "ha", "wa", "ga", "wo", "ni", "de", "to", "mo",
    "he", "no", "yo"};

// At an explicit command boundary (Space/Enter/etc.), a lowercase single kana
// reading is allowed to prefer Japanese. This table must not be reused as an
// inline segmentation signal.
constexpr std::array<std::string_view, 39> kJapaneseBoundaryTokens = {
    "shimashita", "simashita", "shimasita", "simasita", "shimasu", "simasu",
    "sareta", "shitai", "sitai", "shite", "site", "shita", "sita", "suru",
    "kara", "made", "yori", "miru", "tsukau", "tukau", "okuru", "tateru",
    "kakunin", "ha", "wa", "ga", "wo", "o", "ni", "de", "to", "mo",
    "he", "no", "yo", "a", "i", "u", "e"};

bool StartsWithAt(std::string_view raw, std::size_t start,
                  std::string_view value) {
  return start + value.size() <= raw.size() &&
         raw.substr(start, value.size()) == value;
}

bool IsAsciiWordChar(char c) {
  const unsigned char u = static_cast<unsigned char>(c);
  return std::isalnum(u) != 0;
}

bool HasNonAscii(std::string_view value) {
  for (const unsigned char c : value) {
    if (c >= 0x80) {
      return true;
    }
  }
  return false;
}

template <std::size_t N>
bool ContainsWord(const std::array<std::string_view, N>& words,
                  std::string_view value) {
  return std::find(words.begin(), words.end(), value) != words.end();
}

template <std::size_t N>
bool IsPrefixOfAny(const std::array<std::string_view, N>& words,
                   std::string_view value) {
  for (const std::string_view word : words) {
    if (word.size() > value.size() &&
        word.substr(0, value.size()) == value) {
      return true;
    }
  }
  return false;
}

}  // namespace

ResponsibilityDecoder::ResponsibilityDecoder(JapaneseOracle* oracle)
    : oracle_(oracle) {}

ResponsibilityAnalysis ResponsibilityDecoder::Analyze(std::string_view input) {
  ResponsibilityAnalysis analysis;
  analysis.raw = Normalize(input);
  const std::string_view raw = analysis.raw;

  std::size_t i = 0;
  while (i < raw.size()) {
    if (IsHardBoundary(raw[i])) {
      analysis.spans.push_back(ResponsibilitySpan{
          .start = i,
          .end = i + 1,
          .raw = std::string(raw.substr(i, 1)),
          .responsibility = Responsibility::kBoundary,
          .stable = true,
          .evidence = "hard-boundary",
      });
      ++i;
      continue;
    }

    // Incomplete lexical prefixes outrank short exact anchors. Without this,
    // "commi" could be split as "c | ommi" before "commit" has a chance to
    // complete. A hard boundary explicitly closes the unresolved token.
    std::size_t token_end = i;
    while (token_end < raw.size() && !IsHardBoundary(raw[token_end])) {
      ++token_end;
    }
    const std::string_view unresolved_token =
        raw.substr(i, token_end - i);
    if (!unresolved_token.empty() &&
        !IsEnglishExact(unresolved_token) &&
        IsEnglishPrefix(unresolved_token)) {
      if (token_end < raw.size()) {
        analysis.spans.push_back(ResponsibilitySpan{
            .start = i,
            .end = token_end,
            .raw = std::string(unresolved_token),
            .responsibility = Responsibility::kLiteral,
            .stable = true,
            .evidence = "hard-boundary-literal-close",
        });
        i = token_end;
        continue;
      }

      analysis.spans.push_back(ResponsibilitySpan{
          .start = i,
          .end = token_end,
          .raw = std::string(unresolved_token),
          .responsibility = Responsibility::kOpen,
          .stable = false,
          .evidence = "open-english-prefix",
      });
      break;
    }

    const LiteralCandidate literal = FindLiteralAt(raw, i);
    if (literal.end > i) {
      const bool has_lookahead = literal.end < raw.size();
      bool stable = has_lookahead;

      if (!literal.structural && has_lookahead) {
        stable =
            IsJapaneseContinuation(raw.substr(literal.end)) ||
            IsHardBoundary(raw[literal.end]);
      }

      analysis.spans.push_back(ResponsibilitySpan{
          .start = i,
          .end = literal.end,
          .raw = std::string(raw.substr(i, literal.end - i)),
          .responsibility = Responsibility::kLiteral,
          .stable = stable,
          .evidence = literal.evidence,
      });
      i = literal.end;
      continue;
    }

    // An explicit hard boundary closes an otherwise incomplete English
    // prefix as literal text. This mirrors the C# responsibility decoder:
    // "commi " must never turn into Japanese just because the user stopped.
    std::size_t next_boundary = i;
    while (next_boundary < raw.size() &&
           !IsHardBoundary(raw[next_boundary])) {
      ++next_boundary;
    }
    if (next_boundary > i && next_boundary < raw.size()) {
      const std::string_view token = raw.substr(i, next_boundary - i);
      if (IsEnglishPrefix(token) || IsEnglishExact(token)) {
        analysis.spans.push_back(ResponsibilitySpan{
            .start = i,
            .end = next_boundary,
            .raw = std::string(token),
            .responsibility = Responsibility::kLiteral,
            .stable = true,
            .evidence = "hard-boundary-literal-close",
        });
        i = next_boundary;
        continue;
      }
    }

    const std::string_view tail = raw.substr(i);
    if (IsEnglishPrefix(tail)) {
      analysis.spans.push_back(ResponsibilitySpan{
          .start = i,
          .end = raw.size(),
          .raw = std::string(tail),
          .responsibility = Responsibility::kOpen,
          .stable = false,
          .evidence = "open-english-prefix",
      });
      break;
    }

    std::size_t end = FindNextLiteralAnchor(raw, i + 1);
    if (end <= i || end > raw.size()) {
      end = raw.size();
    }

    for (std::size_t j = i; j < end; ++j) {
      if (IsHardBoundary(raw[j])) {
        end = j;
        break;
      }
    }

    if (end == i) {
      ++end;
    }

    const bool stable = end < raw.size();
    analysis.spans.push_back(MakeJapaneseSpan(raw, i, end, stable));
    i = end;
  }

  return analysis;
}

std::string ResponsibilityDecoder::Normalize(std::string_view raw) const {
  std::string result(raw);
  for (char& c : result) {
    const unsigned char u = static_cast<unsigned char>(c);
    if (u < 0x80) {
      c = static_cast<char>(std::tolower(u));
    }
  }
  return result;
}

bool ResponsibilityDecoder::IsEnglishExact(std::string_view raw) const {
  return ContainsWord(kEnglishWords, raw) ||
         ContainsWord(kEnglishShortWords, raw);
}

bool ResponsibilityDecoder::IsEnglishPrefix(std::string_view raw) const {
  if (raw.empty()) {
    return false;
  }
  return IsPrefixOfAny(kEnglishWords, raw) ||
         IsPrefixOfAny(kEnglishShortWords, raw);
}

bool ResponsibilityDecoder::IsJapaneseContinuation(std::string_view raw) const {
  for (const std::string_view suffix : kJapaneseContinuations) {
    if (raw.starts_with(suffix)) {
      return true;
    }
  }
  return false;
}

ResponsibilityDecoder::LiteralCandidate ResponsibilityDecoder::FindLiteralAt(
    std::string_view raw, std::size_t start) const {
  LiteralCandidate best;

  // First, find a plain English lexical anchor.
  for (const std::string_view word : kEnglishWords) {
    if (!StartsWithAt(raw, start, word)) {
      continue;
    }

    const std::size_t end = start + word.size();
    if (word.size() <= 3) {
      const bool right_evidence =
          end < raw.size() &&
          (IsHardBoundary(raw[end]) ||
           IsBindingSymbol(raw[end]) ||
           IsJapaneseContinuation(raw.substr(end)));
      if (!right_evidence) {
        continue;
      }
    }

    if (end > best.end) {
      best = {
          .end = end,
          .structural = false,
          .evidence = "english-lexeme",
      };
    }
  }
  for (const std::string_view word : kEnglishShortWords) {
    if (!StartsWithAt(raw, start, word)) {
      continue;
    }

    const std::size_t end = start + word.size();
    if (word.size() <= 3) {
      const bool right_evidence =
          end < raw.size() &&
          (IsHardBoundary(raw[end]) ||
           IsBindingSymbol(raw[end]) ||
           IsJapaneseContinuation(raw.substr(end)));
      if (!right_evidence) {
        continue;
      }
    }

    if (end > best.end) {
      best = {
          .end = end,
          .structural = false,
          .evidence = "english-lexeme",
      };
    }
  }

  // Then prefer a structural literal if a binding symbol joins two known
  // literal components. This catches node.js, foo/bar-style identifiers,
  // while deliberately rejecting de-ta because de/ta are not English
  // components in this responsibility lexicon.
  const std::size_t scan_end = std::min(raw.size(), start + 24);
  for (std::size_t symbol = start + 1; symbol < scan_end; ++symbol) {
    if (IsHardBoundary(raw[symbol])) {
      break;
    }
    if (!IsBindingSymbol(raw[symbol])) {
      continue;
    }

    const std::string_view left = raw.substr(start, symbol - start);
    const bool c_family =
        left == "c" &&
        (raw[symbol] == '#' || raw[symbol] == '+');
    if (!IsEnglishExact(left) && !c_family) {
      continue;
    }

    if (left == "c" && raw[symbol] == '#') {
      const std::size_t end = symbol + 1;
      if (end > best.end) {
        best = {
            .end = end,
            .structural = true,
            .evidence = "structural-literal",
        };
      }
      continue;
    }

    if (left == "c" && raw[symbol] == '+' &&
        symbol + 1 < raw.size() && raw[symbol + 1] == '+') {
      const std::size_t end = symbol + 2;
      if (end > best.end) {
        best = {
            .end = end,
            .structural = true,
            .evidence = "structural-literal",
        };
      }
      continue;
    }

    if ((raw[symbol] == '+' || raw[symbol] == '#') &&
        symbol + 1 == raw.size()) {
      if (symbol + 1 > best.end) {
        best = {
            .end = symbol + 1,
            .structural = true,
            .evidence = "structural-literal",
        };
      }
      continue;
    }

    const std::size_t right_start = symbol + 1;
    if (right_start >= raw.size()) {
      continue;
    }

    for (const std::string_view right : kEnglishWords) {
      if (StartsWithAt(raw, right_start, right) &&
          right_start + right.size() > best.end) {
        best = {
            .end = right_start + right.size(),
            .structural = true,
            .evidence = "structural-literal",
        };
      }
    }
    for (const std::string_view right : kEnglishShortWords) {
      if (StartsWithAt(raw, right_start, right) &&
          right_start + right.size() > best.end) {
        best = {
            .end = right_start + right.size(),
            .structural = true,
            .evidence = "structural-literal",
        };
      }
    }
  }

  return best;
}

std::size_t ResponsibilityDecoder::FindNextLiteralAnchor(
    std::string_view raw, std::size_t start) const {
  for (std::size_t i = start; i < raw.size(); ++i) {
    if (IsHardBoundary(raw[i])) {
      return i;
    }

    const LiteralCandidate candidate = FindLiteralAt(raw, i);
    if (candidate.end <= i) {
      continue;
    }

    if (candidate.structural) {
      return i;
    }

    if (candidate.end < raw.size() &&
        IsJapaneseContinuation(raw.substr(candidate.end))) {
      return i;
    }
  }

  return raw.size();
}

ResponsibilitySpan ResponsibilityDecoder::MakeJapaneseSpan(
    std::string_view raw, std::size_t start, std::size_t end, bool stable) {
  const std::string token(raw.substr(start, end - start));

  ResponsibilitySpan span{
      .start = start,
      .end = end,
      .raw = token,
      .responsibility = Responsibility::kJapanese,
      .stable = stable,
      .evidence = "japanese-default",
  };

  const bool has_binding =
      std::any_of(token.begin(), token.end(), IsBindingSymbol);
  if (!has_binding || oracle_ == nullptr) {
    return span;
  }

  JapaneseProbeResult probe = oracle_->Probe(token);
  span.mozc_quality = probe.quality;
  if (const std::string* top = probe.TopCandidate(); top != nullptr) {
    span.mozc_top_candidate = *top;
  }

  if (probe.available && probe.ok && probe.quality >= 0.85 &&
      !span.mozc_top_candidate.empty() &&
      HasNonAscii(span.mozc_top_candidate)) {
    span.evidence = "mozc-japanese";
    return span;
  }

  const LiteralCandidate structural = FindLiteralAt(raw, start);
  if (structural.structural && structural.end == end) {
    span.responsibility = Responsibility::kLiteral;
    span.evidence = "structural-literal-after-mozc-reject";
  } else {
    span.responsibility = Responsibility::kOpen;
    span.stable = false;
    span.evidence = "binding-ambiguous";
  }

  return span;
}


bool ResponsibilityDecoder::PrefersJapaneseAtCommandBoundary(
    std::string_view raw) const {
  if (raw.empty()) {
    return false;
  }

  const std::string normalized = Normalize(raw);

  // Explicit casing is strong literal evidence. This keeps "I", "C", etc.
  // usable while lowercase romanized particles continue to favor Japanese.
  if (std::any_of(raw.begin(), raw.end(), [](char c) {
        const unsigned char u = static_cast<unsigned char>(c);
        return u < 0x80 && std::isupper(u) != 0;
      })) {
    return false;
  }

  return std::find(
             kJapaneseBoundaryTokens.begin(),
             kJapaneseBoundaryTokens.end(),
             std::string_view(normalized)) != kJapaneseBoundaryTokens.end();
}

bool ResponsibilityDecoder::IsBindingSymbol(char c) {
  constexpr std::string_view kBinding = "._-+#@/:";
  return kBinding.find(c) != std::string_view::npos;
}

bool ResponsibilityDecoder::IsHardBoundary(char c) {
  constexpr std::string_view kHard = ",;!? ";
  return kHard.find(c) != std::string_view::npos;
}

const char* ResponsibilityName(Responsibility responsibility) {
  switch (responsibility) {
    case Responsibility::kOpen:
      return "Open";
    case Responsibility::kJapanese:
      return "Japanese";
    case Responsibility::kLiteral:
      return "Literal";
    case Responsibility::kBoundary:
      return "Boundary";
    case Responsibility::kUnknown:
      return "Unknown";
  }
  return "Unknown";
}

}  // namespace boundarylab
