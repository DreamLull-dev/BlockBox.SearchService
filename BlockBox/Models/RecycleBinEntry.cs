using System;

namespace BlockBox.Models
{
    public sealed class RecycleBinEntry
    {
        public ShortcutItem Shortcut { get; init; } = new();
        public DateTime DeletedAt { get; init; }
    }
}
