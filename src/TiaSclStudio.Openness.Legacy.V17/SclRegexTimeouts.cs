using System;

namespace TiaSclStudio.Openness.Legacy.V17
{
    /// <summary>
    /// Shared timeout for the patterns this adapter runs over SCL it did not
    /// write.
    /// </summary>
    /// <remarks>
    /// The declaration and header patterns are matched against source text that
    /// comes back from a TIA project or from a file an engineer was sent, so
    /// their input is not under this program's control. Without a timeout a
    /// pattern handed pathological input backtracks with no way to interrupt
    /// it, and the export freezes mid-transaction against a live project. With
    /// one it fails like any other malformed source.
    ///
    /// This assembly cannot reference TiaSclStudio.Core, so the value is
    /// repeated here rather than shared with SclRegex.MatchTimeout.
    /// </remarks>
    internal static class SclRegexTimeouts
    {
        public static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);
    }
}
