using Mirror;
using RTSEngine.Lobby;
using RTSEngine.Multiplayer.Lobby;
using RTSEngine.Multiplayer.Utilities;
using System;
using System.Collections;
namespace RTSEngine.Multiplayer.Mirror.Lobby
{
    public class MultiplayerLobbyUIManager : LobbyUIManagerBase<IMultiplayerLobbyFactionSlot>
    {
        #region Attributes
        protected IMultiplayerManager multiplayerMgr { private set; get; }
        protected new IMultiplayerLobbyManager lobbyMgr { private set; get; }
        #endregion

        #region Initializing/Terminating
        protected override void OnInit()
        {
            this.lobbyMgr = base.lobbyMgr as IMultiplayerLobbyManager;
            this.multiplayerMgr = NetworkManager.singleton as IMultiplayerManager;

            if (!logger.RequireValid(multiplayerMgr,
              $"[{GetType().Name}] A component that implements the '{typeof(IMultiplayerManager).Name}' interface can not be found!"))
                return; 

            SetInteractable(false);
        }

        protected override void OnDestroyed()
        {
        }
        #endregion

        #region Updating Lobby Game Data
        public override void OnLobbyGameDataUIUpdated()
        {
            if (!lobbyMgr.IsLobbyGameDataMaster())
                return;

            lobbyMgr.UpdateLobbyGameDataRequest(UIToLobbyGameData);
        }
        #endregion
    }
}
