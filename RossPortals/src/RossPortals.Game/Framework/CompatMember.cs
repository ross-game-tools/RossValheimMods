namespace RossPortals.Game.Framework
{
    /// <summary>A Valheim member the mod reaches for by name, and why.</summary>
    internal readonly struct CompatMember
    {
        public string Type { get; }
        public string Member { get; }
        public string Why { get; }

        public CompatMember(string type, string member, string why)
        {
            Type = type;
            Member = member;
            Why = why;
        }
    }
}
