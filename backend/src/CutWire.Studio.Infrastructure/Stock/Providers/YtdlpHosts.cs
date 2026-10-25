using CutWire.Studio.Application.Stock;

namespace CutWire.Studio.Infrastructure.Stock.Providers;

/// <summary>Exact-match host rules. Ownership decides which URLs reach an extractor that will fetch them, so lookalikes must never match.</summary>
public static class YtdlpHosts
{
    public const string YoutubeId = "youtube";
    public const string YoutubeMusicId = "youtube_music";
    public const string InstagramId = "instagram";
    public const string FacebookId = "facebook";
    public const string TikTokId = "tiktok";
    public const string VimeoId = "vimeo";
    public const string XId = "x";
    public const string SoundCloudId = "soundcloud";

    public static readonly IReadOnlyDictionary<string, string[]> ById = new Dictionary<string, string[]>
    {
        [YoutubeId] = ["youtube.com", "www.youtube.com", "m.youtube.com", "youtu.be", "www.youtu.be"],
        [YoutubeMusicId] = ["music.youtube.com"],
        [InstagramId] = ["instagram.com", "www.instagram.com", "m.instagram.com", "ddinstagram.com"],
        [FacebookId] = ["facebook.com", "www.facebook.com", "m.facebook.com", "web.facebook.com", "fb.watch"],
        [TikTokId] = ["tiktok.com", "www.tiktok.com", "m.tiktok.com", "vm.tiktok.com", "vt.tiktok.com"],
        [VimeoId] = ["vimeo.com", "www.vimeo.com", "player.vimeo.com"],
        [XId] = ["x.com", "www.x.com", "twitter.com", "www.twitter.com", "mobile.twitter.com"],
        [SoundCloudId] = ["soundcloud.com", "www.soundcloud.com", "m.soundcloud.com", "on.soundcloud.com"],
    };

    public static bool Owns(string providerId, string? url) =>
        ById.TryGetValue(providerId, out var hosts)
        && ProviderSupport.TryParseHttpUrl(url, out var uri)
        && hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
}
