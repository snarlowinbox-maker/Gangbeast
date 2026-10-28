using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BeefCity.Customization;

namespace BeefCity.Accounts
{
    /// <summary>One row of the Users table: unique ID, email, hashed password, creation date.</summary>
    [Serializable]
    public class UserRecord
    {
        public string id;            // GUID
        public string email;         // lower-case, unique
        public string passwordHash;  // pbkdf2-sha256$iterations$salt$hash
        public string createdAt;     // ISO 8601, UTC
        public string displayName;
    }

    [Serializable]
    public class UsersTable
    {
        public List<UserRecord> users = new List<UserRecord>();
    }

    /// <summary>The part of the player's data that follows the account: costume, unlocks, stats.</summary>
    [Serializable]
    public class ProfileData
    {
        public string displayName;
        public CostumeData costume;
        public List<string> unlocked = new List<string>();
        public int matches;
        public int wins;
        public string bindingOverrides = "";
    }

    public sealed class AccountResult
    {
        public bool Ok;
        public string Error;
        public UserRecord User;
        public ProfileData Profile;

        public static AccountResult Fail(string error) => new AccountResult { Ok = false, Error = error };
    }

    /// <summary>
    /// Where accounts live. LocalAccountBackend (JSON files, always available) is the fallback; a cloud backend such as
    /// Unity Gaming Services plugs in behind the same interface.
    /// </summary>
    public interface IAccountBackend
    {
        string Name { get; }
        Task<AccountResult> RegisterAsync(string email, string password, string displayName);
        Task<AccountResult> LoginAsync(string email, string password);
        Task SaveProfileAsync(UserRecord user, ProfileData profile);
        Task<ProfileData> LoadProfileAsync(UserRecord user);
        void Logout();
    }
}
