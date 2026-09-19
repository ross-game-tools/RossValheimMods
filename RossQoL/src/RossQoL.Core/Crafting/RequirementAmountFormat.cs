namespace RossQoL.Core.Crafting
{
    /// <summary>
    /// The text a requirement row shows in place of vanilla's bare cost:
    /// what you have against what the craft needs.
    ///
    /// This is deliberately just string concatenation -- there is no rounding
    /// or clamping to get wrong -- but it exists as its own function because
    /// the row it feeds is a fixed-size UI element that was only ever proven
    /// to hold a single short number. Every digit of both numbers must
    /// survive here; the display layer is responsible for making the numbers
    /// fit, never for cutting them.
    /// </summary>
    public static class RequirementAmountFormat
    {
        public static string Format(int have, int need) => have + "/" + need;
    }
}
