using System.Collections.Generic;

namespace YtMusic.Core.Models;

public sealed record Page<T>(IReadOnlyList<T> Items, string? NextPageToken);
