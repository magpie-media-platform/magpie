using System;

namespace Magpie.Models.Jobs;

/// <summary>
/// The delay settings for job retries.
/// </summary>
/// <param name="BaseDelay">The delay before the first retry</param>
/// <param name="Factor">The multiplier for the delay after each further retry</param>
/// <param name="Cap">The upper limit for the delay between retries</param>
/// <param name="MaxAttempts">The number of attempts before the job fails</param>
public sealed record JobRetryPolicy(TimeSpan BaseDelay, double Factor, TimeSpan Cap, int MaxAttempts);
