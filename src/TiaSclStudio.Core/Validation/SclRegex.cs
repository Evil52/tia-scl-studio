using System;

namespace TiaSclStudio.Core.Validation
{
    /// <summary>
    /// Shared settings for the regular expressions that run over content this
    /// process did not write.
    /// </summary>
    public static class SclRegex
    {
        /// <summary>
        /// Upper bound on a single match. Names, data types and addresses all
        /// arrive from imported SCL sources and from project files that reach an
        /// engineer by e-mail, so a pattern can be handed input chosen to make
        /// it backtrack. Without a timeout that turns into a frozen editor with
        /// no way out; with one it becomes an ordinary validation failure.
        ///
        /// Two seconds is far longer than any legitimate match here needs, so a
        /// slow machine never trips it.
        /// </summary>
        public static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);
    }
}
