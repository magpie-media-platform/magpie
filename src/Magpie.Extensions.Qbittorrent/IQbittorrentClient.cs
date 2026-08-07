using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Extensions.Qbittorrent;

internal interface IQbittorrentClient
{
    Task<string> GetVersionAsync(QbittorrentSettings settings, CancellationToken cancellationToken);

    Task<IReadOnlyList<TorrentInfo>> GetTorrentInfoAsync(
        QbittorrentSettings settings,
        string hash,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TorrentInfo>> GetTorrentInfoAsync(
        QbittorrentSettings settings,
        IReadOnlyCollection<string> hashes,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        QbittorrentSettings settings,
        string hash,
        bool deleteFiles,
        CancellationToken cancellationToken);

    Task<bool> PauseAsync(QbittorrentSettings settings, string hash, CancellationToken cancellationToken);

    Task<bool> ResumeAsync(QbittorrentSettings settings, string hash, CancellationToken cancellationToken);

    Task<bool> SetForceStartAsync(
        QbittorrentSettings settings,
        string hash,
        bool value,
        CancellationToken cancellationToken);

    Task AddTorrentFileAsync(
        QbittorrentSettings settings,
        byte[] torrentBytes,
        string fileName,
        string? tags,
        CancellationToken cancellationToken);

    Task<TorrentInfo?> FindTorrentByNameAsync(
        QbittorrentSettings settings,
        string name,
        CancellationToken cancellationToken);

    Task AddMagnetAsync(
        QbittorrentSettings settings,
        string magnetUri,
        string? tags,
        CancellationToken cancellationToken);

    Task<bool> LoginAsync(QbittorrentSettings settings, CancellationToken cancellationToken);
}
