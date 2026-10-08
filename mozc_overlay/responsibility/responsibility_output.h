#ifndef BOUNDARYLAB_RESPONSIBILITY_RESPONSIBILITY_OUTPUT_H_
#define BOUNDARYLAB_RESPONSIBILITY_RESPONSIBILITY_OUTPUT_H_

#include <string_view>

#include "protocol/commands.pb.h"

namespace boundarylab {

// Builds a Mozc Output that keeps raw ASCII text as an underlined TSF
// composition. This is used while responsibility is still Open.
mozc::commands::Output MakeOpenPreedit(std::string_view raw);

// Builds a Mozc Output that commits raw ASCII text literally to the host
// application. This is used once Literal responsibility is stable.
mozc::commands::Output MakeLiteralCommit(std::string_view raw);

}  // namespace boundarylab

#endif  // BOUNDARYLAB_RESPONSIBILITY_RESPONSIBILITY_OUTPUT_H_
