using System.Collections.Generic;

using RTSEngine.Lobby;

namespace RTSEngine.Multiplayer.Lobby
{
    public interface IMultiplayerLobbyManager : ILobbyManager<IMultiplayerLobbyFactionSlot>
    {
        IReadOnlyList<IMultiplayerLobbyFactionSlot> NPCFactionSlots { get; }

        void UpdateLobbyGameDataRequest(LobbyGameData lobbyGameData);
        void UpdateLobbyGameDataComplete(LobbyGameData lobbyGameData);

        void RemoveFactionSlotRequest(int slotID);
        void RemoveFactionSlotComplete(IMultiplayerLobbyFactionSlot slot);

        void AddNPCFactionSlotComplete();
        void RemoveNPCFactionSlot(int slotID);
    }
}
