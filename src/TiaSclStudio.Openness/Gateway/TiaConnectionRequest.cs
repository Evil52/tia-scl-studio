using System;

namespace TiaSclStudio.Openness.Gateway
{
    public sealed class TiaConnectionRequest
    {
        public TiaConnectionRequest(
            TiaConnectionMode mode,
            int? processId = null,
            string projectPath = null,
            string targetDeviceName = null,
            string targetSoftwareName = null)
        {
            if (!Enum.IsDefined(typeof(TiaConnectionMode), mode))
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            if (processId.HasValue && processId.Value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(processId));
            }

            if (mode != TiaConnectionMode.AttachToRunning && processId.HasValue)
            {
                throw new ArgumentException(
                    "A process ID can only be used when attaching to a running TIA Portal.",
                    nameof(processId));
            }

            Mode = mode;
            ProcessId = processId;
            ProjectPath = Normalize(projectPath);
            TargetDeviceName = Normalize(targetDeviceName);
            TargetSoftwareName = Normalize(targetSoftwareName);
        }

        public TiaConnectionMode Mode { get; }

        public int? ProcessId { get; }

        public string ProjectPath { get; }

        public string TargetDeviceName { get; }

        public string TargetSoftwareName { get; }

        private static string Normalize(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
    }
}
