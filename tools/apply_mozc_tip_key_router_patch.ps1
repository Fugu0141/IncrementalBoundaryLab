param(
    [string]$MozcDir = ".external\mozc"
)

$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$MozcRoot = if ([System.IO.Path]::IsPathRooted($MozcDir)) {
    $MozcDir
} else {
    Join-Path $RepoRoot $MozcDir
}

function Replace-Required {
    param(
        [string]$Path,
        [string]$Old,
        [string]$New
    )

    $Text = (Get-Content -Raw -Path $Path).Replace("`r`n", "`n")
    $OldNormalized = $Old.Replace("`r`n", "`n")
    $NewNormalized = $New.Replace("`r`n", "`n")

    if (-not $Text.Contains($OldNormalized)) {
        throw "Expected key-router patch anchor was not found in $Path"
    }

    $Text = $Text.Replace($OldNormalized, $NewNormalized)
    [System.IO.File]::WriteAllText(
        $Path,
        $Text,
        [System.Text.UTF8Encoding]::new($false))
}

$Source = Join-Path $MozcRoot "src\win32\tip\tip_keyevent_handler.cc"
$Build = Join-Path $MozcRoot "src\win32\tip\BUILD.bazel"

Replace-Required $Source @'
#include <cstdint>
#include <memory>
#include <string>
'@ @'
#include <cctype>
#include <cstdint>
#include <memory>
#include <string>
#include <string_view>
#include <utility>
'@

Replace-Required $Source @'
#include "protocol/commands.pb.h"
#include "win32/base/conversion_mode_util.h"
'@ @'
#include "protocol/commands.pb.h"
#include "responsibility/responsibility_runtime.h"
#include "win32/base/conversion_mode_util.h"
'@

Replace-Required $Source @'
using ::mozc::commands::CompositionMode;
using ::mozc::commands::Context;
using ::mozc::commands::SessionCommand;
'@ @'
using ::mozc::commands::CompositionMode;
using ::mozc::commands::Context;
using ::mozc::commands::KeyEvent;
using ::mozc::commands::Output;
using ::mozc::commands::SessionCommand;
'@

Replace-Required $Source @'
constexpr UINT kTouchKeyboardNextPage = 0xf003;
constexpr UINT kTouchKeyboardPreviousPage = 0xf004;

// Unlike IMM32 Mozc which is marked as IME_PROP_ACCEPT_WIDE_VKEY,
'@ @'
constexpr UINT kTouchKeyboardNextPage = 0xf003;
constexpr UINT kTouchKeyboardPreviousPage = 0xf004;

class ResponsibilityKeyEventAdapter : public KeyEventHandler {
 public:
  static bool Convert(const VirtualKey& virtual_key, BYTE scan_code,
                      bool is_key_down, bool is_menu_active,
                      const InputBehavior& behavior,
                      const InputState& ime_state,
                      const KeyboardStatus& keyboard_status,
                      Win32KeyboardInterface* keyboard, KeyEvent* key) {
    return ConvertToKeyEvent(virtual_key, scan_code, is_key_down,
                             is_menu_active, behavior, ime_state,
                             keyboard_status, keyboard, key);
  }
};

bool IsPureModifierVirtualKey(BYTE virtual_key) {
  switch (virtual_key) {
    case VK_SHIFT:
    case VK_LSHIFT:
    case VK_RSHIFT:
    case VK_CONTROL:
    case VK_LCONTROL:
    case VK_RCONTROL:
    case VK_MENU:
    case VK_LMENU:
    case VK_RMENU:
    case VK_LWIN:
    case VK_RWIN:
      return true;
    default:
      return false;
  }
}

bool HasControlLikeModifier(const KeyEvent& key) {
  for (int i = 0; i < key.modifier_keys_size(); ++i) {
    const KeyEvent::ModifierKey modifier = key.modifier_keys(i);
    switch (modifier) {
      case KeyEvent::CTRL:
      case KeyEvent::ALT:
      case KeyEvent::LEFT_CTRL:
      case KeyEvent::RIGHT_CTRL:
      case KeyEvent::LEFT_ALT:
      case KeyEvent::RIGHT_ALT:
        return true;
      default:
        break;
    }
  }
  return false;
}

bool IsResponsibilityAscii(char c) {
  const unsigned char u = static_cast<unsigned char>(c);
  if (std::isalnum(u) != 0) {
    return true;
  }
  constexpr std::string_view kResponsibilitySymbols = "._-+#@/:()[]{}\"'";
  return kResponsibilitySymbols.find(c) != std::string_view::npos;
}

