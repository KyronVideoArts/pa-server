using System.Collections.Concurrent;
using MasterServer.Models;
using MasterServer.Services;

namespace MasterServer
{
	public static class ServerState
	{
		public static ConcurrentDictionary<string, string> JoinTokens = new();
		public static ConcurrentDictionary<string, LobbyRecord> ActiveLobbies = new();
		public static ConcurrentDictionary<string, RelayLobby> ActiveRelays = new();
		public const string ServerApiKey = "SECRET_API_KEY";
	}
}
