using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using UnityEngine.Events;

using Mirror;

using RTSEngine.Faction;
using RTSEngine.Game;
using RTSEngine.Lobby;
using RTSEngine.Multiplayer.Event;
using RTSEngine.Multiplayer.Lobby;
using RTSEngine.Multiplayer.Utilities;

namespace RTSEngine.Multiplayer.Mirror.Lobby
{
    public class MultiplayerLobbyManager : LobbyManagerBase<IMultiplayerLobbyFactionSlot>, IMultiplayerLobbyManager
    {
        #region Attributes
        private IMultiplayerManager multiplayerMgr;
        public override bool IsStartingLobby => multiplayerMgr.State == MultiplayerState.startingLobby 
            || (LocalFactionSlot.IsValid() && LocalFactionSlot.IsStartingLobby);

        [Space(), SerializeField, EnforceType(typeof(ILobbyFactionSlot)), Tooltip("Prefab used to represent a NPC faction slot in the lobby. This one can not have any networking related component such as the Network Identity component. You can use the lobby slot from the local single player lobby menu.")]
        private GameObject npcLobbyFactionPrefab = null;
        public IReadOnlyList<IMultiplayerLobbyFactionSlot> NPCFactionSlots => FactionSlots.Where(slot => slot.Role == FactionSlotRole.npc).ToList();

        [Space(), SerializeField, Tooltip("Event triggered when the multiplayer game is confirmed to be starting. This is triggered right before the target map scene is loaded.")]
        private UnityEvent onGameConfirmed = new UnityEvent();
        #endregion

        #region IGameBuilder
        public override bool IsMaster => multiplayerMgr.Role == MultiplayerRole.host || multiplayerMgr.Role == MultiplayerRole.server;
        public override bool CanFreezeTimeOnPause => false;

        protected override void OnGameBuiltComplete(IGameManager gameMgr)
        {
            multiplayerMgr.OnGameLoaded(gameMgr);
        }
        #endregion

        #region Initializing/Terminating
        protected override void OnInit()
        {
            base.OnInit();

            multiplayerMgr = NetworkManager.singleton as IMultiplayerManager;

            if (!logger.RequireValid(multiplayerMgr,
              $"[{GetType().Name}] A component that implements the '{typeof(IMultiplayerManager).Name}' interface can not be found!"))
                return;

            multiplayerMgr.OnLobbyLoaded(this);

            multiplayerMgr.MultiplayerStateUpdated += HandleMultiplayerStateUpdated;
        }

        protected override void OnDestroyed()
        {
            multiplayerMgr.MultiplayerStateUpdated -= HandleMultiplayerStateUpdated;
        }
        #endregion

        #region Handling Event: Multiplayer State Updated
        private void HandleMultiplayerStateUpdated(IMultiplayerManager sender, MultiplayerStateEventArgs args)
        {
            if (args.State == MultiplayerState.gameConfirmed)
                onGameConfirmed.Invoke();
        }
        #endregion

        #region Updating Lobby Game Data
        public override bool IsLobbyGameDataMaster()
        {
            return multiplayerMgr.IsValid()
                && !multiplayerMgr.IsServerOnly
                && LocalFactionSlot.IsValid()
                && LocalFactionSlot.Role == FactionSlotRole.host;
        }

        public void UpdateLobbyGameDataRequest (LobbyGameData newLobbyGameData)
        {
            // Whenever the map changes, the faction slot index seed list must also change to suit the target faction slots.
            newLobbyGameData.factionSlotIndexSeed = new List<int>();
            newLobbyGameData.factionSlotIndexSeed.AddRange(RTSHelper.GenerateRandomIndexList(Maps[newLobbyGameData.mapID].factionsAmount.max));

            LocalFactionSlot.TryUpdateLobbyGameData(newLobbyGameData);
        }

        public void UpdateLobbyGameDataComplete(LobbyGameData newLobbyGameData) => base.UpdateLobbyGameData(newLobbyGameData);

        #endregion

        #region Adding/Removing Client Factions Slots
        public override bool CanRemoveFactionSlot(IMultiplayerLobbyFactionSlot slot) => slot.IsValid();

        public void RemoveFactionSlotRequest(int slotID)
        {
            LocalFactionSlot.TryKick(slotID);
        }

        public void RemoveFactionSlotComplete(IMultiplayerLobbyFactionSlot slot) => RemoveFactionSlot(slot);

        protected override void OnFactionSlotRemoved(IMultiplayerLobbyFactionSlot slot)
        {
            if (slot.Role == FactionSlotRole.npc && multiplayerMgr.IsServer)
            { 
                NetworkServer.Destroy(slot.gameObject);
            }
        }
        #endregion

        #region Handling Event: Faction Role Updated
        protected override void HandleFactionSlotRoleUpdated(ILobbyFactionSlot slot, EventArgs args)
        {
            base.HandleFactionSlotRoleUpdated(slot, args);

            // if the role of the faction is NPC, do nothing further.
            if (slot.Role == FactionSlotRole.npc)
                return;

            // Assign the local faction slot of the local player.
            // If this is the headless server instance then set it up so that the local slot is the client host one.
            if ((multiplayerMgr.Role == MultiplayerRole.server && slot.Role == FactionSlotRole.host)
                || (slot as NetworkBehaviour).isLocalPlayer)
                LocalFactionSlot = slot as IMultiplayerLobbyFactionSlot;
        }
        #endregion

        #region Adding/Removing NPC Faction Slots
        public void AddNPCFactionSlotAttempt()
        {
            LocalFactionSlot.TryAddNPCFaction();
        }

        public void AddNPCFactionSlotComplete()
        {
            if (IsStartingLobby)
                return;

            if(FactionSlotCount >= CurrentMap.factionsAmount.max) 
            {
                playerMessageUIHandler.Message.Display($"Maximum factions amount {CurrentMap.factionsAmount.max} for this map has been reached.");
                return;
            }

            ILobbyFactionSlot newSlot = Instantiate(npcLobbyFactionPrefab.gameObject).GetComponent<ILobbyFactionSlot>();
            NetworkServer.Spawn(newSlot.gameObject, GetHostFactionSlot()?.gameObject);

            playerMessageUIHandler.Message.Display($"New NPC faction slot added!");
        }

        public void RemoveNPCFactionSlot(int slotID)
        {
            if (IsStartingLobby)
                return;

            RemoveFactionSlot(GetFactionSlot(slotID));
        }
        #endregion

        #region Starting/Leaving Lobby
        protected override void OnPreLobbyLeave()
        {
            multiplayerMgr.Stop(DisconnectionReason.playerCommand);
        }

        protected override void OnStartLobby()
        {
            LocalFactionSlot.TryStartLobby();
        }

        protected override void OnStartLobbyInterrupt()
        {
            LocalFactionSlot.TryStartLobbyInterrupt();
        }
        #endregion
    }
}
