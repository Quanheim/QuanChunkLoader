using System.Collections.Generic;
using Jotunn.Managers;
using QuanChunkLoader.Core;
using UnityEngine;

namespace QuanChunkLoader
{
    /// <summary>
    /// A Chunk Observer in the world, on every machine that has it loaded: hover text, the switch, and the glow.
    /// The switch is only a request; the server checks who may switch it and the limits, and keeps the
    /// authoritative list.
    /// </summary>
    internal sealed class ChunkObserver : MonoBehaviour, Hoverable, Interactable
    {
        private ZNetView _nview;
        private Piece _piece;
        private GameObject _effect;
        private Material _columnMaterial; // this observer's own copy, so its runes can change colour
        private Color _onGlow;
        private bool _shownOn;

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        // Observers loaded on this machine, for despawn protection on a player's game (which doesn't know the
        // server's loaded zones).
        private static readonly List<ChunkObserver> Loaded = new List<ChunkObserver>();

        /// <summary>Whether a switched-on observer loaded on this machine keeps this zone loaded.</summary>
        internal static bool AnyOnCovering(Vector2s zone)
        {
            foreach (var o in Loaded)
            {
                if (o == null || !o.IsOn)
                {
                    continue;
                }
                var c = ZoneSystem.GetZone(o.transform.position);
                var r = ObserverPiece.RadiusOf(o._nview.GetZDO().GetPrefab());
                if (Mathf.Abs(zone.x - c.x) <= r && Mathf.Abs(zone.y - c.y) <= r)
                {
                    return true;
                }
            }
            return false;
        }

        // Frogger's original look: red runes while off (Color.red * 6, as his mod used), the model's own blue while on.
        private static readonly Color OffGlow = new Color(6f, 0f, 0f, 1f);

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _piece = GetComponent<Piece>();
            var effect = transform.Find(ObserverPiece.EffectChild);
            _effect = effect != null ? effect.gameObject : null;
            TakeOwnMaterial();
            if (_nview == null || _nview.GetZDO() == null)
            {
                ShowState(false); // the placement ghost: shown as a new, switched-off observer
                return;
            }
            _nview.Register<string, bool>(Observers.SetBoolRpc, RPC_SetBool);
            _nview.Register<string, long>(Observers.SetLongRpc, RPC_SetLong);
            Loaded.Add(this);
            _shownOn = IsOn;
            ShowState(_shownOn);
        }

