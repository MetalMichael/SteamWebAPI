namespace SteamAPI
{
    /// <summary>
    /// Available sizes of user avatars.
    /// </summary>
    public enum AvatarSize
    {
        Small,
        Medium,
        Large
    }

    /// <summary>
    /// Enumeration of possible authentication results.
    /// </summary>
    public enum LoginStatus
    {
        LoginFailed,
        LoginSuccessful,
        SteamGuard
    }

    /// <summary>
    /// Visibility of a user's profile.
    /// </summary>
    public enum ProfileVisibility
    {
        Private = 1,
        Public = 3,
        FriendsOnly = 8
    }

    /// <summary>
    /// Available update types.
    /// </summary>
    public enum UpdateType
    {
        UserUpdate,
        Message,
        Emote,
        TypingNotification
    }

    /// <summary>
    /// Status of a user.
    /// </summary>
    public enum UserStatus
    {
        Offline = 0,
        Online = 1,
        Busy = 2,
        Away = 3,
        Snooze = 4
    }
}