bool IsResponsibilityRoutingEnabled(
    TipTextService* text_service, TipPrivateContext* private_context) {
  if (text_service == nullptr || private_context == nullptr) {
    return false;
  }

  const TipInputModeManager* input_mode_manager =
      text_service->GetThreadContext()->GetInputModeManager();
  if (input_mode_manager == nullptr ||
      input_mode_manager->GetEffectiveConversionMode() != commands::HIRAGANA) {
    return false;
  }

  // While Mozc is showing conversion candidates, preserve all normal Mozc
  // key semantics (candidate navigation, selection, conversion, etc.).
  if (private_context->responsibility_base_output().has_candidate_window()) {
    return false;
  }

  return true;
}

bool TryGetResponsibilityAscii(
    const VirtualKey& virtual_key, BYTE scan_code, bool is_key_down,
    bool is_menu_active, const KeyboardStatus& keyboard_status,
    const InputBehavior& behavior, const InputState& ime_state,
    Win32KeyboardInterface* keyboard, char* result) {
  if (!is_key_down || keyboard == nullptr || result == nullptr) {
    return false;
  }

  KeyEvent key;
  if (!ResponsibilityKeyEventAdapter::Convert(
          virtual_key, scan_code, is_key_down, is_menu_active, behavior,
          ime_state, keyboard_status, keyboard, &key)) {
    return false;
  }

  if (HasControlLikeModifier(key) || !key.has_key_code() ||
      key.key_code() > 0x7f) {
    return false;
  }

  const char c = static_cast<char>(key.key_code());
  if (!IsResponsibilityAscii(c)) {
    return false;
  }

  *result = c;
  return true;
}

Output BuildResponsibilityDisplayOutput(const Output& base,
                                        std::string_view pending) {
  Output output = base;
  output.set_consumed(true);

  if (pending.empty()) {
    return output;
  }

  output.clear_candidate_window();
  output.clear_all_candidate_words();
  output.clear_incognito_candidate_words();
  output.clear_result();

  commands::Preedit* preedit = output.mutable_preedit();
  uint32_t cursor = 0;
  for (const commands::Preedit::Segment& segment : preedit->segment()) {
    cursor += segment.value_length();
  }

  commands::Preedit::Segment* segment = preedit->add_segment();
  segment->set_annotation(commands::Preedit::Segment::UNDERLINE);
  segment->set_value(std::string(pending));
  segment->set_value_length(static_cast<uint32_t>(pending.size()));
  segment->set_key(std::string(pending));
  preedit->set_cursor(cursor + static_cast<uint32_t>(pending.size()));

  return output;
}

bool SendResponsibilityFlushes(
    TipPrivateContext* private_context,
    const boundarylab::ResponsibilityRuntimeUpdate& update) {
  if (private_context == nullptr) {
    return false;
  }

  for (const boundarylab::ResponsibilityFlush& flush : update.flushes) {
    const CompositionMode mode =
        flush.responsibility == boundarylab::Responsibility::kLiteral
            ? commands::HALF_ASCII
            : commands::HIRAGANA;

    for (const unsigned char c : flush.raw) {
      KeyEvent key;
      key.set_key_code(static_cast<uint32_t>(c));
      key.set_mode(mode);
      key.set_activated(true);

      Output output;
      if (!private_context->GetClient()->SendKey(key, &output)) {
        return false;
      }
      *private_context->mutable_responsibility_base_output() =
          std::move(output);
    }
  }

  return true;
}

HRESULT RenderResponsibilityUpdate(
    TipTextService* text_service, ITfContext* context,
    TipPrivateContext* private_context,
    const boundarylab::ResponsibilityRuntimeUpdate& update, BOOL* eaten) {
  if (!SendResponsibilityFlushes(private_context, update)) {
    *eaten = FALSE;
    return E_FAIL;
  }

  Output display = BuildResponsibilityDisplayOutput(
      private_context->responsibility_base_output(), update.pending_raw);
  if (!TipEditSession::OnOutputReceivedSync(text_service, context,
                                            std::move(display))) {
    *eaten = FALSE;
    return E_FAIL;
  }

  *eaten = TRUE;
  return S_OK;
}

// Unlike IMM32 Mozc which is marked as IME_PROP_ACCEPT_WIDE_VKEY,
'@

