using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;

namespace SteamAPI
{
    /// <summary>
    /// Class allowing you to use the Steam Web API to log in and use Steam Friends functionality.
    /// </summary>
    public class SteamApiSession
    {
        private string _accessToken;
        private int _message;
        private string _steamid;
        private string _umqid;

        /// <summary>
        /// Authenticate with a username and password.
        /// Sends the SteamGuard e-mail if it has been set up.
        /// </summary>
        /// <param name="username">Username</param>
        /// <param name="password">Password</param>
        /// <param name="emailauthcode">SteamGuard code sent by e-mail</param>
        /// <returns>Indication of the authentication status.</returns>
        public LoginStatus Authenticate(string username, string password, string emailauthcode = "")
        {
            var response = SteamRequest("ISteamOAuth2/GetTokenWithCredentials/v0001",
                "client_id=DE45CD61&grant_type=password&username=" + Uri.EscapeDataString(username) + "&password=" +
                Uri.EscapeDataString(password) + "&x_emailauthcode=" + emailauthcode +
                "&scope=read_profile%20write_profile%20read_client%20write_client");

            if (response != null)
            {
                var data = JObject.Parse(response);

                if (data["access_token"] != null)
                {
                    _accessToken = (string) data["access_token"];

                    return Login() ? LoginStatus.LoginSuccessful : LoginStatus.LoginFailed;
                }
                if (((string) data["x_errorcode"]).Equals("steamguard_code_required"))
                    return LoginStatus.SteamGuard;
                return LoginStatus.LoginFailed;
            }
            return LoginStatus.LoginFailed;
        }

        /// <summary>
        /// Authenticate with an access token previously retrieved with a username
        /// and password (and SteamGuard code).
        /// </summary>
        /// <param name="accessToken">Access token retrieved with credentials</param>
        /// <returns>Indication of the authentication status.</returns>
        public LoginStatus Authenticate(string accessToken)
        {
            _accessToken = accessToken;
            return Login() ? LoginStatus.LoginSuccessful : LoginStatus.LoginFailed;
        }

        /// <summary>
        /// Fetch all friends of a given user.
        /// </summary>
        /// <remarks>This function does not provide detailed information.</remarks>
        /// <param name="steamid">steamid of target user or self</param>
        /// <returns>List of friends or null on failure.</returns>
        public List<Friend> GetFriends(string steamid = null)
        {
            if (_umqid == null) return null;
            if (steamid == null) steamid = _steamid;

            var response =
                SteamRequest("ISteamUserOAuth/GetFriendList/v0001?access_token=" + _accessToken + "&steamid=" + steamid);

            if (response == null) return null;

            var data = JObject.Parse(response);

            if (data["friends"] == null) return null;

            var friends = new List<Friend>();

            foreach (var jToken in data["friends"])
            {
                var friend = (JObject) jToken;
                var f = new Friend
                {
                    Steamid = (string) friend["steamid"],
                    Blocked = ((string) friend["relationship"]).Equals("ignored"),
                    FriendSince = UnixTimestamp((long) friend["friend_since"])
                };
                friends.Add(f);
            }

            return friends;
        }

        /// <summary>
        /// Retrieve information about the specified users.
        /// </summary>
        /// <remarks>This function doesn't have the 100 users limit the original API has.</remarks>
        /// <param name="steamids">64-bit SteamIDs of users</param>
        /// <returns>Information about the specified users</returns>
        public List<User> GetUserInfo(List<string> steamids)
        {
            if (_umqid == null) return null;

            var response =
                SteamRequest("ISteamUserOAuth/GetUserSummaries/v0001?access_token=" + _accessToken + "&steamids=" +
                             string.Join(",", steamids.GetRange(0, Math.Min(steamids.Count, 100)).ToArray()));

            if (response == null) return null;

            var data = JObject.Parse(response);

            if (data["players"] == null) return null;

            var users = new List<User>();

            foreach (var jToken in data["players"])
            {
                var info = (JObject) jToken;
                var user = new User
                {
                    Steamid = (string) info["steamid"],
                    ProfileVisibility = (ProfileVisibility) (int) info["communityvisibilitystate"],
                    ProfileState = (int) info["profilestate"],
                    Nickname = (string) info["personaname"],
                    LastLogoff = UnixTimestamp((long) info["lastlogoff"]),
                    ProfileUrl = (string) info["profileurl"],
                    Status = (UserStatus) (int) info["personastate"],
                    AvatarUrl = info["avatar"] != null ? (string) info["avatar"] : "",
                    JoinDate = UnixTimestamp(info["timecreated"] != null ? (long) info["timecreated"] : 0),
                    PrimaryGroupId = info["primaryclanid"] != null ? (string) info["primaryclanid"] : "",
                    RealName = info["realname"] != null ? (string) info["realname"] : "",
                    LocationCountryCode = info["loccountrycode"] != null ? (string) info["loccountrycode"] : "",
                    LocationStateCode = info["locstatecode"] != null ? (string) info["locstatecode"] : "",
                    LocationCityId = info["loccityid"] != null ? (int) info["loccityid"] : -1
                };

                if (user.AvatarUrl != null)
                    user.AvatarUrl = user.AvatarUrl.Substring(0, user.AvatarUrl.Length - 4);

                users.Add(user);
            }


            // Requests are limited to 100 steamids, so issue multiple requests
            if (steamids.Count > 100)
                users.AddRange(GetUserInfo(steamids.GetRange(100, Math.Min(steamids.Count - 100, 100))));

            return users;
        }

