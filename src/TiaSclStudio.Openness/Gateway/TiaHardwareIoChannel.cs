using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TiaSclStudio.Openness.Gateway
{
    /// <summary>
    /// Version-neutral snapshot of one configured hardware I/O channel. Siemens
    /// objects never cross the gateway boundary.
    /// </summary>
    public sealed class TiaHardwareIoChannel
    {
        public TiaHardwareIoChannel(
            TiaIoChannelKind kind,
            int number,
            int bitAddress,
            uint widthBits,
            string logicalAddress,
            IEnumerable<string> suggestedDataTypes,
            string deviceItemPath,
            string deviceItemTypeIdentifier,
            string deviceItemTypeName)
        {
            if (number < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(number));
            }

            if (bitAddress < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bitAddress));
            }

            if (widthBits == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(widthBits));
            }

            if (string.IsNullOrWhiteSpace(logicalAddress))
            {
                throw new ArgumentException("A canonical logical address is required.", nameof(logicalAddress));
            }

            Kind = kind;
            Number = number;
            BitAddress = bitAddress;
            WidthBits = widthBits;
            LogicalAddress = logicalAddress.Trim();
            SuggestedDataTypes = new ReadOnlyCollection<string>(
                new List<string>(suggestedDataTypes ?? new string[0]));
            DeviceItemPath = deviceItemPath ?? string.Empty;
            DeviceItemTypeIdentifier = deviceItemTypeIdentifier ?? string.Empty;
            DeviceItemTypeName = deviceItemTypeName ?? string.Empty;
        }

        public TiaIoChannelKind Kind { get; }

        public int Number { get; }

        /// <summary>Absolute process-image address reported by TIA, in bits.</summary>
        public int BitAddress { get; }

        /// <summary>Channel width reported by TIA, in bits.</summary>
        public uint WidthBits { get; }

        public string LogicalAddress { get; }

        public IReadOnlyList<string> SuggestedDataTypes { get; }

        public string DeviceItemPath { get; }

        public string DeviceItemTypeIdentifier { get; }

        public string DeviceItemTypeName { get; }
    }
}
