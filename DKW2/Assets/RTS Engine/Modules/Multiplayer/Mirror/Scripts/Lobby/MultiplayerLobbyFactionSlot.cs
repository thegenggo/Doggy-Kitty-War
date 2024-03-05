using System;
using System.Linq;
using System.Collections.Generic;

using Mirror;

using UnityEngine;
using UnityEngine.UI;

using RTSEngine.Event;
using RTSEngine.Faction;
using RTSEngine.Lobby;
using RTSEngine.Lobby.Logging;
using RTSEngine.Multiplayer.Game;
using RTSEngine.Multiplayer.Utilities;
using RTSEngine.Lobby.UI;
using RTSEngine.UI;
using RTSEngine.Multiplayer.Lobby;
using TMPro;
using UnityEngine.EventSystems;
using RTSEngine.Multiplayer.Event;

namespace RTSEngine.Multiplayer.Mirror.Lobby
{
    public class MultiplayerLobbyFactionSlot : NetworkRoomPlayer, IMultiplayerLobbyFactionSlot
    {
        #region Attributes
        // Has this lobby slot been initialized?
        public bool IsInitialized { private set; get; } = false;

        public FactionSlotData Data => new FactionSlotData
        {
            role = Role,

            name = inputData.name,
            color = lobbyMgr.FactionColorSelector.Get(inputData.colorID),

            type = lobbyMgr.CurrentMap.GetFactionType(inputData.factionTypeID),
            npcType = lobbyMgr.CurrentMap.GetNPCType(inputData.factionTypeID, inputData.npcTypeID),

            isLocalPlayer = isLocalPlayer
        };
        private LobbyFactionSlotInputData inputData = new LobbyFactionSlotInputData();

        [SyncVar, SerializeField, HideInInspector]
        private FactionSlotRole role;
        public FactionSlotRole Role => role;

        public bool IsLocalPlayer => isLocalPlayer;
        public bool IsHostSlot => Role == FactionSlotRole.host;

        public bool IsLocalHostSlot => isLocalPlayer
            && IsHostSlot;

        public bool IsHostInstance => lobbyMgr.IsValid()
            && !lobbyMgr.IsStartingLobby
            && lobbyMgr.IsLobbyGameDataMaster();

        public bool IsStartingLobby { private set; get; } = false;

        public bool HasLocalAuthority => isOwned;

        public bool IsInteractable { private set; get; }

        [SerializeField, Tooltip("UI Image to display the faction's color.")]
        private Image factionColorImage = null;

        [SerializeField, Tooltip("UI Input Field to display and change the faction's name.")]
        private TMP_InputField factionNameInput = null;

        [SerializeField, Tooltip("UI Dropdown menu used to display the list of possible faction types that the slot can have.")]
        private TMP_Dropdown factionTypeMenu = null;
        protected virtual string RandomFactionTypeName => "Random";

        [SerializeField, Tooltip("UI Dropdown menu used to display the list of possible NPC faction types that the slot can have")]
        private TMP_Dropdown npcTypeMenu = null;

        [SerializeField, Tooltip("Button used to remove the faction slot from the lobby.")]
        private Button removeButton = null;

        [SerializeField, Tooltip("Button used to allow the player to announce they are ready to start the game or not.")]
        private Button readyToBeginButton = null;
        [SerializeField, Tooltip("Image that is activated whenever the player announces that they are ready to start the game.")]
        private Image readyImage = null;
        public bool IsReady => readyToBegin;

        // Active game
        public IMultiplayerFactionManager MultiplayerFactionMgr { get; private set; }
        public IFactionSlot GameFactionSlot { get; private set; }

        // Lobby Services
        protected IMultiplayerLobbyManager lobbyMgr { private set; get; }
        protected ILobbyLoggingService logger { private set; get; }
        protected ILobbyManagerUI lobbyUIMgr { private set; get; }
        protected ILobbyPlayerMessageUIHandler playerMessageUIHandler { private set; get; }

        // Multiplayer Services
        protected IMultiplayerManager multiplayerMgr { private set; get; }
        #endregion

        #region Raising Events
        public event CustomEventHandler<ILobbyFactionSlot, EventArgs> FactionSlotInitialized;
        private void RaiseFactionSlotInitialized()
        {
            this.lobbyMgr.AddFactionSlot(this);
            IsInitialized = true;

            var handler = FactionSlotInitialized;
            handler?.Invoke(this, EventArgs.Empty);
        }

