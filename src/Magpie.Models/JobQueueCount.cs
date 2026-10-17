namespace Magpie.Models;

/// <summary>
/// The unclaimed queued executions of one job type.
/// </summary>
/// <param name="JobType">The stable type identifier of the job</param>
/// <param name="Ready">The number of executions that can run now</param>
/// <param name="Deferred">The number of first attempts that wait for a later time</param>
/// <param name="Retrying">The number of retries that wait for a later time</param>
public readonly record struct JobQueueCount(string JobType, int Ready, int Deferred, int Retrying);
