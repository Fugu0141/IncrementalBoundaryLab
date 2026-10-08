#ifndef BOUNDARYLAB_RESPONSIBILITY_MOZC_JAPANESE_ORACLE_H_
#define BOUNDARYLAB_RESPONSIBILITY_MOZC_JAPANESE_ORACLE_H_

#include <memory>
#include <string_view>

#include "client/client_interface.h"
#include "responsibility/japanese_oracle.h"

namespace boundarylab {

class MozcJapaneseOracle final : public JapaneseOracle {
 public:
  MozcJapaneseOracle();
  ~MozcJapaneseOracle() override = default;

  JapaneseProbeResult Probe(std::string_view raw) override;

 private:
  bool EnsureReady();

  std::unique_ptr<mozc::client::ClientInterface> client_;
};

}  // namespace boundarylab

#endif  // BOUNDARYLAB_RESPONSIBILITY_MOZC_JAPANESE_ORACLE_H_
