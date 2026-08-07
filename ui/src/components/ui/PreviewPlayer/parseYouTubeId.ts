export function parseYouTubeId(url: string): string | null {
    let parsed: URL;
    try {
        parsed = new URL(url);
    } catch {
        return null;
    }

    if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
        return null;
    }

    const host = parsed.hostname.replace(/^www\./, '').toLowerCase();

    if (host === 'youtu.be') {
        const id = parsed.pathname.slice(1).split('/')[0];
        return id || null;
    }

    if (host === 'youtube.com' || host === 'youtube-nocookie.com' || host === 'm.youtube.com') {
        const watchId = parsed.searchParams.get('v');
        if (watchId) {
            return watchId;
        }
        const embedMatch = parsed.pathname.match(/^\/embed\/([^/?#]+)/);
        if (embedMatch) {
            return embedMatch[1];
        }
    }

    return null;
}
