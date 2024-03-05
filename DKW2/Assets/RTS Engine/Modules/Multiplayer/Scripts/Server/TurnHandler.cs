using System;
using System.Collections;
using System.Linq;

using UnityEngine;

using RTSEngine.Multiplayer.Logging;

namespace RTSEngine.Multiplayer.Server
{
    [System.Serializable]
    public class TurnHandler
    {
        #region Attributes
        private bool isInitialized = false;

        [SerializeField, Tooltip("The allowed duration (in seconds) range that a turn can have. The server is able to adjust the turn time during the game depending on the latency of the clients but it will always keep it inside this range.")]
        private FloatRange turnTimeRange = new FloatRange(0.1f, 1.0f);
        private float turnTimer;

        [SerializeField, Tooltip("Enable to fix the turn time to the initial value it takes from the above range and and not allow to update it.")]
        private bool fixTurnTime = true;

        [SerializeField, Tooltip("Enable to allow the server to update the turn time every 'X' amount of turns based on the RTTs tracked from the game's clients.")]
        private bool periodicTurnTimeUpdateEnabled = true;
        [SerializeField, Tooltip("When the periodic turn time update is enabled, this is the amount of turns needed before an update occurs.")]
        private int turnTimeUpdatePeriod = 20;
        private int turnTimeUpdateRef = 0;

        public enum TurnTimeUpdateOption { averageClientRTT, highestClientRTT };
        [SerializeField, Tooltip("When the turn time is initially set or updated during the game, either use the average of all clients' RTTs or focus on the client with the highest RTT?")]
        private TurnTimeUpdateOption turnTimeUpdateOption = TurnTimeUpdateOption.highestClientRTT;

        [SerializeField, Tooltip("Value added to the turn time after it is updated. Adding a small value after the turn time is computed using the clients' RTT values helps give a little extra time to keep all clients synced while avoiding frequent freezes.")]
        private float turnTimeOffset = 0.05f;

        private float turnTime;

        private Action onTurnComplete;

        // Services
        protected IMultiplayerLoggingService logger { private set; get; }

        // Other components
        protected IMultiplayerManager multiplayerMgr { private set; get; }
        protected IMultiplayerServerGameManager serverGameMgr { private set; get; }
        #endregion

        #region Initializing/Terminating
        public void Init(IMultiplayerManager multiplayerMgr, Action onTurnComplete)
        {
            this.logger = multiplayerMgr.GetService<IMultiplayerLoggingService>();

            if (isInitialized)
            {
                logger.LogError($"[{GetType().Name}] Already initialized!");
                return;
            }

            this.multiplayerMgr = multiplayerMgr;
            this.serverGameMgr = multiplayerMgr.ServerGameMgr;

            this.onTurnComplete = onTurnComplete;

            turnTime = turnTimeRange.RandomValue;
            turnTimer = 0.0f;
            turnTimeUpdateRef = 0;

            isInitialized = true;
        }

        public void Disable()
        {
        }
        #endregion

        #region Handling Turn Update
        public void UpdateTurn()
        {
            turnTimer += Time.deltaTime;
            bool turnTriggered = false;

            while(turnTimer > turnTime)
            {
                turnTimer -= turnTime;
                onTurnComplete();
                turnTriggered = true;
            }

            if(turnTriggered && periodicTurnTimeUpdateEnabled)
            {
                if(serverGameMgr.ServerTurn - turnTimeUpdateRef >= turnTimeUpdatePeriod)
                {
                    turnTimeUpdateRef = serverGameMgr.ServerTurn;
                    serverGameMgr.UpdateTurnTimeWithRTTLogs();
                }
            }
        }

        public void UpdateTurnTime(float[][] clientLogs)
        {
            if (fixTurnTime)
                return;

            float lastTurnTime = turnTime;

            var averageClientLogs = clientLogs.Select(logs =>
            {
                // We do not consider the slots where RTT is equal to 0.0f because these would be tied to turns that are yet to occur.
                // This is however only the case when the multiplayer game starts.
                var validLogs = logs.Where(log => log > 0.0f).ToArray();

                return validLogs.Any()
                ? validLogs.Sum() / validLogs.Length
                : 0.0f;
            });

            switch(turnTimeUpdateOption)
            {
                case TurnTimeUpdateOption.averageClientRTT:
                    turnTime = turnTimeRange.Clamp(averageClientLogs.Any() 
                        ? averageClientLogs.Sum() / clientLogs.Length
                        : 0.0f);
                    break;

                case TurnTimeUpdateOption.highestClientRTT:
                    turnTime = turnTimeRange.Clamp(averageClientLogs.Any() 
                        ? averageClientLogs.Max()
                        : 0.0f);
                    break;
            }


            turnTime += turnTimeOffset;

            if(lastTurnTime != turnTime)
                logger.LogWarning(
                    $"[TurnHandler - Server Turn: {multiplayerMgr.ServerGameMgr.ServerTurn}] Turn time update from {lastTurnTime} to {turnTime}");
        }
        #endregion
    }
}
