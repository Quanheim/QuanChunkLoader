using BepInEx.Configuration;

namespace QuanChunkLoader
{
    internal enum LogDetail
    {
        Info,
        Debug,
        Trace
    }

    /// <summary>
    /// The config file, in the numbered sections of docs/design.md ("Limits and admin controls"). Every setting is
    /// the server's (synced, admins only) except the map section, which is each admin's own.
    /// </summary>
    internal static class Settings
    {
        // 0 - General
        internal static ConfigEntry<bool> AdminOnlyBuilding;
        internal static ConfigEntry<bool> BuilderOrAdminSwitches;

        // 1 - Limits
        internal static ConfigEntry<int> ZonesPerPlayer;
        internal static ConfigEntry<int> ZonesInTotal;
        internal static ConfigEntry<bool> AdminsIgnoreLimits;
        internal static ConfigEntry<int> ExpireAfterHours;

        // 2 - Tamed Creatures
        internal static ConfigEntry<bool> SimulateTames;

        // 3 - Empty server
        internal static ConfigEntry<int> EmptyServerMinutes;

        // 4 - Phantom spawner
        internal static ConfigEntry<string> PhantomCreatureSpawners;
        internal static ConfigEntry<bool> PhantomSpawnAreas;

        // 5 - Despawn protection
        internal static ConfigEntry<bool> DespawnProtection;

        // 6 - Map (personal)
        internal static ConfigEntry<bool> ShowOnMap;

        // 7 - Logging
        internal static ConfigEntry<LogDetail> Detail;
        internal static ConfigEntry<bool> DailyFile;
        internal static ConfigEntry<int> DaysToKeep;

        // 8 - Unattended bases
        internal static ConfigEntry<bool> PauseRadiation;

        // 9 - Known error suppression (personal)
        internal static ConfigEntry<bool> SuppressUnreadableNavMesh;

        private static ConfigDescription Enforced(string text, AcceptableValueBase values = null) =>
            new ConfigDescription(text + " Synced from the server; admins only.", values,
                new ConfigurationManagerAttributes { IsAdminOnly = true });

