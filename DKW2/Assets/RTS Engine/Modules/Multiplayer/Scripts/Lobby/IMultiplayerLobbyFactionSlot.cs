using RTSEngine.Faction;
using RTSEngine.Lobby;

namespace RTSEngine.Multiplayer.Lobby
{
    public interface IMultiplayerLobbyFactionSlot : ILobbyFactionSlot
    {
        bool IsReady { get; }
        bool IsLocalPlayer { get; }
        bool IsStartingLobby { get; }

        void OnFactionSlotValidated(IMultiplayerLobbyFactionSlot newFactionSlot);

        void TryUpdateLobbyGameData(LobbyGameData newLobbyGameData);

        void TryKick(int factionSlotID);

        void TryUpdateRole(FactionSlotRole newRole);

        void TryAddNPCFaction();
        void SetReadyStatus(bool isReady);

        void TryStartLobby();
        void TryStartLobbyInterrupt();
    }
}
