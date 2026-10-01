namespace Content.Shared.Voting
{
    /// <summary>
    /// Standard vote types that players can initiate themselves from the escape menu.
    /// </summary>
    public enum StandardVoteType : byte
    {
        /// <summary>
        /// Vote to restart the round.
        /// </summary>
        Restart,

        /// <summary>
        /// Vote to change the game preset for next round.
        /// </summary>
        Preset,

        /// <summary>
        /// Vote to change the map for the next round.
        /// </summary>
        Map,

        // #Misfits Change: Vote to extend the current round (recall shuttle / reset auto-call timer).
        /// <summary>
        /// Vote to extend the round by recalling the emergency shuttle and resetting the auto-call timer.
        /// </summary>
        Extend
    }
}
