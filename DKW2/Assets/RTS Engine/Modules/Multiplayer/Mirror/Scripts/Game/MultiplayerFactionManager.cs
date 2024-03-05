using System;
using System.Collections.Generic;
using System.Linq;

using Mirror;

using RTSEngine.Determinism;
using RTSEngine.Event;
using RTSEngine.Faction;
using RTSEngine.Game;
using RTSEngine.Logging;
using RTSEngine.Multiplayer.Game;
using RTSEngine.Multiplayer.Logging;
using RTSEngine.Multiplayer.Utilities;
using RTSEngine.ResourceExtension;
using UnityEngine;

namespace RTSEngine.Multiplayer.Mirror.Game
{
    public class MultiplayerFactionManager : NetworkBehaviour, IMultiplayerFactionManager
    {
        #region Attributes
        /// <summary>
        /// Has the multiplayer faction manager asked to get validated after the local game has been initialized?
        /// </summary>
        public bool IsInitialized { private set; get; } = false;

        /// <summary>
        /// Is the multiplayer faction manager validated by the server?
        /// </summary>
        public bool IsValidated { private set; get; } = false;

        public IFactionSlot GameFactionSlot { private set; get; }

        public int CurrTurn { private set; get; } = -1;
        public int LastInputID { private set; get; } = -1;

        private List<CommandInput> relayedInputs = new List<CommandInput>();
        public bool IsSimPaused { private set; get; }

        // Holds added inputs received before all clients are cached to re-send them as soon as the server informs this instance that all clients are ready to get inputs
        private List<CommandInput> preValidationInputCache = new List<CommandInput>();

        public double LastRTT => NetworkTime.rtt;

        [SerializeField, SyncVar, HideInInspector]
        private bool isNPCFaction = false;
        [SerializeField, SyncVar, HideInInspector]
        private int NPCFactionID = -1;

        public bool IsLocalPlayerController => isOwned;
        public bool IsNPCController => (isNPCFaction && NetworkServer.active);

        // Ping
        [SerializeField, Tooltip("Resource type used to store the value of the ping.")]
        private ResourceTypeInfo pingResource = null;

        // Multiplayer Services
        protected IMultiplayerLoggingService logger { private set; get; }

        // Game Services
        protected IGlobalEventPublisher globalEvent { private set; get; }
        protected IInputManager inputMgr { private set; get; }
        protected ITimeModifier timeModifier { private set; get; }
        protected IPlayerMessageHandler playerMessage { private set; get; }
        protected IResourceManager resourceMgr { private set; get; }

        // Other components
        protected IMultiplayerManager multiplayerMgr { private set; get; }
        protected IGameManager gameMgr => this.multiplayerMgr.CurrentGameMgr;
        #endregion

        #region NPC Faction PreInit
        public void OnNPCFactionPreInit(int npcFactionID)
        {
            NPCFactionID = npcFactionID;
            isNPCFaction = true;
        }
        #endregion

        #region Pre-Initializing/Post-Terminating: Server Only
        public override void OnStartServer()
        {
            if (IsInitialized)
                return;

            // Find the multiplayer manager and only proceed if this the server since initializing the faction slots on host/clients uses OnClientEnterRoom() callback.
            if (!(NetworkManager.singleton as IMultiplayerManager).IsServerOnly)
                return;

            PreInit();
        }

        public override void OnStartClient()
        {
            if (IsInitialized)
                return;

            // The RoomNetworkManager (Mirror) handles spawning this lobby player object.
            // Therefore, we use this callback to know when the client enters the room and initialize their lobby slot here
            PreInit();
        }

        private void PreInit()
        {
            this.multiplayerMgr = (NetworkManager.singleton as IMultiplayerManager);
            this.logger = multiplayerMgr.GetService<IMultiplayerLoggingService>(); 

            this.globalEvent = gameMgr.GetService<IGlobalEventPublisher>();
            this.inputMgr = gameMgr.GetService<IInputManager>();
            this.timeModifier = gameMgr.GetService<ITimeModifier>();
            this.playerMessage = gameMgr.GetService<IPlayerMessageHandler>();
            this.resourceMgr = gameMgr.GetService<IResourceManager>();

            IsValidated = false;
            IsInitialized = false;
            PauseSimulationFinal(enable: true);
        }

        // Use the Update method to complete the initialization process since we have to wait for the game manager to load
        // while this object is created by the multiplayer manager before the demo scene is loaded.
        private void Update()
        {
            if (!multiplayerMgr.IsValid()
                || IsInitialized
                || !multiplayerMgr.CurrentGameMgr.IsValid())
                return;

            Init();
        }

