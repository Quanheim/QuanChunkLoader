using System;
using System.Collections.Generic;
using BepInEx;
using UnityEngine;

namespace QuanChunkLoaderDiag
{
    /// <summary>
    /// Read-only, server-only diagnostic for the old chunk loader mods. No Harmony patches: it polls world state
    /// and logs changes to BepInEx/QuanChunkLoaderDiag/. On a client or listen server it does nothing.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "quandru.QuanChunkLoaderDiag";
        public const string PluginName = "QuanChunkLoaderDiag v3";

        private const float FastInterval = 2f;   // players, loaders, ownership (matches the game's 2 s ownership pass)
        private const float ShipInterval = 5f;
        private const float PlantInterval = 30f;
        private const float HeartbeatInterval = 300f;
        private const int MaxErrorsPerCheck = 5;

        private readonly Dictionary<string, int> _errors = new Dictionary<string, int>();
        private WorldMonitor _monitor;
        private PlantMonitor _plants;
        private bool _sessionActive;
        private bool _reportedNotDedicated;
        private float _nextFast, _nextShip, _nextPlant, _nextHeartbeat;

        private void Awake()
        {
            Logger.LogInfo($"{PluginName} {PluginInfo.Version} loaded. Active on dedicated servers only; logs to BepInEx/QuanChunkLoaderDiag/v3/.");
        }

        private void Update()
        {
            if (ZNet.instance == null || ZNetScene.instance == null || ZDOMan.instance == null || ZoneSystem.instance == null)
            {
                if (_sessionActive)
                {
                    DiagLog.Write("STOP", "World session ended.");
                    _sessionActive = false;
                    _monitor = null;
                    _plants = null;
                }
                return;
            }

            if (!ZNet.instance.IsDedicated())
            {
                if (!_reportedNotDedicated)
                {
                    Logger.LogInfo($"{PluginName}: not a dedicated server, staying idle.");
                    _reportedNotDedicated = true;
                }
                return;
            }

            var now = Time.realtimeSinceStartup;
            if (!_sessionActive)
            {
                _sessionActive = true;
                _monitor = new WorldMonitor();
                _plants = new PlantMonitor(_monitor.OwnerName);
                _errors.Clear();
                Guard("startup report", StartupReport.Write);
                _nextFast = now;
                _nextShip = now;
                _nextPlant = now + PlantInterval;
                _nextHeartbeat = now + HeartbeatInterval;
            }

            if (now >= _nextFast)
            {
                _nextFast = now + FastInterval;
                Guard("players", _monitor.PollPeers);
                Guard("loaders", _monitor.PollLoaders);
                Guard("ownership", _monitor.PollOwnership);
            }
            if (now >= _nextShip)
            {
                _nextShip = now + ShipInterval;
                Guard("ships and water", _monitor.PollShipsAndWater);
            }
            if (now >= _nextPlant)
            {
                _nextPlant = now + PlantInterval;
                Guard("plants", _plants.Poll);
            }
            if (now >= _nextHeartbeat)
            {
                _nextHeartbeat = now + HeartbeatInterval;
                Guard("heartbeat", _monitor.Heartbeat);
                Guard("plant heartbeat", _plants.Heartbeat);
            }
        }

        /// <summary>Runs one check in isolation. A check that keeps failing is switched off rather than spamming the log.</summary>
        private void Guard(string check, Action action)
        {
            _errors.TryGetValue(check, out var count);
            if (count >= MaxErrorsPerCheck)
            {
                return;
            }
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _errors[check] = ++count;
                var disabled = count >= MaxErrorsPerCheck ? " Check disabled for this session." : "";
                DiagLog.Write("ERROR", $"{check} failed ({count}/{MaxErrorsPerCheck}).{disabled} {ex}");
            }
        }
    }
}
