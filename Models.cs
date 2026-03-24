using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Models
{
	public class MasterDbContext : DbContext
	{
		public MasterDbContext(DbContextOptions<MasterDbContext> options) : base(options) { }
		public DbSet<PlayerProfile> PlayerProfiles { get; set; } = null!;
	}

	public class PlayerProfile
	{
		[Key] public string Username { get; set; } = string.Empty;
		public string PasswordHash { get; set; } = string.Empty;
		public string LoadoutId { get; set; } = "default";
		public string MultiplayerGadget { get; set; } = "grapple";
		public string MultiplayerPerks { get; set; } = "[]";
		public int TotalFrags { get; set; }
		public int TimePlayedMinutes { get; set; }
		public int MultiplayerGems { get; set; }
		public string SingleplayerLoadoutId { get; set; } = "default";
		public string SingleplayerGadget { get; set; } = "grapple";
		public string SingleplayerPerks { get; set; } = "[]";
		public int SingleplayerGems { get; set; }
		public string UnlockedWeapons { get; set; } = "[\"default\"]";
	}

	public class LobbyRecord
	{
		public string HostToken { get; set; } = string.Empty;
		public string JoinCode { get; set; } = string.Empty;
		public string LobbyName { get; set; } = string.Empty;
		public string HostName { get; set; } = string.Empty;
		public string AdvertisedAddress { get; set; } = string.Empty;
		public int AdvertisedPort { get; set; }
		public bool IsOfficial { get; set; }
		public bool HasPassword { get; set; }
		public string CurrentMap { get; set; } = "Waiting...";
		public string MapMode { get; set; } = "Deathmatch";
		public int CurrentPlayers { get; set; } = 1;
		public int MaxPlayers { get; set; } = 16;
		public bool UseBots { get; set; } = false;
		public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
	}

	public class PrepareLobbyRequest
	{
		public string HostToken { get; set; } = string.Empty;
		public string LobbyName { get; set; } = string.Empty;
		public string HostName { get; set; } = string.Empty;
		public int RequestedPort { get; set; }
		public string LocalAddress { get; set; } = string.Empty;
		public string PasswordHash { get; set; } = string.Empty;
		public string ServerApiKey { get; set; } = string.Empty;
		public int MaxPlayers { get; set; } = 16;
		public bool UseBots { get; set; } = false;
		public string MapMode { get; set; } = "Deathmatch";
	}

	public class HeartbeatRequest
	{
		public string JoinCode { get; set; } = string.Empty;
		public string CurrentMap { get; set; } = string.Empty;
		public int CurrentPlayers { get; set; } = 1;
	}

	public class IceCandidateRecord
	{
		public string Foundation { get; set; } = string.Empty;
		public int Component { get; set; } = 1;
		public string Transport { get; set; } = "udp";
		public uint Priority { get; set; }
		public string Address { get; set; } = string.Empty;
		public int Port { get; set; }
		public string Type { get; set; } = string.Empty;
		public string RelatedAddress { get; set; } = string.Empty;
		public int RelatedPort { get; set; }
	}

	public class IceDescriptionRecord
	{
		public string Ufrag { get; set; } = string.Empty;
		public string Pwd { get; set; } = string.Empty;
		public List<IceCandidateRecord> Candidates { get; set; } = new();
	}

	public class IcePrepareLobbyRequest
	{
		public string HostToken { get; set; } = string.Empty;
		public string LobbyName { get; set; } = string.Empty;
		public string HostName { get; set; } = string.Empty;
		public int RequestedPort { get; set; }
		public string PasswordHash { get; set; } = string.Empty;
		public string ServerApiKey { get; set; } = string.Empty;
		public int MaxPlayers { get; set; } = 16;
		public bool UseBots { get; set; }
		public string MapMode { get; set; } = "Deathmatch";
		public IceDescriptionRecord Ice { get; set; } = new();
	}

	public class IceJoinLobbyRequest
	{
		public string PlayerName { get; set; } = string.Empty;
		public IceDescriptionRecord Ice { get; set; } = new();
	}

	public class IceCandidateSubmitRequest
	{
		public string SessionId { get; set; } = string.Empty;
		public List<IceCandidateRecord> Candidates { get; set; } = new();
	}

	public class IceConnectedRequest
	{
		public string SessionId { get; set; } = string.Empty;
		public string SelectedRemoteAddress { get; set; } = string.Empty;
		public int SelectedRemotePort { get; set; }
	}

	public class IceSessionEvent
	{
		public int EventId { get; set; }
		public string Type { get; set; } = string.Empty;
		public string SessionId { get; set; } = string.Empty;
		public string RemoteSessionId { get; set; } = string.Empty;
		public IceDescriptionRecord? Ice { get; set; }
		public List<IceCandidateRecord> Candidates { get; set; } = new();
		public string SelectedRemoteAddress { get; set; } = string.Empty;
		public int SelectedRemotePort { get; set; }
	}

	public class IceSessionRecord
	{
		private readonly object _eventLock = new();
		private readonly List<IceSessionEvent> _events = new();
		private int _nextEventId = 1;

		public string SessionId { get; set; } = string.Empty;
		public string LobbyCode { get; set; } = string.Empty;
		public string Role { get; set; } = string.Empty;
		public string PlayerName { get; set; } = string.Empty;
		public string RemoteSessionId { get; set; } = string.Empty;
		public IceDescriptionRecord Ice { get; set; } = new();
		public bool IsConnected { get; set; }
		public string SelectedRemoteAddress { get; set; } = string.Empty;
		public int SelectedRemotePort { get; set; }
		public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;

		public void AddEvent(IceSessionEvent sessionEvent)
		{
			lock (_eventLock)
			{
				sessionEvent.EventId = _nextEventId++;
				_events.Add(sessionEvent);
			}
		}

		public List<IceSessionEvent> GetEventsSince(int lastEventId)
		{
			lock (_eventLock)
			{
				return _events.Where(e => e.EventId > lastEventId).ToList();
			}
		}
	}

	public class IceLobbyRecord
	{
		public string JoinCode { get; set; } = string.Empty;
		public string HostToken { get; set; } = string.Empty;
		public string LobbyName { get; set; } = string.Empty;
		public string HostName { get; set; } = string.Empty;
		public bool HasPassword { get; set; }
		public bool IsOfficial { get; set; }
		public int RequestedPort { get; set; }
		public int MaxPlayers { get; set; } = 16;
		public bool UseBots { get; set; }
		public string MapMode { get; set; } = "Deathmatch";
		public string CurrentMap { get; set; } = "Waiting...";
		public int CurrentPlayers { get; set; } = 1;
		public string HostSessionId { get; set; } = string.Empty;
		public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
	}

	public class TokenValidationRequest { public string Token { get; set; } = string.Empty; }

	public class StatReportRequest
	{
		public string Username { get; set; } = string.Empty;
		public int FragsAdded { get; set; }
		public int MinutesPlayed { get; set; }
		public int GemsAdded { get; set; }
	}

	public class SingleplayerSyncRequest
	{
		public string Token { get; set; } = string.Empty;
		public int Gems { get; set; }
		public string LoadoutId { get; set; } = string.Empty;
	}

	public class ServerListEntry
	{
		public string name { get; set; } = "";
		public string map { get; set; } = "";
		public string mode { get; set; } = "";
		public string hostName { get; set; } = "";
		public int players { get; set; }
		public int maxPlayers { get; set; }
		public int port { get; set; }
		public bool official { get; set; }
		public bool locked { get; set; }
		public bool bots { get; set; }
		public string joinUrl { get; set; } = "";
	}
}
