using RTSEngine.Determinism;
using RTSEngine.Faction;
using RTSEngine.Multiplayer.Utilities;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RTSEngine.Multiplayer.Game
{
    public interface IMultiplayerFactionManager : IInputAdder, IMonoBehaviour
    {
        bool IsInitialized { get; }
        bool IsValidated { get; }

        IFactionSlot GameFactionSlot { get; }

        int CurrTurn { get; }
        int LastInputID { get; }

        bool IsSimPaused { get; }
        double LastRTT { get; }

        void OnClientValidatedServer(bool allClientsValidated, int validatedAmount);

        void RelayInput(IEnumerable<MultiplayerInputWrapper> relayedInputs, int lastInputID, int serverTurn, float relayedRTT);

        void PauseSimulation(bool enable);
        void OnNPCFactionPreInit(int npcFactionID);
    }
}
