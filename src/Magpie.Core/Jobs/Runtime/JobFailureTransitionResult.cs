using Magpie.Models.Data;

namespace Magpie.Core.Jobs.Runtime;

internal sealed record JobFailureTransitionResult(bool Transitioned, bool PublishFailure, JobExecution? Retry);