        /// <summary>
        /// Gives every part drawn with the column material (the columns and the cluster's plinths) one copy of it.
        /// The copy is changed, not a property block: the hammer's hover highlight writes and then clears the glow
        /// through property blocks (WearNTear.Highlight), which would undo a block of ours.
        /// </summary>
        private void TakeOwnMaterial()
        {
            var shared = ObserverPiece.ColumnMaterial;
            if (shared == null || !shared.HasProperty(EmissionColor))
            {
                return;
            }
            _columnMaterial = new Material(shared);
            _onGlow = shared.GetColor(EmissionColor);
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r.sharedMaterial == shared)
                {
                    r.sharedMaterial = _columnMaterial;
                }
            }
        }

        private void Start()
        {
            // Just built by this player: record their name and tell the server, which notes whether an admin built it.
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner() || Player.m_localPlayer == null)
            {
                return;
            }
            var zdo = _nview.GetZDO();
            if (zdo.GetString(Observers.BuilderKey) == "" && _piece.GetCreator() == Player.m_localPlayer.GetPlayerID())
            {
                zdo.Set(Observers.BuilderKey, Player.m_localPlayer.GetPlayerName());
                ZRoutedRpc.instance.InvokeRoutedRPC(Observers.PlacedRpc, zdo.m_uid);
            }
        }

        private bool IsOn => _nview != null && _nview.IsValid() && _nview.GetZDO().GetBool(Observers.OnKey);

        // Reads the synced state every frame, so the glow changes for everyone nearby as soon as the data arrives.
        private void Update()
        {
            if (ZInput.GetButtonDown("AltPlace") && LookedAtByLocalPlayer())
            {
                ShowZones();
            }
            var on = IsOn;
            if (on != _shownOn)
            {
                _shownOn = on;
                ShowState(on);
            }
        }

        private void ShowState(bool on)
        {
            if (_effect != null)
            {
                _effect.SetActive(on);
            }
            if (_columnMaterial != null)
            {
                _columnMaterial.SetColor(EmissionColor, on ? _onGlow : OffGlow);
            }
        }

        private bool LookedAtByLocalPlayer()
        {
            var player = Player.m_localPlayer;
            var hover = player != null ? player.GetHoverObject() : null;
            return hover != null && hover.GetComponentInParent<ChunkObserver>() == this;
        }

        /// <summary>Draws the zones this observer loads, on this player's machine (Frogger's mod had this too).</summary>
        private void ShowZones()
        {
            if (_nview == null || !_nview.IsValid())
            {
                return;
            }
            ZoneGrid.Show(ZoneSystem.GetZone(transform.position), ObserverPiece.RadiusOf(_nview.GetZDO().GetPrefab()));
        }

        private void OnDestroy()
        {
            Loaded.Remove(this);
            if (_columnMaterial != null)
            {
                Destroy(_columnMaterial);
            }
        }

        /// <summary>Owner: the server asks it to set a flag (see Observers.WriteBool).</summary>
        private void RPC_SetBool(long sender, string key, bool value)
        {
            if (FromServerToOwner(sender) && (key == Observers.OnKey || key == Observers.AdminKey || key == Observers.BlockedKey))
            {
                _nview.GetZDO().Set(key, value);
            }
        }

        private void RPC_SetLong(long sender, string key, long value)
        {
            if (FromServerToOwner(sender) && key == Observers.RefreshedKey)
            {
                _nview.GetZDO().Set(key, value);
            }
        }

        private bool FromServerToOwner(long sender)
        {
            if (!_nview.IsOwner())
            {
                return false;
            }
            if (ZNet.instance.IsServer())
            {
                return true;
            }
            var server = ZNet.instance.GetServerPeer();
            return server != null && sender == server.m_uid;
        }

        private string BuilderName
        {
            get
            {
                var name = _nview.GetZDO().GetString(Observers.BuilderKey);
                return string.IsNullOrEmpty(name) ? "unknown" : name;
            }
        }

        private static bool LocalIsAdmin => SynchronizationManager.Instance.PlayerIsAdmin;

        private bool MaySwitch(out string refusal)
        {
            refusal = null;
            if (Settings.BuilderOrAdminSwitches.Value)
            {
                if (_piece.IsCreator() || LocalIsAdmin)
                {
                    return true;
                }
                refusal = $"Only {BuilderName} or an admin can switch this observer.";
                return false;
            }
            if (PrivateArea.CheckAccess(transform.position))
            {
                return true;
            }
            refusal = "A ward here stops you switching this observer.";
            return false;
        }

        public string GetHoverName() => _piece != null ? Localization.instance.Localize(_piece.m_name) : "";

        public float GetHoverOffset() => 0f;

        public string GetHoverText()
        {
            if (_nview == null || !_nview.IsValid())
            {
                return "";
            }
            var zdo = _nview.GetZDO();
            var on = IsOn;
            var size = ObserverPiece.SizeLabel(zdo.GetPrefab());
            var text = GetHoverName() +
                       (on ? $"\n<color=#7fffd4>On</color>: keeping {size} zones loaded" : $"\n<color=#a0a0a0>Off</color> (loads {size} zones)") +
                       $"\nBuilt by {BuilderName}";
            if (zdo.GetBool(Observers.BlockedKey))
            {
                text += "\n<color=#ff8080>Built while only admins could build these: it stays off</color>";
            }
            if (LocalIsAdmin && zdo.GetBool(Observers.AdminKey))
            {
                text += "\n<color=orange>Admin-placed</color>";
            }
            if (MaySwitch(out _))
            {
                text += Localization.instance.Localize(
                    $"\n[<color=yellow><b>$KEY_Use</b></color>] {(on ? "Switch off" : "Switch on")}");
            }
            text += Localization.instance.Localize("\n[<color=yellow><b>$KEY_AltPlace</b></color>] Show its zones for 2m");
            return text;
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || _nview == null || !_nview.IsValid())
            {
                return false;
            }
            if (alt)
            {
                ShowZones(); // AltPlace held while pressing Use, as Frogger's mod did
                return true;
            }
            if (!MaySwitch(out var refusal))
            {
                user.Message(MessageHud.MessageType.Center, refusal);
                return true;
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(Observers.SwitchRpc, _nview.GetZDO().m_uid, !IsOn);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }
}
