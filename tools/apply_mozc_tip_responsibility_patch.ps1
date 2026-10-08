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

    $Text = Get-Content -Raw -Path $Path
    if (-not $Text.Contains($Old)) {
        throw "Expected patch anchor was not found in $Path"
    }

    $Text = $Text.Replace($Old, $New)
    Set-Content -Path $Path -Value $Text -Encoding utf8NoBOM
}

$Header = Join-Path $MozcRoot "src\win32\tip\tip_private_context.h"
$Source = Join-Path $MozcRoot "src\win32\tip\tip_private_context.cc"
$Build = Join-Path $MozcRoot "src\win32\tip\BUILD.bazel"

Replace-Required $Header @'
namespace mozc {
namespace win32 {
namespace tsf {
'@ @'
namespace boundarylab {
class ResponsibilityDecoder;
}

namespace mozc {
namespace win32 {
namespace tsf {
'@

Replace-Required $Header @'
  client::ClientInterface* GetClient();
  SurrogatePairObserver* GetSurrogatePairObserver();
'@ @'
  client::ClientInterface* GetClient();
  boundarylab::ResponsibilityDecoder* GetResponsibilityDecoder();
  SurrogatePairObserver* GetSurrogatePairObserver();
'@

Replace-Required $Source @'
#include "protocol/commands.pb.h"
#include "win32/base/config_snapshot.h"
'@ @'
#include "protocol/commands.pb.h"
#include "responsibility/mozc_japanese_oracle.h"
#include "responsibility/responsibility_decoder.h"
#include "win32/base/config_snapshot.h"
'@

Replace-Required $Source @'
class TipPrivateContext::InternalState {
 public:
  InternalState() : client_(ClientFactory::NewClient()) {}
  std::unique_ptr<client::ClientInterface> client_;
'@ @'
class TipPrivateContext::InternalState {
 public:
  InternalState()
      : client_(ClientFactory::NewClient()),
        responsibility_oracle_(
            std::make_unique<boundarylab::MozcJapaneseOracle>()),
        responsibility_decoder_(
            std::make_unique<boundarylab::ResponsibilityDecoder>(
                responsibility_oracle_.get())) {}

  std::unique_ptr<client::ClientInterface> client_;
  std::unique_ptr<boundarylab::MozcJapaneseOracle> responsibility_oracle_;
  std::unique_ptr<boundarylab::ResponsibilityDecoder> responsibility_decoder_;
'@

Replace-Required $Source @'
ClientInterface* TipPrivateContext::GetClient() {
  return state_->client_.get();
}

void TipPrivateContext::EnsureInitialized() {
'@ @'
ClientInterface* TipPrivateContext::GetClient() {
  return state_->client_.get();
}

boundarylab::ResponsibilityDecoder*
TipPrivateContext::GetResponsibilityDecoder() {
  return state_->responsibility_decoder_.get();
}

void TipPrivateContext::EnsureInitialized() {
'@

Replace-Required $Build @'
        "//protocol:commands_cc_proto",
        "//win32/base:config_snapshot",
'@ @'
        "//protocol:commands_cc_proto",
        "//responsibility:mozc_japanese_oracle",
        "//responsibility:responsibility_decoder",
        "//win32/base:config_snapshot",
'@

Write-Host "Applied BoundaryLab Responsibility state to Mozc TSF TipPrivateContext."