        public event CustomEventHandler<ILobbyFactionSlot, EventArgs> FactionRoleUpdated;
        private void RaiseRoleUpdated(FactionSlotRole role)
        {
            this.role = role;

            var handler = FactionRoleUpdated;
            handler?.Invoke(this, EventArgs.Empty);
        }
        #endregion

        #region Initializing/Terminating
        // The server/client init methods are responsible for initialzing the multiplayer lobby faction slot
        public override void OnStartServer()
        {
            if (IsInitialized)
                return;

            // Find the multiplayer manager and only proceed if this the server since initializing the faction slots on host/clients uses OnClientEnterRoom() callback.
            if (!(NetworkManager.singleton as IMultiplayerManager).IsServerOnly)
                return;

            Init();
        }

        public override void OnClientEnterRoom()
        {
            if (IsInitialized)
                return;

            // The OnClientEnterRoom (Mirror) handles spawning this lobby player object.
            // Therefore, we use this callback to know when the client enters the room and initialize their lobby slot here
            Init();
        }

        private void Init()
        {
            this.multiplayerMgr = NetworkManager.singleton as IMultiplayerManager;
            this.lobbyMgr = multiplayerMgr.CurrentLobby;

            // Get services
            this.logger = lobbyMgr.GetService<ILobbyLoggingService>();
            this.lobbyUIMgr = lobbyMgr.GetService<ILobbyManagerUI>();
            this.playerMessageUIHandler = lobbyMgr.GetService<ILobbyPlayerMessageUIHandler>();

            if (!logger.RequireValid(factionColorImage, $"[{GetType().Name}] The field 'Faction Color Image' is required!")
                || !logger.RequireValid(factionTypeMenu, $"[{GetType().Name}] The field 'Faction Type Menu' is required!")
                || !logger.RequireValid(npcTypeMenu, $"[{GetType().Name}] The field 'NPC Type Menu' is required!")
                || !logger.RequireValid(removeButton, $"[{GetType().Name}] The field 'Remove Button' is required!")
                || !logger.RequireValid(readyToBeginButton, $"[{GetType().Name}] The field 'Ready Button' is required!")
                || !logger.RequireValid(readyImage, $"[{GetType().Name}] The field 'Ready Image' is required!"))
                return;

            EventTrigger factionColorImageEventTrigger = factionColorImage.GetComponent<EventTrigger>();
            EventTrigger.Entry entry = new EventTrigger.Entry();
            entry.eventID = EventTriggerType.PointerClick;
            entry.callback.AddListener((eventData) => { OnFactionColorUpdated(); });
            factionColorImageEventTrigger.triggers.Add(entry);

            factionNameInput.onEndEdit.AddListener(OnFactionNameUpdated);
            factionTypeMenu.onValueChanged.AddListener(OnFactionTypeUpdated);
            npcTypeMenu.onValueChanged.AddListener(OnNPCTypeUpdated);
            removeButton.onClick.AddListener(OnRemove);
            readyToBeginButton.onClick.AddListener(ToggleReadyStatus);

            if (isOwned)
                role = lobbyMgr.FactionSlotCount == 0
                    ? FactionSlotRole.host
                    : (lobbyMgr.LocalFactionSlot.IsValid() ? FactionSlotRole.npc : FactionSlotRole.client);

            this.lobbyMgr.LobbyGameDataUpdated += HandleLobbyGameDataUpdated;
            this.lobbyMgr.FactionSlotRemoved += HandleFactionSlotRemoved;
            this.multiplayerMgr.MultiplayerFactionManagerValidated += HandleMultiplayerFactionManagerValidated;
            this.multiplayerMgr.MultiplayerStateUpdated += HandleMultiplayerStateUpdated;

            // Every faction slot starts with the same default input data
            ResetInputData();

            // By default, the faction slot is not interactable until it is validated.
            SetInteractable(false);

            // Since this Init method is called either from a client or headless server instance and not created in the lobby manager and then initiated here
            // We need to add the faction slot so that the lobby manager can keep track of it.
            RaiseFactionSlotInitialized();

            if (isOwned)
                CmdValidateSlot(localGameCode: lobbyMgr.GameCode, preValidatedRole: role);
        }