        private void Init()
        {
            CurrTurn = 0;
            LastInputID = -1;

            IsInitialized = true;

            if (IsLocalPlayerController)
                CmdValidate(gameMgr.LocalFactionSlot.ID);
            else if (IsNPCController)
                Validate(NPCFactionID);
        }
        #endregion

        #region Post-Initializing: Validation from Server
        [Command]
        private void CmdValidate(int factionSlotID)
        {
            if (multiplayerMgr.IsServerOnly)
                Validate(factionSlotID);

            RpcValidate(factionSlotID);
        }

        [ClientRpc]
        private void RpcValidate(int factionSlotID)
        {
            Validate(factionSlotID);
        }


        private void Validate(int factionSlotID)
        {
            // In case this is the server, this object might not have been initialized yet so fetch it directly.
            if (!multiplayerMgr.IsValid())
                multiplayerMgr = NetworkManager.singleton as IMultiplayerManager;

            // If this game instance is the headless server then update the input directly as the RPC call will not be called on the headless server
            if (multiplayerMgr.IsServer)
            {
                OnValidated(factionSlotID);

                if (!IsNPCController)
                    TargetOnValidated(factionSlotID);
            }

            multiplayerMgr.OnMultiplayerFactionManagerValidated(this, (float)LastRTT);
        }

        [TargetRpc]
        private void TargetOnValidated(int factionSlotID)
        {
            OnValidated(factionSlotID);
        }

        private void OnValidated(int factionSlotID)
        {
            GameFactionSlot = gameMgr.GetFactionSlot(factionSlotID);
        }

        public void OnClientValidatedServer(bool allClientsValidated, int validatedAmount)
        {
            // Only allow to go through if this has been called on the server instance
            if (!multiplayerMgr.ServerGameMgr.IsValid()
                || !allClientsValidated)
                return;

            if (multiplayerMgr.IsServerOnly)
                PauseSimulationFinal(enable: false);

            // If this is a headless server instance then it would have to push its cached inputs to all clients to start the game
            // Therefore we call it directly as the above TargetRpc method would only be called on the clients side
            if (IsNPCController)
                OnAllClientsValidated();
            else
                TargetOnAllClientsValidatedLocal(connectionToClient);
        }

        [TargetRpc]
        private void TargetOnAllClientsValidatedLocal(NetworkConnection connection)
        {
            OnAllClientsValidated();
        }

        private void OnAllClientsValidated()
        {
            IsValidated = true;

            logger.Log($"[{GetType().Name}] All players have been validated on the server. Simulation has now started!");
            globalEvent.RaiseShowPlayerMessageGlobal(this, new MessageEventArgs
            (
                type: RTSEngine.UI.MessageType.info,
                message: "All factions validated! Game Starting..."
            ));

            PauseSimulationFinal(enable: false);

            // Re-send the cached input that was added and attempted to be sent before all clients were validated
            AddInput(preValidationInputCache);
            preValidationInputCache.Clear();
        }
        #endregion

        #region Adding Input
        public void AddInput(CommandInput input)
        {
            AddInput(Enumerable.Repeat(input, 1));
        }

        public void AddInput(IEnumerable<CommandInput> inputs)
        {
            if (IsSimPaused)
            {
                preValidationInputCache.AddRange(inputs);
                return;
            }

            // If we are dealing with the instance where the server is then directly add the input to the server
            if (RTSHelper.IsMasterInstance())
                AddInputToMaster(inputs);
            else 
                CmdAddInput(inputs.ToArray());
        }

        [Command(requiresAuthority =false)]
        private void CmdAddInput(CommandInput[] inputs)
        {
            AddInputToMaster(inputs);
        }

        private void AddInputToMaster(IEnumerable<CommandInput> inputs)
        {
            multiplayerMgr.ServerGameMgr.AddInput(inputs, GameFactionSlot.ID);
        }
        #endregion

        #region Getting Relayed Input
        public void RelayInput(IEnumerable<MultiplayerInputWrapper> relayedInputs, int lastRelayedInputID, int serverTurn, float relayedRTT)
        {
            // Only allow to go through if this has been called on the server instance
            if (!multiplayerMgr.ServerGameMgr.IsValid())
                return;

            if (IsNPCController)
            {
                // Let the server know we received it!
                if (IsSimPaused && serverTurn == CurrTurn - 1)
                    logger.Log($"[{GetType().Name} - Faction ID: {GameFactionSlot.ID}] Server is resending inputs of last turn due to locked simulation (game is paused)!");
                else if (!logger.RequireTrue(serverTurn == CurrTurn,
                  $"[{GetType().Name}] Expected to get server turn {CurrTurn} but received {serverTurn} instead! This MUST NOT happen!"))
                    return;
                else
                {
                    multiplayerMgr.ServerGameMgr.OnRelayedInputReceived(GameFactionSlot.ID, serverTurn, (float)LastRTT);

                    // Only increase the current turn in case we receive input on the expected server turn.
                    if (serverTurn == CurrTurn)
                        CurrTurn++;
                }
            }
            else
            {
                TargetRpcRelayInput(connectionToClient, relayedInputs.ToArray(), lastRelayedInputID, serverTurn);
                RpcRelayRTT(GameFactionSlot.ID, relayedRTT);
            }
        }

