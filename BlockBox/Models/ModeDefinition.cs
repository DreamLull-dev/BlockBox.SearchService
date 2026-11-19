namespace BlockBox.Models
{
    public sealed class ModeDefinition
    {
        public AppMode Mode { get; init; }
        public string Name { get; init; } = "";
        public bool IsBuiltIn { get; init; }
    }
}