        private void OnDestroy()
        {
            this.lobbyMgr.LobbyGameDataUpdated -= HandleLobbyGameDataUpdated;
            this.lobbyMgr.FactionSlotRemoved -= HandleFactionSlotRemoved;

            this.multiplayerMgr.MultiplayerFactionManagerValidated -= HandleMultiplayerFactionManagerValidated;
            this.multiplayerMgr.MultiplayerStateUpdated -= HandleMultiplayerStateUpdated;
        }
        #endregion

        #region Post-Initializing: Validating Client
        // Here we validate that the new faction slot can be added to the lobby or not
        // Client associated to faction slot can get kicked if they are running a different game version, the game is already starting or lobby reached max slots
        // If the client is good to join, then validate its role to all other slots in lobby
        [Command]
        private void CmdValidateSlot(string localGameCode, FactionSlotRole preValidatedRole)
        {
            // In case this is the server, this object might not have been initialized yet so fetch it directly.
            if (!multiplayerMgr.IsValid())
                multiplayerMgr = NetworkManager.singleton as IMultiplayerManager;
            if (!lobbyMgr.IsValid())
                lobbyMgr = multiplayerMgr.CurrentLobby;

            // If the game is already starting then no new clients are allowed to join.
            if (lobbyMgr.IsStartingLobby)
            {
                KickOnServerInstance(factionSlotID: index, reason: DisconnectionReason.lobbyAlreadyStarting);
                return;
            }
            // If the client's game code does not match with the server, kick the client.
            else if (localGameCode != lobbyMgr.GameCode)
            {
                KickOnServerInstance(factionSlotID: index, reason: DisconnectionReason.gameCodeMismatch);
                return;
            }
            else if (lobbyMgr.FactionSlotCount > lobbyMgr.CurrentMap.factionsAmount.max)
            {
                KickOnServerInstance(factionSlotID: index, reason: DisconnectionReason.lobbyMapMaxFactions);
                return;
            }

            // If this game instance is the headless server then update the input directly as the RPC call will not be called on the headless server
            // Add the faction slot to the manager here for the same above reason
            if (multiplayerMgr.IsServerOnly)
                OnSlotValidated(preValidatedRole);
            RpcOnSlotValidated(preValidatedRole);
        }

        // OnSlotValidated
        // Source: Server/Host - CmdValidateSlot
        // Target: ServerOnly (Direct), Otherwise (via RpcOnSlotValidated)
        [ClientRpc]
        private void RpcOnSlotValidated(FactionSlotRole validatedRole) => OnSlotValidated(validatedRole);

        private void OnSlotValidated(FactionSlotRole validatedRole)
        {
            RaiseRoleUpdated(validatedRole);

            if (validatedRole == FactionSlotRole.npc)
                ValidateNPCPlayer();
            else
            {
                if (isLocalPlayer)
                    ValidateLocalPlayer();
                else
                    ValidateNonLocalPlayer();
            }
        }

        public void ValidateNPCPlayer()
        {
            // Only make the NPC slot interactable in the host's instance
            if (!IsHostInstance)
                return;

            SetInteractable(true);

            TryUpdateInputData(this.inputData);

            SetReadyStatus(lobbyMgr.LocalFactionSlot.IsReady);

            lobbyMgr.LocalFactionSlot.OnFactionSlotValidated(this);
        }

        public void ValidateLocalPlayer()
        {
            // Host player sets the default map.
            if (IsLocalHostSlot)
            {
                lobbyUIMgr.SetInteractable(true);
                TryUpdateLobbyGameData(lobbyMgr.CurrentLobbyGameData);
            }

            SetInteractable(true);

            TryUpdateInputData(this.inputData);
        }

        public void ValidateNonLocalPlayer()
        {
            // New faction slot added by a new player -> sync the local faction slot input data to this one.
            lobbyMgr.LocalFactionSlot.OnFactionSlotValidated(this);

            // If the local faction slot is the host then go through each NPC faction slot and sync their input data
            if (IsHostInstance)
                foreach (IMultiplayerLobbyFactionSlot slot in lobbyMgr.NPCFactionSlots)
                    slot.OnFactionSlotValidated(this);
        }
        #endregion

