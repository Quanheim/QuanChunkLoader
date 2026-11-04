using BepInEx.Configuration;

namespace QuanChunkLoader
{
    internal enum AreaSize
    {
        ThreeByThree,
        OneByOne
    }

    internal enum LogDetail
    {
        Info,
        Debug,
        Trace
    }

    /// <summary>
    /// The config file, in the numbered sections of docs/design.md ("Limits and admin controls"). This first build
    /// binds the settings the core uses; the rest are added with the features they control.
    /// </summary>
    internal static class Settings
    {
        internal static ConfigEntry<bool> BuilderOrAdminSwitches;
        internal static ConfigEntry<AreaSize> Area;
        internal static ConfigEntry<bool> SimulateTames;
        internal static ConfigEntry<int> StoneCost;
        internal static ConfigEntry<LogDetail> Detail;
        internal static ConfigEntry<bool> DailyFile;
        internal static ConfigEntry<int> DaysToKeep;

        private static ConfigDescription Enforced(string text, AcceptableValueBase values = null) =>
            new ConfigDescription(text + " Synced from the server; admins only.", values,
                new ConfigurationManagerAttributes { IsAdminOnly = true });

        internal static void Bind(ConfigFile config)
        {
            const string general = "0 - General";
            BuilderOrAdminSwitches = config.Bind(general, "Only the builder or an admin can switch an observer", true,
                Enforced("On: only the player who built an observer, or an admin, can switch it on or off. Off: " +
                         "anyone with access through the wards around it can."));

            const string loading = "2 - Loading";
            Area = config.Bind(loading, "Area size", AreaSize.ThreeByThree,
                Enforced("How many zones (64 x 64 m each) an active observer keeps loaded: the 3 x 3 block around " +
                         "it, or only its own zone. Changing it applies at once to every observer."));
            SimulateTames = config.Bind(loading, "Simulate tames in loaded zones", false,
                Enforced("Off: tamed creatures in a loaded base pause while nobody is near, as they would without " +
                         "a chunk loader, and load normally when a player arrives. On: the server runs them, so " +
                         "pens keep breeding with nobody there. Risks: unattended pens can grow past their limits, " +
                         "and tame mods' feeding and breeding code runs on the server. See the README."));

            const string pieces = "6 - Pieces";
            StoneCost = config.Bind(pieces, "Stone cost", 10,
                Enforced("Stone needed to build a Chunk Observer. Needs no crafting station. Takes effect after a " +
                         "restart.", new AcceptableValueRange<int>(1, 100)));

            const string logging = "8 - Logging";
            Detail = config.Bind(logging, "Log detail", LogDetail.Info,
                Enforced("How much the server logs. Info: switches, loaded-zone changes, a summary every 5 " +
                         "minutes and warnings; a handful of lines a minute at most. Debug: adds a line per object " +
                         "for ownership changes and loads. Trace: adds every ownership decision, for chasing a bug."));
            DailyFile = config.Bind(logging, "Write a daily log file", false,
                Enforced("Also write this mod's log lines to BepInEx/QuanChunkLoader/logs, one file per day."));
            DaysToKeep = config.Bind(logging, "Days of daily files to keep", 14,
                Enforced("Older daily files are deleted.", new AcceptableValueRange<int>(1, 365)));
        }
    }
}
