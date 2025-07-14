namespace S7UaLib.Core.Ua;

public class UserIdentity
{
    #region Constructors

    public UserIdentity()
    { }

    public UserIdentity(string username, string password)
    {
        Username = username;
        Password = password;
    }

    #endregion Constructors

    #region Public Properties

    public string? Username { get; }
    public string? Password { get; }

    #endregion Public Properties
}