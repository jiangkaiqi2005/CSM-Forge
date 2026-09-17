using System;

namespace CsmForge.Runtime.Cities1
{
    internal enum DlcAuthorityCoverageKind
    {
        CoreAuthority = 0,
        DedicatedAdapter = 1,
        ContentOnly = 2
    }

    internal sealed class DlcAuthorityCoverageEntry
    {
        public string Name { get; private set; }
        public DlcAuthorityCoverageKind Kind { get; private set; }
        public string Authority { get; private set; }

        public DlcAuthorityCoverageEntry(string name, DlcAuthorityCoverageKind kind, string authority)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(authority)) throw new ArgumentException("DLC coverage entry is incomplete.");
            Name = name; Kind = kind; Authority = authority;
        }
    }

    /// <summary>
    /// Code-coverage registry for official CS1 gameplay DLC. This is not a gameplay-certification
    /// table: two-machine validation remains separate evidence. Every gameplay pack is classified
    /// either to core Forge domains or to a dedicated absolute-state adapter.
    /// </summary>
    internal static class OfficialDlcCoverage
    {
        private static readonly DlcAuthorityCoverageEntry[] Values =
        {
            new DlcAuthorityCoverageEntry("After Dark", DlcAuthorityCoverageKind.CoreAuthority,
                "Building+Transport+Economy+Clock+District"),
            new DlcAuthorityCoverageEntry("Snowfall", DlcAuthorityCoverageKind.CoreAuthority,
                "Weather+Net+Water+Transport+Budget+Building"),
            new DlcAuthorityCoverageEntry("Natural Disasters", DlcAuthorityCoverageKind.DedicatedAdapter,
                "builtin.disasters+Building+Net+Event"),
            new DlcAuthorityCoverageEntry("Mass Transit", DlcAuthorityCoverageKind.CoreAuthority,
                "Net+Transport+Building+Budget"),
            new DlcAuthorityCoverageEntry("Green Cities", DlcAuthorityCoverageKind.CoreAuthority,
                "Building+District+Policy+Tax+Budget"),
            new DlcAuthorityCoverageEntry("Parklife", DlcAuthorityCoverageKind.DedicatedAdapter,
                "builtin.districtpark+builtin.parkgrid+builtin.districtpark-deep+Building+Policy"),
            new DlcAuthorityCoverageEntry("Industries", DlcAuthorityCoverageKind.DedicatedAdapter,
                "builtin.districtpark+builtin.parkgrid+builtin.districtpark-deep+Building+Economy+Transport"),
            new DlcAuthorityCoverageEntry("Campus", DlcAuthorityCoverageKind.DedicatedAdapter,
                "builtin.districtpark+builtin.parkgrid+builtin.districtpark-controls+builtin.districtpark-campus+builtin.events"),
            new DlcAuthorityCoverageEntry("Sunset Harbor", DlcAuthorityCoverageKind.CoreAuthority,
                "Building+Transport+Water+Economy+Budget+Net"),
            new DlcAuthorityCoverageEntry("Airports", DlcAuthorityCoverageKind.DedicatedAdapter,
                "builtin.districtpark+builtin.parkgrid+builtin.districtpark-deep+Building+Transport+Economy"),
            new DlcAuthorityCoverageEntry("Plazas & Promenades", DlcAuthorityCoverageKind.DedicatedAdapter,
                "builtin.districtpark+builtin.parkgrid+builtin.districtpark-deep+Building+Net+District+Policy"),
            new DlcAuthorityCoverageEntry("Financial Districts", DlcAuthorityCoverageKind.CoreAuthority,
                "Building+Economy+Tax+Budget+District"),
            new DlcAuthorityCoverageEntry("Hotels & Retreats", DlcAuthorityCoverageKind.CoreAuthority,
                "Building+Economy+District+Tax+Budget"),
            new DlcAuthorityCoverageEntry("Match Day", DlcAuthorityCoverageKind.DedicatedAdapter,
                "builtin.events+Building+Transport"),
            new DlcAuthorityCoverageEntry("Concerts", DlcAuthorityCoverageKind.DedicatedAdapter,
                "builtin.events+Building+Economy"),
            new DlcAuthorityCoverageEntry("Pearls from the East", DlcAuthorityCoverageKind.ContentOnly,
                "Asset manifest+Building core authority"),
            new DlcAuthorityCoverageEntry("Content Creator Packs", DlcAuthorityCoverageKind.ContentOnly,
                "dlc:modderpack manifest+asset fingerprints"),
            new DlcAuthorityCoverageEntry("Radio Stations", DlcAuthorityCoverageKind.ContentOnly,
                "local audiovisual content; no shared simulation state")
        };

        public static DlcAuthorityCoverageEntry[] Entries
        {
            get { return (DlcAuthorityCoverageEntry[])Values.Clone(); }
        }

        public static string Summary()
        {
            int core = 0, dedicated = 0, content = 0;
            for (int i = 0; i < Values.Length; i++)
            {
                if (Values[i].Kind == DlcAuthorityCoverageKind.CoreAuthority) core++;
                else if (Values[i].Kind == DlcAuthorityCoverageKind.DedicatedAdapter) dedicated++;
                else content++;
            }
            return "official-dlc-classified=" + Values.Length +
                "; core-authority=" + core + "; dedicated-adapter=" + dedicated + "; content-only=" + content;
        }
    }
}
