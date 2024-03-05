using RTSEngine.Event;
using System;
using System.Collections.Generic;

using RTSEngine.Game;
using RTSEngine.Service;
using RTSEngine.Multiplayer.Event;
using RTSEngine.Multiplayer.Game;
using RTSEngine.Multiplayer.Lobby;
using RTSEngine.Multiplayer.Server;
using RTSEngine.Multiplayer.Service;
using RTSEngine.Multiplayer.Utilities;
using RTSEngine.Lobby;

namespace RTSEngine.Multiplayer
{
    public interface IMultiplayerManager : IServicePublisher<IMultiplayerService>
    {
        MultiplayerState State { get; }
        MultiplayerRole Role { get; }
        bool IsServer { get; }
        bool IsServerOnly { get; }

        IMultiplayerServerManager ServerMgr { get; }
        ServerAccessData CurrServerAccessData { get; }
        ILobbyFactionSlot LobbyFactionSlotPrefab { get; }

        event CustomEventHandler<IMultiplayerManager, MultiplayerStateEventArgs> MultiplayerStateUpdated;
        event CustomEventHandler<IMultiplayerFactionManager, MultiplayerFactionEventArgs> MultiplayerFactionManagerValidated;

        ServerAccessData UpdateServerAccessData(ServerAccessData accessData);

        void LaunchHost();
        void LaunchClient();
        void LaunchServer();

        void Stop(DisconnectionReason reason);

        IMultiplayerLobbyManager CurrentLobby { get; }
        void OnLobbyLoaded(IMultiplayerLobbyManager currentLobby);
        ErrorMessage CanStartLobby();
        ErrorMessage StartLobby();
        bool InterruptStartLobby();

        IMultiplayerServerGameManager ServerGameMgr { get; }
        IReadOnlyList<IMultiplayerFactionManager> MultiplayerFactionMgrs { get; }
        IMultiplayerFactionManager LocalMultiplayerFactionMgr { get; }
        IGameManager CurrentGameMgr { get; }
        void OnMultiplayerFactionManagerValidated(IMultiplayerFactionManager multiplayerFactionMgr, float initialRTT);
        void OnGameLoaded(IGameManager gameMgr);

    }
}
