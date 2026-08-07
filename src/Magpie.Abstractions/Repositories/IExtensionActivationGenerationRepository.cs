using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for the cluster-wide extension activation generation.
/// </summary>
public interface IExtensionActivationGenerationRepository : IRepository
{
    /// <summary>
    /// Gets the current value of the activation generation counter.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The current generation value</returns>
    Task<long> GetCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds one to the activation generation counter in a single atomic step.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The new generation value</returns>
    Task<long> BumpAsync(CancellationToken cancellationToken = default);
}