Replace-Required $Source @'
  InputState next_state;
  commands::Output temporal_output;
  std::unique_ptr<Win32KeyboardInterface> keyboard(
      Win32KeyboardInterface::CreateDefault());

  const KeyEventHandlerResult result = KeyEventHandler::ImeProcessKey(
'@ @'
  InputState next_state;
  commands::Output temporal_output;
  std::unique_ptr<Win32KeyboardInterface> keyboard(
      Win32KeyboardInterface::CreateDefault());

  if (open && IsResponsibilityRoutingEnabled(text_service, private_context)) {
    boundarylab::ResponsibilityRuntime* runtime =
        private_context->GetResponsibilityRuntime();

    char responsibility_char = 0;
    if (runtime != nullptr &&
        TryGetResponsibilityAscii(
            vk, key_info.GetScanCode(), is_key_down,
            key_info.HasContextCode(), keyboard_status, behavior, input_state,
            keyboard.get(), &responsibility_char)) {
      *eaten = TRUE;
      return S_OK;
    }

    if (runtime != nullptr && !runtime->pending_raw().empty() &&
        is_key_down && !IsPureModifierVirtualKey(vk.virtual_key())) {
      *eaten = TRUE;
      return S_OK;
    }
  }

  const KeyEventHandlerResult result = KeyEventHandler::ImeProcessKey(
'@

Replace-Required $Source @'
    std::unique_ptr<Win32KeyboardInterface> keyboard(
        Win32KeyboardInterface::CreateDefault());

    Context mozc_context;
    FillMozcContextForOnKey(text_service, context, &mozc_context);

    InputState next_state;
    const KeyEventHandlerResult result = KeyEventHandler::ImeToAsciiEx(
'@ @'
    std::unique_ptr<Win32KeyboardInterface> keyboard(
        Win32KeyboardInterface::CreateDefault());

    boundarylab::ResponsibilityRuntime* runtime =
        private_context->GetResponsibilityRuntime();

    if (open && is_key_down && runtime != nullptr &&
        IsResponsibilityRoutingEnabled(text_service, private_context)) {
      if (vk.virtual_key() == VK_BACK && !runtime->pending_raw().empty()) {
        const boundarylab::ResponsibilityRuntimeUpdate update =
            runtime->Backspace();
        return RenderResponsibilityUpdate(
            text_service, context, private_context, update, eaten);
      }

      if (vk.virtual_key() == VK_ESCAPE && !runtime->pending_raw().empty()) {
        runtime->Reset();

        const Output& base = private_context->responsibility_base_output();
        const bool server_has_preedit =
            base.has_preedit() && base.preedit().segment_size() > 0;

        if (!server_has_preedit) {
          boundarylab::ResponsibilityRuntimeUpdate cleared;
          return RenderResponsibilityUpdate(
              text_service, context, private_context, cleared, eaten);
        }
        // If Mozc already owns stable text, fall through so the normal Escape
        // path cancels the server-side composition as well.
      }

      char responsibility_char = 0;
      if (TryGetResponsibilityAscii(
              vk, key_info.GetScanCode(), is_key_down,
              key_info.HasContextCode(), keyboard_status, behavior, ime_state,
              keyboard.get(), &responsibility_char)) {
        const boundarylab::ResponsibilityRuntimeUpdate update =
            runtime->Push(responsibility_char);
        return RenderResponsibilityUpdate(
            text_service, context, private_context, update, eaten);
      }

      if (!runtime->pending_raw().empty() &&
          !IsPureModifierVirtualKey(vk.virtual_key())) {
        const boundarylab::ResponsibilityRuntimeUpdate update =
            runtime->ClosePending();
        if (!SendResponsibilityFlushes(private_context, update)) {
          *eaten = FALSE;
          return E_FAIL;
        }
      }
    }

    Context mozc_context;
    FillMozcContextForOnKey(text_service, context, &mozc_context);

    InputState next_state;
    const KeyEventHandlerResult result = KeyEventHandler::ImeToAsciiEx(
'@

Replace-Required $Source @'
    if (!result.should_be_sent_to_server) {
      // no message generated.
      *eaten = FALSE;
      return S_OK;
    }

    ignore_this_keyevent = !result.should_be_eaten;
'@ @'
    if (!result.should_be_sent_to_server) {
      // no message generated.
      *eaten = FALSE;
      return S_OK;
    }

    if (private_context->GetResponsibilityRuntime() != nullptr &&
        private_context->GetResponsibilityRuntime()->pending_raw().empty()) {
      if (temporal_output.has_preedit()) {
        *private_context->mutable_responsibility_base_output() =
            temporal_output;
      } else {
        private_context->mutable_responsibility_base_output()->Clear();
      }
    }

    ignore_this_keyevent = !result.should_be_eaten;
'@

Replace-Required $Build @'
        "//protocol:commands_cc_proto",
        "//win32/base:conversion_mode_util",
'@ @'
        "//protocol:commands_cc_proto",
        "//responsibility:responsibility_runtime",
        "//win32/base:conversion_mode_util",
'@

Write-Host "Applied BoundaryLab Responsibility key router to Mozc TSF."
