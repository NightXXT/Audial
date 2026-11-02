using System;

namespace YtMusic.Core.Models;

public sealed record Track(string Id, string Title, string Artist, string? Album, TimeSpan? Duration, string? ArtworkUrl, bool IsDemo = false);