        public List<User> GetUserInfo(List<Friend> friends)
        {
            var steamids = new List<string>(friends.Count);
            steamids.AddRange(friends.Select(f => f.Steamid));
            return GetUserInfo(steamids);
        }

        public User GetUserInfo(string steamid = null)
        {
            if (steamid == null) steamid = _steamid;
            return GetUserInfo(new List<string>(new[] {steamid}))[0];
        }

        /// <summary>
        /// Retrieve the avatar of the specified user in the specified format.
        /// </summary>
        /// <param name="user">User</param>
        /// <param name="size">Requested avatar size</param>
        /// <returns>The avatar as bitmap on success or null on failure.</returns>
        public Bitmap GetUserAvatar(User user, AvatarSize size = AvatarSize.Small)
        {
            if (user.AvatarUrl.Length == 0) return null;

            try
            {
                var client = new WebClient();

                Stream stream;
                if (size == AvatarSize.Small)
                    stream = client.OpenRead(user.AvatarUrl + ".jpg");
                else if (size == AvatarSize.Medium)
                    stream = client.OpenRead(user.AvatarUrl + "_medium.jpg");
                else
                    stream = client.OpenRead(user.AvatarUrl + "_full.jpg");

                if (stream == null)
                    return null;

                var avatar = new Bitmap(stream);
                stream.Flush();
                stream.Close();

                return avatar;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Retrieve the avatar of the specified group in the specified format.
        /// </summary>
        /// <param name="group">Group</param>
        /// <param name="size">Requested avatar size</param>
        /// <returns>The avatar as bitmap on success or null on failure.</returns>
        public Bitmap GetGroupAvatar(GroupInfo group, AvatarSize size = AvatarSize.Small)
        {
            var user = new User {AvatarUrl = @group.AvatarUrl};
            return GetUserAvatar(user, size);
        }

        /// <summary>
        /// Fetch all groups of a given user.
        /// </summary>
        /// <param name="steamid">SteamID</param>
        /// <returns>List of groups.</returns>
        public List<Group> GetGroups(string steamid = null)
        {
            if (_umqid == null) return null;
            if (steamid == null) steamid = _steamid;

            var response =
                SteamRequest("ISteamUserOAuth/GetGroupList/v0001?access_token=" + _accessToken + "&steamid=" + steamid);

            if (response == null) return null;

            var data = JObject.Parse(response);

            if (data["groups"] == null) return null;

            var groups = new List<Group>();

            foreach (var jToken in data["groups"])
            {
                var info = (JObject) jToken;
                var group = new Group
                {
                    Steamid = (string) info["steamid"],
                    Inviteonly = ((string) info["permission"]).Equals("2")
                };

                if (((string) info["relationship"]).Equals("Member"))
                    groups.Add(group);
            }

            return groups;
        }

        /// <summary>
        /// Retrieve information about the specified groups.
        /// </summary>
        /// <param name="steamids">64-bit SteamIDs of groups</param>
        /// <returns>Information about the specified groups</returns>
        public List<GroupInfo> GetGroupInfo(List<string> steamids)
        {
            if (_umqid == null) return null;

            var response =
                SteamRequest("ISteamUserOAuth/GetGroupSummaries/v0001?access_token=" + _accessToken + "&steamids=" +
                             string.Join(",", steamids.GetRange(0, Math.Min(steamids.Count, 100)).ToArray()));

            if (response == null) return null;

            var data = JObject.Parse(response);

            if (data["groups"] == null) return null;

            var groups = new List<GroupInfo>();

            foreach (var jToken in data["groups"])
            {
                var info = (JObject) jToken;
                var group = new GroupInfo
                {
                    Steamid = (string) info["steamid"],
                    CreationDate = UnixTimestamp((long) info["timecreated"]),
                    Name = (string) info["name"],
                    ProfileUrl = "http://steamcommunity.com/groups/" + (string) info["profileurl"],
                    UsersOnline = (int) info["usersonline"],
                    UsersInChat = (int) info["usersinclanchat"],
                    UsersInGame = (int) info["usersingame"],
                    Owner = (string) info["ownerid"],
                    Members = (int) info["users"],
                    AvatarUrl = (string) info["avatar"],
                    Headline = info["headline"] != null ? (string) info["headline"] : "",
                    Summary = info["summary"] != null ? (string) info["summary"] : "",
                    Abbreviation = info["abbreviation"] != null ? (string) info["abbreviation"] : "",
                    LocationCountryCode = info["loccountrycode"] != null
                        ? (string) info["loccountrycode"]
                        : "",
                    LocationStateCode = info["locstatecode"] != null ? (string) info["locstatecode"] : "",
                    LocationCityId = info["loccityid"] != null ? (int) info["loccityid"] : -1,
                    FavoriteAppId = info["favoriteappid"] != null ? (int) info["favoriteappid"] : -1
                };

                if (group.AvatarUrl != null)
                    group.AvatarUrl = group.AvatarUrl.Substring(0, group.AvatarUrl.Length - 4);

                groups.Add(group);
            }

            // Requests are limited to 100 steamids, so issue multiple requests
            if (steamids.Count > 100)
                groups.AddRange(GetGroupInfo(steamids.GetRange(100, Math.Min(steamids.Count - 100, 100))));

            return groups;
        }

        public List<GroupInfo> GetGroupInfo(List<Group> groups)
        {
            var steamids = new List<string>(groups.Count);
            steamids.AddRange(groups.Select(g => g.Steamid));
            return GetGroupInfo(steamids);
        }

        public GroupInfo GetGroupInfo(string steamid)
        {
            return GetGroupInfo(new List<string>(new[] {steamid}))[0];
        }

        /// <summary>
        /// Let a user know you're typing a message. Should be called periodically.
        /// </summary>
        /// <param name="user">Recipient of notification</param>
        /// <returns>Returns a boolean indicating success of the request.</returns>
        public bool SendTypingNotification(User user)
        {
            if (_umqid == null) return false;

            var response = SteamRequest("ISteamWebUserPresenceOAuth/Message/v0001",
                "?access_token=" + _accessToken + "&umqid=" + _umqid + "&type=typing&steamid_dst=" + user.Steamid);

            if (response == null) return false;

            var data = JObject.Parse(response);

            return data["error"] != null && ((string) data["error"]).Equals("OK");
        }

        /// <summary>
        /// Send a text message to the specified user.
        /// </summary>
        /// <param name="user">Recipient of message</param>
        /// <param name="message">Message contents</param>
        /// <returns>Returns a boolean indicating success of the request.</returns>
        public bool SendMessage(User user, string message)
        {
            if (_umqid == null) return false;

            var response = SteamRequest("ISteamWebUserPresenceOAuth/Message/v0001",
                "?access_token=" + _accessToken + "&umqid=" + _umqid + "&type=saytext&text=" +
                Uri.EscapeDataString(message) + "&steamid_dst=" + user.Steamid);

            if (response == null) return false;

            var data = JObject.Parse(response);

            return data["error"] != null && ((string) data["error"]).Equals("OK");
        }

        public bool SendMessage(string steamid, string message)
        {
            var user = new User {Steamid = steamid};
            return SendMessage(user, message);
        }

        /// <summary>
        /// Check for updates and new messages.
        /// </summary>
        /// <returns>A list of updates.</returns>
        public List<Update> Poll()
        {
            if (_umqid == null) return null;

            var response = SteamRequest("ISteamWebUserPresenceOAuth/Poll/v0001",
                "?access_token=" + _accessToken + "&umqid=" + _umqid + "&message=" + _message);

            if (response == null) return null;
            var data = JObject.Parse(response);

            if (!((string) data["error"]).Equals("OK")) return null;
            _message = (int) data["messagelast"];

            var updates = new List<Update>();

            foreach (var jToken in data["messages"])
            {
                var info = (JObject) jToken;
                var update = new Update
                {
                    Timestamp = UnixTimestamp((long) info["timestamp"]),
                    Origin = (string) info["steamid_from"]
                };

                var type = (string) info["type"];
                if (type.Equals("saytext") || type.Equals("my_saytext") || type.Equals("emote"))
                {
                    update.Type = type.Equals("emote") ? UpdateType.Emote : UpdateType.Message;
                    update.Message = (string) info["text"];
                    update.LocalMessage = type.Equals("my_saytext");
                }
                else if (type.Equals("typing"))
                {
                    update.Type = UpdateType.TypingNotification;
                    update.Message = (string) info["text"]; // Not sure if this is useful
                }
                else if (type.Equals("personastate"))
                {
                    update.Type = UpdateType.UserUpdate;
                    update.Status = (UserStatus) (int) info["persona_state"];
                    update.Nick = (string) info["persona_name"];
                }
                else
                {
                    continue;
                }

                updates.Add(update);
            }

            return updates;
        }

        /// <summary>
        /// Retrieves information about the server.
        /// </summary>
        /// <returns>Returns a structure with the information.</returns>
        public ServerInfo GetServerInfo()
        {
            var response = SteamRequest("ISteamWebAPIUtil/GetServerInfo/v0001");

            if (response == null) return null;
            var data = JObject.Parse(response);

            if (data["servertime"] == null) return null;

            var info = new ServerInfo
            {
                ServerTime = UnixTimestamp((long) data["servertime"]),
                ServerTimestring = (string) data["servertimestring"]
            };
            return info;
        }

        /// <summary>
        /// Helper function to complete the login procedure and check the
        /// credentials.
        /// </summary>
        /// <returns>Whether the login was successful or not.</returns>
        private bool Login()
        {
            var response = SteamRequest("ISteamWebUserPresenceOAuth/Logon/v0001",
                "?access_token=" + _accessToken);

            if (response == null) return false;
            var data = JObject.Parse(response);

            if (data["umqid"] == null) return false;
            _steamid = (string) data["steamid"];
            _umqid = (string) data["umqid"];
            _message = (int) data["message"];
            return true;
        }

        /// <summary>
        /// Helper function to perform Steam API requests.
        /// </summary>
        /// <param name="get">Path URI</param>
        /// <param name="post">Post data</param>
        /// <returns>Web response info</returns>
        private string SteamRequest(string get, string post = null)
        {
            ServicePointManager.Expect100Continue = false;

            var request = (HttpWebRequest) WebRequest.Create("https://api.steampowered.com/" + get);
            request.Host = "api.steampowered.com:443";
            request.ProtocolVersion = HttpVersion.Version11;
            request.Accept = "*/*";
            request.Headers[HttpRequestHeader.AcceptEncoding] = "gzip, deflate";
            request.Headers[HttpRequestHeader.AcceptLanguage] = "en-us";
            request.UserAgent = "Steam 1291812 / iPhone";

            if (post != null)
            {
                request.Method = "POST";
                var postBytes = Encoding.ASCII.GetBytes(post);
                request.ContentType = "application/x-www-form-urlencoded";
                request.ContentLength = postBytes.Length;

                var requestStream = request.GetRequestStream();
                requestStream.Write(postBytes, 0, postBytes.Length);
                requestStream.Close();

                _message++;
            }

            try
            {
                var response = (HttpWebResponse) request.GetResponse();
                if ((int) response.StatusCode != 200) return null;

                var rStream = response.GetResponseStream();
                if (rStream == null) return null;

                var src = new StreamReader(rStream).ReadToEnd();
                response.Close();
                return src;
            }
            catch (WebException)
            {
                return null;
            }
        }

        private static DateTime UnixTimestamp(long timestamp)
        {
            var origin = new DateTime(1970, 1, 1, 0, 0, 0, 0);
            return origin.AddSeconds(timestamp);
        }

        /// <summary>
        /// Structure containing basic friend info.
        /// </summary>
        public class Friend
        {
            public bool Blocked;
            public DateTime FriendSince;
            public string Steamid;
        }

        /// <summary>
        /// Structure containing extensive user info.
        /// </summary>
        public class User
        {
            internal string AvatarUrl;
            public DateTime JoinDate;
            public DateTime LastLogoff;
            public int LocationCityId;
            public string LocationCountryCode;
            public string LocationStateCode;
            public string Nickname;
            public string PrimaryGroupId;
            public int ProfileState;
            public string ProfileUrl;
            public ProfileVisibility ProfileVisibility;
            public string RealName;
            public UserStatus Status;
            public string Steamid;
        }

        /// <summary>
        /// Basic group info.
        /// </summary>
        public class Group
        {
            public bool Inviteonly;
            public string Steamid;
        }

        /// <summary>
        /// Structure containing extensive group info.
        /// </summary>
        public class GroupInfo
        {
            public string Abbreviation;
            internal string AvatarUrl;
            public DateTime CreationDate;
            public int FavoriteAppId;
            public string Headline;
            public int LocationCityId;
            public string LocationCountryCode;
            public string LocationStateCode;
            public int Members;
            public string Name;
            public string Owner;
            public string ProfileUrl;
            public string Steamid;
            public string Summary;
            public int UsersInChat;
            public int UsersInGame;
            public int UsersOnline;
        }

        /// <summary>
        /// Structure containing information about a single update.
        /// </summary>
        public class Update
        {
            public bool LocalMessage;
            public string Message;
            public string Nick;
            public string Origin;
            public UserStatus Status;
            public DateTime Timestamp;
            public UpdateType Type;
        }

        /// <summary>
        /// Structure containing server info.
        /// </summary>
        public class ServerInfo
        {
            public DateTime ServerTime;
            public string ServerTimestring;
        }
    }
}