        #region Update Faction Slot Role
        public void TryUpdateRole(FactionSlotRole newRole)
        {
            // Only the headless server instance can update the role of a faction slot
            if (!multiplayerMgr.IsServerOnly)
                return;

            UpdateRole(newRole); 
            RpcUpdateRole(newRole);
        }

        // UpdateRole
        // Source: ServerOnly - TryUpdateRole
        // Target: ServerOnly (Direct), Otherwise (via RpcUpdateRole)

        [ClientRpc]
        private void RpcUpdateRole(FactionSlotRole newRole) => UpdateRole(newRole);

        private void UpdateRole(FactionSlotRole newRole)
        {
            RaiseRoleUpdated(newRole);

            // If the game has already started then reassign the host in the game faction slots.
            if (multiplayerMgr.State == MultiplayerState.game)
            {
                MultiplayerFactionMgr.GameFactionSlot.UpdateRole(newRole);

                // Stop here as we do not need to update interactibility on the lobby UI since an active game is underway.
                return;
            }

            // Only the host is able to pick the lobby game data
            lobbyUIMgr.SetInteractable(IsHostInstance);
        }
        #endregion

        #region Updating Faction Slot Input Data
        private void ResetInputData()
        {
            inputData = new LobbyFactionSlotInputData
            {
                name = role == FactionSlotRole.npc ? "npc" : "player",

                colorID = lobbyMgr.FactionColorSelector.GetRandomIndex(),

                factionTypeID = 0,
                npcTypeID = 0
            };
        }

        public void OnFactionSlotValidated(IMultiplayerLobbyFactionSlot newFactionSlot)
        {
            // Will only go through if this is the host's slot
            TryUpdateLobbyGameData(lobbyMgr.CurrentLobbyGameData);

            // Sync the local faction's input data so that the new faction slot can see them
            TryUpdateInputData(this.inputData);
        }

        private void TryUpdateInputData(LobbyFactionSlotInputData inputData)
        {
            if (!HasLocalAuthority)
                return;

            CmdUpdateInputData(inputData);
        }

        // UpdateInputData
        // Source: Client with Authority - TryUpdateInputData
        // Intermediate: ToServer = CmdUpdateInputData
        // Target: ServerOnly (Direct), Otherwise (via RpcUpdateInputData)

        [Command]
        private void CmdUpdateInputData(LobbyFactionSlotInputData inputData)
        {
            // Let the host/server pick the random faction type
            if (inputData.isFactionTypeRandom)
            {
                inputData.factionTypeID = UnityEngine.Random.Range(0, lobbyMgr.CurrentMap.GetFactionTypes().Count);
            }

            if (multiplayerMgr.IsServerOnly)
                UpdateInputData(inputData);
            RpcUpdateInputData(inputData);
        }

        [ClientRpc]
        private void RpcUpdateInputData(LobbyFactionSlotInputData inputData) =>  UpdateInputData(inputData);

        private void UpdateInputData(LobbyFactionSlotInputData inputData)
        {
            this.inputData = inputData;
            RefreshInputDataUI();
        }

        private void RefreshInputDataUI()
        {
            factionNameInput.text = inputData.name;

            factionColorImage.color = this.lobbyMgr.FactionColorSelector.Get(inputData.colorID);

            if(!inputData.isPrevFactionTypeRandom || !inputData.isFactionTypeRandom)
                ResetNPCType(prevMapID: -1);

            UpdateFactionTypeMenuValue();

            inputData.isPrevFactionTypeRandom = factionTypeMenu.value == factionTypeMenu.options.Count - 1;

            npcTypeMenu.value = inputData.npcTypeID;
        }
        #endregion

        #region Updating Lobby Game Data
        public void TryUpdateLobbyGameData(LobbyGameData newLobbyGameData)
        {
            if (!IsLocalHostSlot)
                return;

            CmdUpdateLobbyGameData(newLobbyGameData);
        }

        [Command]
        private void CmdUpdateLobbyGameData(LobbyGameData gameData)
        {
            if (multiplayerMgr.IsServerOnly)
                lobbyMgr.UpdateLobbyGameDataComplete(gameData);
            RpcUpdateLobbyGameData(gameData);
        }

