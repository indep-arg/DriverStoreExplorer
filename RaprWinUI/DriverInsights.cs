using System;
using System.Collections.Generic;
using System.Linq;

using Rapr.Utils;

namespace Rapr.WinUI
{
    /// <summary>
    /// The groups the Explorer and Compact layouts filter by.
    /// </summary>
    public enum DriverView
    {
        All,
        OlderVersions,
        NoDevice,
        BootCritical,
        Extensions,
    }

    /// <summary>
    /// What can be said about each package from the whole list: whether a newer version of it is
    /// installed, and which one.
    /// </summary>
    public sealed class DriverInsights
    {
        private readonly Dictionary<DriverStoreEntry, DriverStoreEntry> newerVersions =
            new Dictionary<DriverStoreEntry, DriverStoreEntry>();

        public static DriverInsights Empty { get; } = new DriverInsights(Array.Empty<DriverStoreEntry>(), devicesKnown: false);

        /// <param name="entries">All packages in the store.</param>
        /// <param name="devicesKnown">False for offline images, where devices aren't listed.</param>
        public DriverInsights(IEnumerable<DriverStoreEntry> entries, bool devicesKnown)
        {
            this.DevicesKnown = devicesKnown;
            this.Entries = entries.ToList();

            // Same grouping as Select Old Driver(s): one driver, several versions.
            var groups = this.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.DriverInfName)
                    && entry.DriverInfName != DriverStoreRepository.UnknownInfName
                    && entry.DriverInfName != "ntprint.inf")
                .GroupBy(entry => (entry.DriverClass, entry.DriverExtensionId, entry.DriverPkgProvider, entry.DriverInfName));

            foreach (var group in groups)
            {
                var newestFirst = group
                    .OrderByDescending(entry => entry.DriverVersion)
                    .ThenByDescending(entry => entry.DriverDate)
                    .ToList();

                DriverStoreEntry newest = newestFirst[0];
                foreach (DriverStoreEntry entry in newestFirst.Skip(1))
                {
                    if (entry.DriverVersion != newest.DriverVersion || entry.DriverDate != newest.DriverDate)
                    {
                        this.newerVersions[entry] = newest;
                    }
                }
            }
        }

        public IReadOnlyList<DriverStoreEntry> Entries { get; }

        public bool DevicesKnown { get; }

        /// <summary>
        /// The newest version of the same driver, if this one is older.
        /// </summary>
        public DriverStoreEntry? GetNewerVersion(DriverStoreEntry entry) =>
            this.newerVersions.TryGetValue(entry, out DriverStoreEntry? newer) ? newer : null;

        /// <summary>
        /// An older copy that no device uses, the same packages Select Old Driver(s) picks.
        /// </summary>
        public bool IsOlderVersion(DriverStoreEntry entry) =>
            this.DevicesKnown && !entry.HasDeviceAssociation && this.newerVersions.ContainsKey(entry);

        public bool HasNoDevice(DriverStoreEntry entry) => this.DevicesKnown && !entry.HasDeviceAssociation;

        public static bool IsBootCritical(DriverStoreEntry entry) => entry.BootCritical == true;

        public static bool IsExtension(DriverStoreEntry entry) => entry.DriverExtensionId != Guid.Empty;

        public bool Matches(DriverStoreEntry entry, DriverView view)
        {
            switch (view)
            {
                case DriverView.OlderVersions:
                    return this.IsOlderVersion(entry);
                case DriverView.NoDevice:
                    return this.HasNoDevice(entry);
                case DriverView.BootCritical:
                    return IsBootCritical(entry);
                case DriverView.Extensions:
                    return IsExtension(entry);
                default:
                    return true;
            }
        }

        public int Count(DriverView view) => this.Entries.Count(entry => this.Matches(entry, view));

        /// <summary>
        /// Driver classes with how many packages each has, largest first.
        /// </summary>
        public IEnumerable<KeyValuePair<string, int>> CountByClass() => this.Entries
            .GroupBy(entry => string.IsNullOrEmpty(entry.DriverClass) ? string.Empty : entry.DriverClass)
            .Select(group => new KeyValuePair<string, int>(group.Key, group.Count()))
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase);
    }
}