        [ClientRpc]
        private void RpcRelayRTT(int factionID, float relayedRTT)
        {
            if (!pingResource.IsValid())
                return;

            resourceMgr.SetResource(
                factionID,
                new ResourceInput
                {
                    type = pingResource,
                    value = new ResourceTypeValue { amount = Mathf.RoundToInt(relayedRTT * 1000)}
                });
        }

        [TargetRpc]
        private void TargetRpcRelayInput(NetworkConnection targetClient, MultiplayerInputWrapper[] inputs, int lastRelayedInputID, int serverTurn)
        {
            if(IsSimPaused && serverTurn == CurrTurn - 1)
                logger.Log($"[{GetType().Name} - Faction ID: {GameFactionSlot.ID}] Server is resending inputs of last turn due to locked simulation (game is paused)!");
            else if (!logger.RequireTrue(serverTurn == CurrTurn,
              $"[{GetType().Name}] Expected to get server turn {CurrTurn} but received {serverTurn} instead! This MUST NOT happen!"))
                return; 

            if (inputs.Any())
                foreach (MultiplayerInputWrapper input in inputs)
                {
                    // Client has already received this input.
                    if (input.ID < LastInputID + 1)
                        continue;
                    // Next input that the client is expecting
                    else if (input.ID == LastInputID + 1)
                    {
                        LastInputID++;
                        this.relayedInputs.Add(input.input);
                    }
                    else
                    {
                        logger.LogError($"[{GetType().Name}] Bad relayed input on client! Expected input of ID {LastInputID + 1} but got input of ID {input.ID}. This MUST NOT happen!");
                        return;
                    }
                }

            // Either no inputs were sent or we can confirm all inputs were received.
            if(!inputs.Any() || LastInputID == lastRelayedInputID)
            {
                // Play the actual inputs
                foreach (CommandInput input in this.relayedInputs)
                    inputMgr.LaunchInput(input);

                this.relayedInputs.Clear();

                /*
                 * Issue of random disconnection used to happen in case the simulation pauses and the confirmation does not get to the server before the server actually resends the input.
                 * When this happens, the received inputs is empty. this needs further investigating. This is now fixed with the server ignoring double input relay confirmation
                 * But a more robust solution should be implemented to avoid unnecessary network traffic.
                 * If the transport used is reliable then it probably makes sense to not resend the confirmation on empty inputs?? but what if the turn has no inputs to relay??
                 * Or maybe do not tie this to the inputs being empty or not but rather the serverTurn and CurrTurn
                if(LastInputID != lastRelayedInputID && !inputs.Any())
                    logger.LogError($"Double confirmed input of server turn {serverTurn} with local turn {CurrTurn}");
                */

                // Let the server know we received it!
                CmdOnRelayedInputReceived(serverTurn, (float)LastRTT);
            }

            // Only increase the current turn in case we receive input on the expected server turn.
            if(serverTurn == CurrTurn)
                CurrTurn++;

            // Show the relayed RTT
        }

        [Command(requiresAuthority =false)]
        private void CmdOnRelayedInputReceived(int turnID, float lastRTT)
        {
            multiplayerMgr.ServerGameMgr.OnRelayedInputReceived(GameFactionSlot.ID, turnID, (float)lastRTT);
        }
        #endregion

        #region Pausing Simulation
        public void PauseSimulation(bool enable)
        {
            // Only allow to go through if this has been called on the server instance
            if (!multiplayerMgr.ServerGameMgr.IsValid())
                return;

            if (multiplayerMgr.IsServerOnly)
                PauseSimulationFinal(enable);

            if(!isNPCFaction)
                RpcPauseSimulation(connectionToClient, enable);

            IsSimPaused = enable;
        }

        [TargetRpc]
        private void RpcPauseSimulation(NetworkConnection connectionToClient, bool enable)
        {
            PauseSimulationFinal(enable);
        }

        private void PauseSimulationFinal(bool enable)
        {
            this.IsSimPaused = enable;

            if (IsSimPaused)
                gameMgr.SetState(GameStateType.frozen);
            else
                gameMgr.SetState(GameStateType.running);
        }
        #endregion
    }
}