        // UpdateLobbyGameData
        // Source: Host Slot - TryUpdateLobbyGameData
        // Intermediate: ToServer - CmdUpdateLobbyGameData
        // Target: ServerOnly (Direct), Otherwise (via RpcUpdateLobbyGameData)

        [ClientRpc]
        private void RpcUpdateLobbyGameData(LobbyGameData gameData) => UpdateLobbyGameData(gameData);

        private void UpdateLobbyGameData (LobbyGameData gameData) => lobbyMgr.UpdateLobbyGameDataComplete(gameData);

        private void HandleLobbyGameDataUpdated(LobbyGameData prevLobbyGameData, EventArgs args)
        {
            ResetFactionType(prevMapID: prevLobbyGameData.mapID);
        }
        #endregion

        #region General UI Handling
        public void SetInteractable(bool interactable)
        {
            factionNameInput.interactable = interactable;
            factionTypeMenu.interactable = interactable;

            npcTypeMenu.gameObject.SetActive(Role == FactionSlotRole.npc);
            npcTypeMenu.interactable = interactable && Role == FactionSlotRole.npc;

            readyToBeginButton.interactable = interactable && Role != FactionSlotRole.npc;

            removeButton.gameObject.SetActive(lobbyMgr.LocalFactionSlot?.Role == FactionSlotRole.host && !isLocalPlayer);
            removeButton.interactable = lobbyMgr.LocalFactionSlot?.Role == FactionSlotRole.host && !isLocalPlayer;

            IsInteractable = interactable;
        }
        #endregion

        #region Updating Faction Name
        private void OnFactionNameUpdated(string newValue)
        {
            if (!HasLocalAuthority || string.IsNullOrEmpty(factionNameInput.text.Trim()))
            {
                factionNameInput.text = inputData.name;
                return;
            }

            inputData.name = factionNameInput.text.Trim();

            TryUpdateInputData(inputData);
        }
        #endregion

        #region Updating Faction Type
        private void ResetFactionType(int prevMapID)
        {
            int prevFactionTypeIndex = inputData.factionTypeID;

            List<string> factionTypeOptions = lobbyMgr.CurrentMap.GetFactionTypes().Select(type => type.Name).ToList();
            // Last faction type option in the list is the random one
            factionTypeOptions.Add(RandomFactionTypeName);

            if (inputData.isFactionTypeRandom)
            {
                factionTypeMenu.value = factionTypeOptions.Count - 1;
            }
            else
            {
                RTSHelper.UpdateDropdownValue(ref factionTypeMenu,
                    lastOption: lobbyMgr.GetMap(prevMapID).GetFactionType(inputData.factionTypeID).Name,
                    newOptions: factionTypeOptions);

                inputData.factionTypeID = factionTypeMenu.value;
            }

            ResetNPCType(prevMapID);
        }

        private void OnFactionTypeUpdated(int newOption)
        {
            if (!HasLocalAuthority)
            {
                UpdateFactionTypeMenuValue();
                return;
            }

            inputData.prevFactionTypeID = inputData.factionTypeID;

            inputData.factionTypeID = factionTypeMenu.value;
            inputData.isFactionTypeRandom = factionTypeMenu.value == factionTypeMenu.options.Count - 1;

            TryUpdateInputData(inputData);
        }

        private void UpdateFactionTypeMenuValue()
        {
            // Last faction type option in the list is the random one
            factionTypeMenu.value = inputData.isFactionTypeRandom
                ? factionTypeMenu.options.Count - 1
                : inputData.factionTypeID;
        }
        #endregion

        #region Updating Color
        private void OnFactionColorUpdated()
        {
            if (!HasLocalAuthority)
                return;

            inputData.colorID = lobbyMgr.FactionColorSelector.GetNextIndex(inputData.colorID);

            TryUpdateInputData(inputData);
        }
        #endregion

        #region Updating NPC Type
        private void ResetNPCType(int prevMapID)
        {
            RTSHelper.UpdateDropdownValue(ref npcTypeMenu,
                lastOption: lobbyMgr.GetMap(prevMapID).GetNPCType(inputData.prevFactionTypeID, inputData.npcTypeID).Name,
                newOptions: (inputData.isFactionTypeRandom
                    ? lobbyMgr.CurrentMap.GetRandomFactionTypeNPCTypes()
                    : lobbyMgr.CurrentMap.GetNPCTypes(inputData.factionTypeID))
                    .Select(type => type.Name).ToList());

            inputData.npcTypeID = npcTypeMenu.value;
        }