        internal static void Bind(ConfigFile config)
        {
            const string general = "0 - General";
            AdminOnlyBuilding = config.Bind(general, "Only admins can build observers and phantoms", false,
                Enforced("On: the Chunk Observers and the Phantom Spawner are hidden from other players' hammers, and " +
                         "any a non-admin still manages to build stay switched off. Pieces already built are " +
                         "untouched. Useful for trying the mod out on a live server."));
            BuilderOrAdminSwitches = config.Bind(general, "Only the builder or an admin can switch an observer", true,
                Enforced("On: only the player who built an observer, or an admin, can switch it on or off. Off: " +
                         "anyone with access through the wards around it can."));

            const string limits = "1 - Limits";
            ZonesPerPlayer = config.Bind(limits, "Zones per player", 18,
                Enforced("How many zones one player's switched-on observers may load. A Chunk Observer loads 1 zone, " +
                         "a Chunk Observer Cluster 9. Checked when an observer is switched on; observers already on " +
                         "stay on if you lower it.", new AcceptableValueRange<int>(0, 1000)));
            ZonesInTotal = config.Bind(limits, "Zones in total", 108,
                Enforced("How many zones all switched-on observers on the server may load together.",
                         new AcceptableValueRange<int>(0, 10000)));
            AdminsIgnoreLimits = config.Bind(limits, "Admins ignore limits", true,
                Enforced("On: observers an admin built don't count towards either limit, and admins can switch any " +
                         "observer on regardless of the limits."));
            ExpireAfterHours = config.Bind(limits, "Expire after hours without a refresh", 0,
                Enforced("Switches an observer off after this many real hours since it was last switched on. It's " +
                         "never deleted; switching it on again restarts the count. 0 means never. Turning this on " +
                         "later starts every observer's count from that moment.", new AcceptableValueRange<int>(0, 8760)));

            const string tames = "2 - Tamed Creatures";
            SimulateTames = config.Bind(tames, "Simulate tames in loaded zones", false,
                Enforced("Off: tamed creatures in a loaded base pause while nobody is near, as they would without " +
                         "a chunk loader, and load normally when a player arrives. On: the server runs them, so " +
                         "pens keep breeding with nobody there. Risks: unattended pens can grow past their limits, " +
                         "and tame mods' feeding and breeding code runs on the server. See the README."));

            const string empty = "3 - Empty server";
            EmptyServerMinutes = config.Bind(empty, "Empty-server window in minutes", 60,
                Enforced("After the last player logs off, the world clock keeps running for up to this many real " +
                         "minutes while at least one observer is on, so loaded bases finish their work. 0 turns it " +
                         "off; at most 240 (4 hours).", new AcceptableValueRange<int>(0, 240)));

            const string phantom = "4 - Phantom spawner";
            PhantomCreatureSpawners = config.Bind(phantom, "Creature spawners", "Spawner_imp_respawn",
                Enforced("Comma-separated prefab names of the creature spawners a Phantom Spawner keeps running. " +
                         "Spawner_imp_respawn is the surtling spawner. Every spawner name in your game, including " +
                         "other mods', is listed in quandru.QuanChunkLoader.spawners.txt next to this file " +
                         "(written when the game starts). Anything added here runs its monsters on the server " +
                         "while nobody is near, and they can attack the base."));
            PhantomSpawnAreas = config.Bind(phantom, "Spawner areas", false,
                Enforced("Also keep spawner areas running, anywhere in a loaded zone: the kind of spawner that keeps " +
                         "a group of creatures topped up around itself, such as greydwarf nests and bone piles, " +
                         "including other mods' ones. They can't be chosen one by one; this turns them all on or " +
                         "off. Their monsters run on the server while nobody is near and can attack the base."));

            const string despawn = "5 - Despawn protection";
            DespawnProtection = config.Bind(despawn, "Despawn protection", true,
                Enforced("In loaded zones, items on the ground near a production station or an active Phantom " +
                         "Spawner don't vanish after an hour, so offline output waits for you."));

            const string map = "6 - Map (personal)";
            ShowOnMap = config.Bind(map, "Show observers on the map", false,
                new ConfigDescription("Admins only: shows every chunk observer in the world on your map, lit when " +
                                      "it's on. Personal."));

            const string logging = "7 - Logging";
            Detail = config.Bind(logging, "Log detail", LogDetail.Info,
                Enforced("How much the server logs. Info: switches, loaded-zone changes, a summary every 5 " +
                         "minutes and warnings; a handful of lines a minute at most. Debug: adds the diagnostic " +
                         "checks: a tame watch (each tame that moves 10 m or more, jumps, or changes who runs it), " +
                         "owner changes grouped, players coming and going, ships' buoyancy, plants overdue to " +
                         "regrow, station capacities at start and a heartbeat with each summary. Trace: adds a " +
                         "line per object loaded or taken, for chasing a bug; thousands of lines when players " +
                         "come and go, so don't leave it on."));
            DailyFile = config.Bind(logging, "Write a daily log file", false,
                Enforced("Also write this mod's log lines to BepInEx/QuanChunkLoaderLogs, one file per day."));
            DaysToKeep = config.Bind(logging, "Days of daily files to keep", 14,
                Enforced("Older daily files are deleted.", new AcceptableValueRange<int>(1, 365)));

            // Appended after release rather than renumbering: saved values sit under the section names.
            const string unattended = "8 - Unattended bases";
            PauseRadiation = config.Bind(unattended, "Pause Eitr radiation while nobody is near", true,
                Enforced("On: dropped Refined Eitr and running eitr refineries in loaded zones stop firing their " +
                         "radiation bolts (30 lightning + 30 poison, in random directions) while no player is near, " +
                         "as if the zone were unloaded. A player arriving brings back the vanilla sparks and damage. " +
                         "Off: they keep firing with nobody there, and a pile of dropped Eitr can wreck a base."));

            const string suppression = "9 - Known error suppression (personal)";
            SuppressUnreadableNavMesh = config.Bind(suppression, "Vanilla default model collider no read access", false,
                new ConfigDescription("Personal: this machine's log only. On: stops the repeated \"RuntimeNavMeshBuilder: " +
                                      "Source mesh default is skipped because it does not allow read access\" lines. " +
                                      "They come from vanilla models (cart wheels, ship hulls, logs) the navmesh can't " +
                                      "read; this leaves them out before the build instead of after, so creatures " +
                                      "path exactly as before."));
        }
    }
}
