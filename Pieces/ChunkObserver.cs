using Jotunn.Managers;
using QuanChunkLoader.Core;
using UnityEngine;

namespace QuanChunkLoader
{
    /// <summary>
    /// The observer in the world, on every machine that has it loaded: hover text, the switch, and the glow. The
    /// switch is only a request; the server checks who may switch it and keeps the authoritative list.
    /// </summary>
    internal sealed class ChunkObserver : MonoBehaviour, Hoverable, Interactable
    {
        private ZNetView _nview;
        private Piece _piece;
        private GameObject _effect;
        private bool _shownOn;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _piece = GetComponent<Piece>();
            var effect = transform.Find(ObserverPiece.EffectChild);
            _effect = effect != null ? effect.gameObject : null;
            if (_nview == null || _nview.GetZDO() == null)
            {
                return;
            }
            _nview.Register<string, bool>(Observers.SetBoolRpc, RPC_SetBool);
            _shownOn = IsOn;
            ShowState(_shownOn);
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
        }

        /// <summary>Owner: the server asks it to set a flag (see Observers.WriteBool).</summary>
        private void RPC_SetBool(long sender, string key, bool value)
        {
            if (!_nview.IsOwner() || (key != Observers.OnKey && key != Observers.AdminKey))
            {
                return;
            }
            var server = ZNet.instance.GetServerPeer();
            if (!ZNet.instance.IsServer() && (server == null || sender != server.m_uid))
            {
                return;
            }
            _nview.GetZDO().Set(key, value);
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
            var on = IsOn;
            var text = GetHoverName() +
                       (on ? "\n<color=#7fffd4>On</color>: keeping this area loaded" : "\n<color=#a0a0a0>Off</color>") +
                       $"\nBuilt by {BuilderName}";
            if (LocalIsAdmin && _nview.GetZDO().GetBool(Observers.AdminKey))
            {
                text += "\n<color=orange>Admin-placed</color>";
            }
            if (MaySwitch(out _))
            {
                text += Localization.instance.Localize(
                    $"\n[<color=yellow><b>$KEY_Use</b></color>] {(on ? "Switch off" : "Switch on")}");
            }
            return text;
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || _nview == null || !_nview.IsValid())
            {
                return false;
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