        private void OnNPCTypeUpdated(int newOption)
        {
            if (!HasLocalAuthority)
            {
                npcTypeMenu.value = inputData.npcTypeID;
                return;
            }

            if(!inputData.isPrevFactionTypeRandom)
                inputData.prevFactionTypeID = inputData.factionTypeID;

            inputData.npcTypeID = npcTypeMenu.value;

            TryUpdateInputData(inputData);
        }
        #endregion

        #region Updating Ready Status
        public void SetReadyStatus(bool isReady)
        {
            if(!HasLocalAuthority)
                return;

            CmdChangeReadyState(isReady);
        }

        private void ToggleReadyStatus()
        {
            if(!IsLocalPlayer)
                return;

            bool nextReadyStatus = !readyToBegin;
            CmdChangeReadyState(nextReadyStatus);

            SyncNPCReadyStatus(nextReadyStatus);
        }

        private void SyncNPCReadyStatus(bool isReady)
        {
            if (!IsLocalHostSlot)
                return;

            foreach (IMultiplayerLobbyFactionSlot slot in lobbyMgr.FactionSlots)
                if (slot.Role == FactionSlotRole.npc)
                    slot.SetReadyStatus(isReady);
        }

        public override void ReadyStateChanged(bool _, bool newReadyState)
        {
            readyImage.gameObject.SetActive(newReadyState);
        }
        #endregion

        #region Removing Faction Slot
        public void OnRemove()
        {
            lobbyMgr.RemoveFactionSlotRequest(lobbyMgr.GetFactionSlotID(this));
        }

        // Kick
        // Source: Any Slot on ServerOnly or Host Slot at HostInstance - TryKick

        // Kick (Source: ServerOnly)
        // Intermediate: Server - KickOnServerInstance
        // Target: Client - RpcKick

        // Kick (Source: HostInstance)
        // Intermediate: Server - CmdKick then Server - KickOnServerInstance
        // Target: Client - RpcKick

        public void TryKick(int factionSlotID)
        {
            if (multiplayerMgr.IsServerOnly)
            {
                KickOnServerInstance(factionSlotID, DisconnectionReason.serverKick);
            }
            else if (IsLocalHostSlot)
            {
                CmdKick(factionSlotID, DisconnectionReason.lobbyHostKick);
            }
        }

        [Command]
        private void CmdKick(int factionSlotID, DisconnectionReason reason)
        {
            KickOnServerInstance(factionSlotID, reason);
        }

        private void KickOnServerInstance(int factionSlotID, DisconnectionReason reason)
        {
            if (multiplayerMgr.IsServerOnly 
                && lobbyMgr.GetFactionSlot(factionSlotID).Role == FactionSlotRole.npc)
            {
                lobbyMgr.RemoveNPCFactionSlot(factionSlotID);
            }

            RpcKick(factionSlotID, reason);
        }

        [ClientRpc]
        private void RpcKick(int factionSlotID, DisconnectionReason reason)
        {
            // Kicking player in game:
            if (multiplayerMgr.CurrentGameMgr.IsValid())
            {
                if (factionSlotID == lobbyMgr.LocalFactionSlot.GameFactionSlot.ID)
                    multiplayerMgr.Stop(reason);

                return;
            }

            IMultiplayerLobbyFactionSlot nextSlot = lobbyMgr.GetFactionSlot(factionSlotID);

            // Kicking NPC faction:
            if (nextSlot.Role == FactionSlotRole.npc)
            {
                lobbyMgr.RemoveNPCFactionSlot(factionSlotID);
                return;
            }

            // Kicking client in lobby:

            // Only apply the lobby departure for the local player since we will close their connection from their end to complete the kick.
            // This is in case the local player is still connected...
            if (nextSlot.IsLocalPlayer)
            {
                multiplayerMgr.Stop(reason);
            }
        }

        private void HandleFactionSlotRemoved(ILobbyFactionSlot removedSlot, EventArgs args)
        {
            // Only if the game is active
            if (!multiplayerMgr.CurrentGameMgr.IsValid()
                || !multiplayerMgr.ServerGameMgr.IsValid())
                return;

            multiplayerMgr.CurrentGameMgr.OnFactionDefeated(removedSlot.GameFactionSlot.ID);
        }
        #endregion

