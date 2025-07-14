using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace S7UaLib.Core.Ua;
public class UserIdentity
{
    #region Constructors

    public UserIdentity() { }

    public UserIdentity(string username, string password)
    {
        Username = username;
        Password = password;
    }

    #endregion

    #region Public Properties

    public string? Username { get; }
    public string? Password { get; }

    #endregion Public Properties
}
