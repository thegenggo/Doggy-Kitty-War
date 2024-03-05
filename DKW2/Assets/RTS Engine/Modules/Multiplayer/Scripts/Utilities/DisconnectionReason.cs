namespace RTSEngine.Multiplayer.Utilities
{
    public enum DisconnectionReason
    {
        playerCommand,

        socketUsed,

        timeout,

        // Lobby related
        lobbyNotFound,
        gameCodeMismatch,
        lobbyHostKick,
        lobbyNotAvailable,
        lobbyAlreadyStarting,
        lobbyMapMaxFactions,

        // Server related,
        nextHostNotFound,
        serverKick,
    }
}