        #region Starting Lobby
        public void TryStartLobby()
        {
            if (!IsLocalHostSlot)
            {
                playerMessageUIHandler.Message.Display("Only host is allowed to start the game!");
                return;
            }

            CmdStartLobby();
        }

        // UpdateRole
        // Source: Host Slot - TryStartLobby
        // Intermediate: Server - CmdStartLobby (call StartLobby on multiplayer manager on server)
        // Target: Clients - RpcStartLobby

        [Command]
        private void CmdStartLobby()
        {
            // Called by another faction slot other than the host? deny it.
            if (!IsHostSlot)
                return;

            ErrorMessage startLobbyError = multiplayerMgr.StartLobby();

            RpcStartLobby(startLobbyError);
        }

        [ClientRpc]
        private void RpcStartLobby(ErrorMessage errorMessage)
        {
            switch (errorMessage)
            {
                case ErrorMessage.none:

                    // Disable allowing any input on all faction slots and wait for the game to start.
                    foreach (ILobbyFactionSlot slot in lobbyMgr.FactionSlots)
                        slot.SetInteractable(false);

                    lobbyUIMgr.SetInteractable(false);

                    playerMessageUIHandler.Message.Display("Starting game...");

                    IsStartingLobby = true;
                    break;

                case ErrorMessage.lobbyMinSlotsUnsatisfied:
                case ErrorMessage.lobbyMaxSlotsUnsatisfied:

                    if (Role == FactionSlotRole.host)
                        playerMessageUIHandler.Message.Display($"Amount of faction slots must be between {lobbyMgr.CurrentMap.factionsAmount.min} and {lobbyMgr.CurrentMap.factionsAmount.max}!");
                    break;

                case ErrorMessage.lobbyPlayersNotAllReady:

                    if (Role == FactionSlotRole.host)
                        playerMessageUIHandler.Message.Display($"Not all faction slots are ready!");
                    break;

                default:

                    // Only display failure error to the host since it was the one that attempted to start the game.
                    if (Role == FactionSlotRole.host)
                        playerMessageUIHandler.Message.Display(errorMessage.ToString(), MessageType.error);
                    break;
            }
        }

        public void TryStartLobbyInterrupt()
        {
            if (multiplayerMgr.IsServerOnly)
            {
                multiplayerMgr.InterruptStartLobby();
                RpcHandleStartLobbyInterrupted();
            }
            else
                CmdHandleStartLobbyInterrupted();
        }

        [Command]
        private void CmdHandleStartLobbyInterrupted()
        {
            multiplayerMgr.InterruptStartLobby();
            RpcHandleStartLobbyInterrupted();
        }

        [ClientRpc]
        private void RpcHandleStartLobbyInterrupted() => HandleStartLobbyInterrupted();

        private void HandleStartLobbyInterrupted()
        {
            // Allow players to edit their faction slots again.
            lobbyMgr.LocalFactionSlot.SetInteractable(true);

            lobbyUIMgr.SetInteractable(IsLocalHostSlot);

            playerMessageUIHandler.Message.Display("Game start interrupted!");


            IsStartingLobby = false;
        }
        #endregion

        #region Handling Active Game
        private void HandleMultiplayerStateUpdated(IMultiplayerManager sender, MultiplayerStateEventArgs args)
        {
            if (multiplayerMgr.State == MultiplayerState.gameConfirmed)
                IsStartingLobby = false;
        }

        private void HandleMultiplayerFactionManagerValidated(IMultiplayerFactionManager newMultiFactionMgr, EventArgs args)
        {
            if (!multiplayerMgr.CurrentGameMgr.IsValid())
                return;

            this.MultiplayerFactionMgr = newMultiFactionMgr;
        }

        public void OnGameBuilt(IFactionSlot gameFactionSlot)
        {
            this.GameFactionSlot = gameFactionSlot;
        }
        #endregion

        #region Adding/Removing NPC Factions
        public void TryAddNPCFaction()
        {
            if (!IsLocalHostSlot)
                return;

            CmdAddNPCFaction();
        }

        [Command]
        private void CmdAddNPCFaction()
        {
            lobbyMgr.AddNPCFactionSlotComplete();
        }
        #endregion
    }
}
