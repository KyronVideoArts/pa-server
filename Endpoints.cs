using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using MasterServer.Models;
using MasterServer.Services;
using System.IO;
using System.Collections.Generic;

namespace MasterServer
{
	public class StorePurchaseRequest
	{
		public string WeaponId { get; set; }
		public bool IsMultiplayer { get; set; }
		public int Cost { get; set; }
	}

	public class SetLoadoutRequest
	{
		public string WeaponId { get; set; } = string.Empty;
		public bool IsMultiplayer { get; set; }
	}

	public class EquipSpecialRequest
	{
		public string Id { get; set; } = string.Empty;
		public string Type { get; set; } = string.Empty;
		public bool IsMultiplayer { get; set; }
	}

	static class Html
	{
		// Shared nav + shell
		public static string Layout(string title, string body, bool loggedIn, string username = "", int mpGems = 0, int spGems = 0, string activePage = "")
		{
			string gemButtons = loggedIn
				? $@"<span class='gem-pill mp'><svg viewBox='0 0 24 24' fill='currentColor'><polygon points='12,2 22,8.5 22,15.5 12,22 2,15.5 2,8.5'/></svg>{mpGems} MP</span>
				     <span class='gem-pill sp'><svg viewBox='0 0 24 24' fill='currentColor'><polygon points='12,2 22,8.5 22,15.5 12,22 2,15.5 2,8.5'/></svg>{spGems} SP</span>"
				: "";

			string authBtn = loggedIn
				? $"<div class='avatar'>{(username.Length > 0 ? username[0].ToString().ToUpper() : "?")}</div><a class='btn-nav' href='/logout'>Logout</a>"
				: "<a class='btn-nav' href='/login'>Login</a><a class='btn-nav' style='background:linear-gradient(135deg,#4d9fff,#2563eb);color:#fff;border:none' href='/signup'>Sign Up</a>";

			string navLink(string href, string label, string ico, string page) =>
				$"<li><a href='{href}' class='{(activePage == page ? "active" : "")}'>{ico} {label}</a></li>";

			return $@"<!DOCTYPE html>
<html lang='en'>
<head>
  <meta charset='UTF-8'/>
  <meta name='viewport' content='width=device-width,initial-scale=1'/>
  <title>{title} - CaromMasse</title>
  <link rel='stylesheet' href='/css/site.css'/>
</head>
<body>
<div id='toast-container'></div>
<nav>
  <a href='/' class='nav-brand'>⬤ Carom<span>Masse</span></a>
  <ul class='nav-links'>
    {navLink("/", "Servers", "🌐", "home")}
    {(loggedIn ? navLink("/loadout", "Loadout", "🎒", "loadout") : "")}
    {(loggedIn ? navLink("/profile", "Profile", "👤", "profile") : "")}
  </ul>
  <div class='nav-right'>
    {gemButtons}
    {authBtn}
  </div>
</nav>
{body}
<script src='/js/app.js'></script>
</body>
</html>";
		}
	}

	public static class EndpointMapper
	{
		public static void MapAllEndpoints(this WebApplication app)
		{
			app.UseStaticFiles();
			MapUIEndpoints(app);
			MapAuthEndpoints(app);
			MapGameApiEndpoints(app);
			MapStoreEndpoints(app);
		}

		// ── helpers ───────────────────────────────────────────────────────────
		static async Task<PlayerProfile?> GetCurrentUser(WebApplication app, HttpContext context)
		{
			if (context.User.Identity?.IsAuthenticated != true) return null;
			using var scope = app.Services.CreateScope();
			var db = scope.ServiceProvider.GetRequiredService<MasterDbContext>();
			return await db.PlayerProfiles.FirstOrDefaultAsync(p => p.Username == context.User.Identity.Name);
		}

		static List<string> GetUnlocks(PlayerProfile p) =>
			JsonSerializer.Deserialize<List<string>>(p.UnlockedWeapons ?? "[\"default\"]") ?? new List<string> { "default" };

		static string WeaponCards(List<WeaponShopEntry> weapons, List<string> unlocks, string equipped, string mode)
		{
			var sb = new System.Text.StringBuilder();
			foreach (var w in weapons)
				sb.Append($@"<div class='weapon-card {(unlocks.Contains(w.Id) ? "" : "locked")} {(equipped == w.Id ? "selected" : "")}' 
					data-id='{w.Id}' data-owned='{(unlocks.Contains(w.Id) ? "true" : "false")}'
					onclick=""handleWeaponClick('{w.Id}', {(unlocks.Contains(w.Id) ? "true" : "false")}, '{mode}')"">
				  <div class='weapon-icon'><span id='icon-{w.Id}-{mode}'>{w.Icon}</span></div>
				  <div class='weapon-name'>{w.DisplayName}</div>
				  <div class='weapon-type'>{w.TriggerMode.Replace("_"," ")} · {w.SimType}</div>
				  <div class='weapon-stats'>
				    <div class='stat-row'><span class='stat-label'>Damage</span><div class='stat-bar-wrap'><div class='stat-bar' style='width:{w.DmgScore}%'></div></div></div>
				    <div class='stat-row'><span class='stat-label'>Range</span><div class='stat-bar-wrap'><div class='stat-bar' style='width:{w.RangeScore}%'></div></div></div>
				    <div class='stat-row'><span class='stat-label'>Fire Rate</span><div class='stat-bar-wrap'><div class='stat-bar' style='width:{w.SpeedScore}%'></div></div></div>
				  </div>
				  <div class='weapon-price'>
				    {(unlocks.Contains(w.Id)
				        ? (equipped == w.Id ? "<span class='equip-badge'>✓ Equipped</span>" : "<span class='owned-tag'>✓ Owned</span>")
				        : $"<span class='locked-tag'>🔒 {w.GemCost} Gems</span>")}
				    {(unlocks.Contains(w.Id)
				        ? (equipped == w.Id ? "" : $"<button class='btn-join' style='padding:0.3rem 0.75rem;font-size:0.75rem' onclick=\"event.stopPropagation();equipWeapon('{w.Id}','{mode}')\">Equip</button>")
				        : $"<button class='btn-join' style='padding:0.3rem 0.75rem;font-size:0.75rem;background:linear-gradient(135deg,#a855f7,#7c3aed)' onclick=\"event.stopPropagation();buyWeapon('{w.Id}','{mode}')\">Buy {w.GemCost}💎</button>")}
				  </div>
				</div>");
			return sb.ToString();
		}

		// ── weapon shop entries (pulled from JSON files) ───────────────────────
		static List<WeaponShopEntry> LoadWeaponEntries(WebApplication app)
		{
			string path = Path.Combine(app.Environment.ContentRootPath, "GameData", "Weapons", "weapon_definitions.json");
			if (!File.Exists(path)) return new List<WeaponShopEntry>();
			var doc = JsonDocument.Parse(File.ReadAllText(path));
			var list = new List<WeaponShopEntry>();
			foreach (var el in doc.RootElement.GetProperty("weapons").EnumerateArray())
			{
				string id = el.GetProperty("id").GetString() ?? "";
				double dmg = el.GetProperty("damage").GetDouble();
				double range = el.GetProperty("range").GetDouble();
				double fire = el.GetProperty("fireIntervalSeconds").GetDouble();
				int shots = el.GetProperty("projectilesPerShot").GetInt32();

				double dps = (dmg * shots) / Math.Max(fire, 0.01);
				int dmgScore = Math.Min(100, (int)((dps / 25.0) * 100));
				int rangeScore = Math.Min(100, (int)((range / 6000.0) * 100));
				int speedScore = Math.Max(5, (int)(100 - (fire / 0.9) * 100));

				string[] icons = { "blaster=⚡", "machinegun=🔫", "shotgun=💥", "nailgun=🔩", "lightning=🌩️" };
				string icon = "🗡️";
				foreach (var i in icons) { var p = i.Split('='); if (p[0] == id) { icon = p[1]; break; } }

				int cost = id switch { "blaster" => 0, "machinegun" => 50, "shotgun" => 75, "nailgun" => 100, "lightning" => 150, _ => 100 };

				list.Add(new WeaponShopEntry
				{
					Id = id,
					DisplayName = el.GetProperty("displayName").GetString() ?? id,
					TriggerMode = el.GetProperty("triggerMode").GetString() ?? "",
					SimType = el.GetProperty("simulationType").GetString() ?? "",
					DmgScore = dmgScore,
					RangeScore = rangeScore,
					SpeedScore = speedScore,
					Icon = icon,
					GemCost = cost,
					IsDefault = id == "blaster"
				});
			}
			return list;
		}

		static void RemoveLegacyLobby(string joinCode)
		{
			ServerState.ActiveLobbies.TryRemove(joinCode, out _);
			if (ServerState.ActiveRelays.TryRemove(joinCode, out var relay))
			{
				relay.Stop();
			}
		}

		static void RemoveIceLobby(string joinCode)
		{
			ServerState.ActiveIceLobbies.TryRemove(joinCode, out _);
			var staleSessions = ServerState.ActiveIceSessions
				.Where(kv => kv.Value.LobbyCode == joinCode)
				.Select(kv => kv.Key)
				.ToList();
			foreach (var sessionId in staleSessions)
			{
				ServerState.ActiveIceSessions.TryRemove(sessionId, out _);
			}
		}

		static void PruneStaleGameState()
		{
			var staleLegacy = ServerState.ActiveLobbies
				.Where(kv => (DateTime.UtcNow - kv.Value.LastHeartbeat).TotalMinutes > 5)
				.Select(kv => kv.Key)
				.ToList();
			foreach (var joinCode in staleLegacy)
			{
				RemoveLegacyLobby(joinCode);
			}

			var staleIce = ServerState.ActiveIceLobbies
				.Where(kv => (DateTime.UtcNow - kv.Value.LastHeartbeat).TotalMinutes > 5)
				.Select(kv => kv.Key)
				.ToList();
			foreach (var joinCode in staleIce)
			{
				RemoveIceLobby(joinCode);
			}
		}

		static string GenerateJoinCode()
		{
			const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
			var random = Random.Shared;
			for (int attempt = 0; attempt < 32; ++attempt)
			{
				var chars = new char[6];
				for (int i = 0; i < chars.Length; ++i)
				{
					chars[i] = alphabet[random.Next(alphabet.Length)];
				}
				var joinCode = new string(chars);
				if (!ServerState.ActiveIceLobbies.ContainsKey(joinCode) && !ServerState.ActiveLobbies.ContainsKey(joinCode))
				{
					return joinCode;
				}
			}

			return Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
		}

		static string[] GetConfiguredIceServers(IConfiguration configuration, string sectionName)
		{
			return configuration
				.GetSection(sectionName)
				.Get<string[]>()?
				.Where(value => !string.IsNullOrWhiteSpace(value))
				.ToArray()
				?? Array.Empty<string>();
		}

		static void ForwardIceEvent(string sessionId, IceSessionEvent sessionEvent)
		{
			if (ServerState.ActiveIceSessions.TryGetValue(sessionId, out var targetSession))
			{
				targetSession.AddEvent(sessionEvent);
				targetSession.LastUpdatedUtc = DateTime.UtcNow;
			}
		}

		// ── UI endpoints ──────────────────────────────────────────────────────
		static void MapUIEndpoints(WebApplication app)
		{
			// Home / Server List
			app.MapGet("/", async context =>
			{
				bool loggedIn = context.User.Identity?.IsAuthenticated == true;
				string username = context.User.Identity?.Name ?? "";
				int mpGems = 0, spGems = 0;

				if (loggedIn)
				{
					using var scope = app.Services.CreateScope();
					var db = scope.ServiceProvider.GetRequiredService<MasterDbContext>();
					var profile = await db.PlayerProfiles.FirstOrDefaultAsync(p => p.Username == username);
					if (profile != null) { mpGems = profile.MultiplayerGems; spGems = profile.SingleplayerGems; }
				}

				// Evict stale lobbies
				PruneStaleGameState();

				var serverRows = new System.Text.StringBuilder();
				foreach (var lobby in ServerState.ActiveLobbies.Values)
				{
					string joinUrl = "#";
					if (loggedIn)
					{
						var token = Guid.NewGuid().ToString("N");
						ServerState.JoinTokens[token] = username;
						joinUrl = $"gameprotocol://join/{lobby.AdvertisedAddress}:{lobby.AdvertisedPort}?token={token}";
					}
					serverRows.Append($@"
					<a class='server-row' href='{joinUrl}'>
					  <div class='server-status-dot'></div>
					  <div style='flex:1'>
					    <div class='server-name'>{lobby.LobbyName}</div>
					    <div class='server-map' style='display:flex;gap:1.25rem;margin-top:0.4rem;align-items:center'>
					      <span>📍 {lobby.CurrentMap}</span>
					      <span style='color:var(--text-muted);font-size:0.75rem;padding:0.1rem 0.4rem;border-radius:4px;border:1px solid rgba(255,255,255,0.1)'>{lobby.MapMode}</span>
					      <span style='color:var(--text-muted);display:flex;align-items:center;gap:0.3rem'>
					        <svg width='12' height='12' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'><path d='M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2'></path><circle cx='12' cy='7' r='4'></circle></svg>
					        {lobby.HostName}
					      </span>
					      <span style='color:var(--text-muted);display:flex;align-items:center;gap:0.3rem'>
					        👥 {lobby.CurrentPlayers}/{lobby.MaxPlayers}
					      </span>
					      <span style='color:var(--text-muted);background:rgba(255,255,255,0.05);padding:0.1rem 0.4rem;border-radius:4px;font-family:monospace;font-size:0.75rem' title='Connect via console: connect master_ip:{lobby.AdvertisedPort}'>
					        🔌 {lobby.AdvertisedPort}
					      </span>
					      {(lobby.UseBots ? "<span title='Bots Enabled'>🤖</span>" : "")}
					    </div>
					  </div>
					  <div style='display:flex;gap:0.4rem;align-items:center;height:fit-content'>
					    {(lobby.IsOfficial ? "<span class='server-badge badge-official'>Official</span>" : "<span class='server-badge badge-p2p'>P2P</span>")}
					    {(lobby.HasPassword ? "<span class='server-badge badge-locked'>🔒</span>" : "")}
					  </div>
					  <div style='display:flex;align-items:center'>
					    {(loggedIn ? $"<span class='btn-join'>Join →</span>" : "<span style='font-size:0.8rem;color:var(--text-muted)'>Login to Join</span>")}
					  </div>
					</a>");
				}

				foreach (var lobby in ServerState.ActiveIceLobbies.Values)
				{
					string joinUrl = "#";
					if (loggedIn)
					{
						var token = Guid.NewGuid().ToString("N");
						ServerState.JoinTokens[token] = username;
						joinUrl = $"gameprotocol://join/{lobby.JoinCode}?token={token}";
					}
					serverRows.Append($@"
					<a class='server-row' href='{joinUrl}'>
					  <div class='server-status-dot'></div>
					  <div style='flex:1'>
					    <div class='server-name'>{lobby.LobbyName}</div>
					    <div class='server-map' style='display:flex;gap:1.25rem;margin-top:0.4rem;align-items:center'>
					      <span>ðŸ“ {lobby.CurrentMap}</span>
					      <span style='color:var(--text-muted);font-size:0.75rem;padding:0.1rem 0.4rem;border-radius:4px;border:1px solid rgba(255,255,255,0.1)'>{lobby.MapMode}</span>
					      <span style='color:var(--text-muted);display:flex;align-items:center;gap:0.3rem'>
					        <svg width='12' height='12' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'><path d='M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2'></path><circle cx='12' cy='7' r='4'></circle></svg>
					        {lobby.HostName}
					      </span>
					      <span style='color:var(--text-muted);display:flex;align-items:center;gap:0.3rem'>
					        ðŸ‘¥ {lobby.CurrentPlayers}/{lobby.MaxPlayers}
					      </span>
					      <span style='color:var(--text-muted);background:rgba(255,255,255,0.05);padding:0.1rem 0.4rem;border-radius:4px;font-family:monospace;font-size:0.75rem' title='ICE join code'>
					        ðŸ”Œ {lobby.JoinCode}
					      </span>
					      {(lobby.UseBots ? "<span title='Bots Enabled'>ðŸ¤–</span>" : "")}
					    </div>
					  </div>
					  <div style='display:flex;gap:0.4rem;align-items:center;height:fit-content'>
					    {(lobby.IsOfficial ? "<span class='server-badge badge-official'>Official</span>" : "<span class='server-badge badge-p2p'>P2P</span>")}
					    {(lobby.HasPassword ? "<span class='server-badge badge-locked'>ðŸ”’</span>" : "")}
					  </div>
					  <div style='display:flex;align-items:center'>
					    {(loggedIn ? $"<span class='btn-join'>Join â†’</span>" : "<span style='font-size:0.8rem;color:var(--text-muted)'>Login to Join</span>")}
					  </div>
					</a>");
				}

				string serversHtml = (ServerState.ActiveLobbies.Count + ServerState.ActiveIceLobbies.Count) == 0
					? "<div class='empty-state'><div style='font-size:3rem'>🛸</div><p style='margin-top:0.75rem'>No servers online right now.</p></div>"
					: serverRows.ToString();

				// Escape username for placeholder
				string safeUsername = username.Replace("'", "&#39;");

				string hostHtml = !loggedIn ? "" : $@"
				<div class='card mt-8 mb-8'>
				  <div class='section-title'>Host a Game</div>
				  <div class='host-form' style='max-width:500px'>
				    <div><label>Server Name</label><input id='hname' type='text' placeholder='{safeUsername}&#39;s Game' value='{safeUsername}&#39;s Game'/></div>
				    
				    <div><label>Map</label>
				      <select id='hmap' onchange='updateMode()'>
				        <option value='criticalfreight' data-mode='Deathmatch'>Critical Freight</option>
				        <option value='voidrun' data-mode='Deathmatch'>Void Run</option>
				        <option value='example_map' data-mode='Deathmatch'>Test Map</option>
				      </select>
				    </div>
				    
				    <div><label>Mode</label><div id='hmode' style='padding:0.5rem 0;color:var(--text-muted);font-weight:600;font-size:0.9rem'>Deathmatch</div></div>
				    
				    <div style='display:grid;grid-template-columns:1fr 1fr;gap:1.5rem'>
				      <div><label>Max Players</label><input id='hmax' type='number' min='2' max='64' value='16' style='width:100%'/></div>
				      <div><label style='display:flex;align-items:center;height:100%;gap:0.75rem;cursor:pointer;margin-top:1.5rem'>
					    <input type='checkbox' id='hbots' style='width:1.2rem;height:1.2rem'> Enable Bots
					  </label></div>
				    </div>
				    
				    <div><label>Password (optional)</label><input id='hpwd' type='text' placeholder='Leave blank for public'/></div>
				    <div style='padding-bottom:0;margin-top:1rem'>
				      <button class='btn-join' onclick='doHost()' style='padding:0.65rem 1.25rem;font-size:1rem'>🚀 Host Game</button>
				    </div>
				  </div>
				</div>
				<script>
				function updateMode() {{
				  var sel = document.getElementById('hmap');
				  var opt = sel.options[sel.selectedIndex];
				  document.getElementById('hmode').textContent = opt.getAttribute('data-mode');
				}}
				async function doHost() {{
				  var t = await (await fetch('/api/v1/auth/hosttoken')).text();
				  var pwd = document.getElementById('hpwd').value;
				  var sel = document.getElementById('hmap');
				  var map = sel.value;
				  var mode = sel.options[sel.selectedIndex].getAttribute('data-mode');
				  var name = document.getElementById('hname').value;
				  var bots = document.getElementById('hbots').checked ? 1 : 0;
				  var max = document.getElementById('hmax').value;
				  window.location.href = 'gameprotocol://host/' + map + '?token=' + t + '&name=' + encodeURIComponent(name) + '&max=' + max + '&bots=' + bots + '&mode=' + encodeURIComponent(mode) + (pwd ? '&password=' + pwd : '');
				}}
				</script>";

				string body = $@"
				<div class='page-wrap'>
				  <div class='hero'>
				    <h1>Carom<em>Masse</em></h1>
				    <p>Choose a server and drop in. {(loggedIn ? "Your loadout is ready." : "Sign in to save your progress.")}</p>
				  </div>

				  <div class='section-title mt-8'>🌐 Live Servers</div>
				  <div class='server-grid' id='server-list'>{serversHtml}</div>

				  {hostHtml}
				</div>";

				context.Response.ContentType = "text/html";
				await context.Response.WriteAsync(Html.Layout("Home", body, loggedIn, username, mpGems, spGems, "home"));
			});

			// ── Profile ───────────────────────────────────────────────────────
			app.MapGet("/profile", async context =>
			{
				if (context.User.Identity?.IsAuthenticated != true) { context.Response.Redirect("/login"); return; }
				using var scope = app.Services.CreateScope();
				var db = scope.ServiceProvider.GetRequiredService<MasterDbContext>();
				var u = await db.PlayerProfiles.FirstOrDefaultAsync(p => p.Username == context.User.Identity.Name);

				string kd = u.TotalFrags > 0 ? u.TotalFrags.ToString() : "—";

				string body = $@"
				<div class='page-wrap'>
				  <div class='page-header'>
				    <h1>Player Profile</h1>
				    <p>Your global game stats and progression.</p>
				  </div>
				  <div class='stats-grid'>
				    <div class='stat-card'><div class='num'>{u.TotalFrags}</div><div class='label'>Total Frags</div></div>
				    <div class='stat-card'><div class='num'>{u.TimePlayedMinutes}</div><div class='label'>Mins Played</div></div>
				    <div class='stat-card'><div class='num'>{u.MultiplayerGems}</div><div class='label'>MP Gems</div></div>
				    <div class='stat-card'><div class='num'>{u.SingleplayerGems}</div><div class='label'>SP Gems</div></div>
				  </div>
				  <div class='card mt-8'>
				    <div class='section-title' style='margin-bottom:0.75rem'>Loadouts</div>
				    <div style='display:flex;gap:1rem;flex-wrap:wrap'>
				      <div>
				        <div style='font-size:0.75rem;color:var(--text-muted);text-transform:uppercase;letter-spacing:.06em;margin-bottom:.3rem'>Multiplayer Active</div>
				        <span class='server-badge badge-official' style='font-size:0.85rem'>{u.LoadoutId}</span>
				      </div>
				      <div>
				        <div style='font-size:0.75rem;color:var(--text-muted);text-transform:uppercase;letter-spacing:.06em;margin-bottom:.3rem'>Singleplayer Active</div>
				        <span class='server-badge badge-p2p' style='font-size:0.85rem'>{u.SingleplayerLoadoutId}</span>
				      </div>
				    </div>
				    <div style='margin-top:1rem'>
				      <a href='/loadout' class='btn-join'>Customize Loadout →</a>
				    </div>
				  </div>
				</div>";

				context.Response.ContentType = "text/html";
				await context.Response.WriteAsync(Html.Layout("Profile", body, true, u.Username, u.MultiplayerGems, u.SingleplayerGems, "profile"));
			});

			// ── Loadout Builder ───────────────────────────────────────────────
			app.MapGet("/loadout", async context =>
			{
				if (context.User.Identity?.IsAuthenticated != true) { context.Response.Redirect("/login"); return; }
				using var scope = app.Services.CreateScope();
				var db = scope.ServiceProvider.GetRequiredService<MasterDbContext>();
				var u = await db.PlayerProfiles.FirstOrDefaultAsync(p => p.Username == context.User.Identity.Name);

				var weapons = LoadWeaponEntries(app);
				// Always unlock "blaster" for everyone
				var unlocks = GetUnlocks(u);
				if (!unlocks.Contains("blaster")) { unlocks.Insert(0, "blaster"); }

				string mpCards = WeaponCards(weapons, unlocks, u.LoadoutId, "mp");
				string spCards = WeaponCards(weapons, unlocks, u.SingleplayerLoadoutId, "sp");

				string getSpecialOpts(bool mp) {
					var g = mp ? u.MultiplayerGadget : u.SingleplayerGadget;
					var pJson = mp ? u.MultiplayerPerks : u.SingleplayerPerks;
					var pList = JsonSerializer.Deserialize<List<string>>(pJson ?? "[]") ?? new List<string>();
					return $@"
					<div class='opt-item' onclick='equipSpecial(""grapple"", ""gadget"", {(mp?"true":"false")})'>
					  <span class='opt-label'>Grapple Hook Gadget</span><div class='opt-check {(g == "grapple" ? "on" : "off")}'></div>
					</div>
					<div class='opt-item' onclick='equipSpecial("""", ""gadget"", {(mp?"true":"false")})'>
					  <span class='opt-label'>No Gadget</span><div class='opt-check {((string.IsNullOrEmpty(g) || g == "none") ? "on" : "off")}'></div>
					</div>
					<div class='opt-item' onclick='equipSpecial(""jetpackpowerup"", ""perk"", {(mp?"true":"false")})'>
					  <span class='opt-label'>Jetpack Perk</span><div class='opt-check {(pList.Contains("jetpackpowerup") ? "on" : "off")}'></div>
					</div>
					<div class='opt-item' onclick='equipSpecial(""speedboostpowerup"", ""perk"", {(mp?"true":"false")})'>
					  <span class='opt-label'>Speed Boost Perk</span><div class='opt-check {(pList.Contains("speedboostpowerup") ? "on" : "off")}'></div>
					</div>
					<div class='opt-item' onclick='equipSpecial(""quadjumppowerup"", ""perk"", {(mp?"true":"false")})'>
					  <span class='opt-label'>Quad Jump Perk</span><div class='opt-check {(pList.Contains("quadjumppowerup") ? "on" : "off")}'></div>
					</div>";
				};

				string slotsMp = $@"
				<div class='slot-item {(u.LoadoutId != "" ? "active-slot" : "")}'><span class='slot-emoji'>🔫</span><span>{(u.LoadoutId != "" ? u.LoadoutId : "None")}</span></div>";
				string slotsSp = $@"
				<div class='slot-item {(u.SingleplayerLoadoutId != "" ? "active-slot" : "")}'><span class='slot-emoji'>🔫</span><span>{(u.SingleplayerLoadoutId != "" ? u.SingleplayerLoadoutId : "None")}</span></div>";

				string body = $@"
				<div class='page-wrap'>
				  <div class='page-header'>
				    <h1>🎒 Loadout Customizer</h1>
				    <p>Configure your Multiplayer and Singleplayer loadouts independently. Buy new weapons with Gems.</p>
				  </div>

				  <div class='loadout-builder'>
				    <div>
				      <div class='loadout-tabs'>
				        <button class='tab-btn' data-mode='mp' onclick='switchMode(""mp"")'>⚔ Multiplayer Loadout</button>
				        <button class='tab-btn' data-mode='sp' onclick='switchMode(""sp"")'>🎮 Singleplayer Loadout</button>
				      </div>

				      <div class='mode-panel' data-mode='mp'>
				        <div class='section-title'>MP Weapons · <span style='color:var(--gem-mp)'>{u.MultiplayerGems} Gems</span></div>
				        <div class='weapons-grid'>{mpCards}</div>
				      </div>
				      <div class='mode-panel' data-mode='sp' style='display:none'>
				        <div class='section-title'>SP Weapons · <span style='color:var(--gem-sp)'>{u.SingleplayerGems} Gems</span></div>
				        <div class='weapons-grid'>{spCards}</div>
				      </div>
				    </div>

				    <div>
				      <div class='loadout-panel'>
				        <h3 id='panel-title'>⚔ Multiplayer Loadout</h3>
				        <div class='section-title' style='font-size:0.75rem'>Active Weapon</div>
				        <div class='slot-list' id='panel-slots'>
				          <div class='mode-panel' data-mode='mp'>{slotsMp}</div>
				          <div class='mode-panel' data-mode='sp' style='display:none'>{slotsSp}</div>
				        </div>
				        <div class='section-title' style='font-size:0.75rem'>Special Abilities</div>
				        <div class='special-opts' id='panel-special'>
				          <div class='mode-panel' data-mode='mp'>{getSpecialOpts(true)}</div>
				          <div class='mode-panel' data-mode='sp' style='display:none'>{getSpecialOpts(false)}</div>
				        </div>
				        <div style='margin-top:1.25rem'>
				          <div style='font-size:0.72rem;color:var(--text-muted);text-transform:uppercase;letter-spacing:.06em;margin-bottom:.4rem'>Click any owned weapon to equip it</div>
				          <a href='/profile' class='btn-join' style='display:block;text-align:center;text-decoration:none;padding:.6rem'>View Full Profile →</a>
				        </div>
				      </div>
				    </div>
				  </div>
				</div>
				<script>
				  // Update panel title on tab switch
				  const _origSwitch = window.switchMode;
				  window.switchMode = function(mode) {{
				    _origSwitch && _origSwitch(mode);
				    document.getElementById('panel-title').textContent = mode === 'mp' ? '⚔ Multiplayer Loadout' : '🎮 Singleplayer Loadout';
				    // sync slot panels
				    document.querySelectorAll('#panel-slots .mode-panel, #panel-special .mode-panel').forEach(p => p.style.display = p.dataset.mode === mode ? '' : 'none');
				  }};
				</script>";

				context.Response.ContentType = "text/html";
				await context.Response.WriteAsync(Html.Layout("Loadout", body, true, u.Username, u.MultiplayerGems, u.SingleplayerGems, "loadout"));
			});
		}

		// ── Auth ──────────────────────────────────────────────────────────────
		static void MapAuthEndpoints(WebApplication app)
		{
			app.MapGet("/signup", async context =>
			{
				context.Response.ContentType = "text/html";
				await context.Response.WriteAsync(Html.Layout("Sign Up", @"
				<div class='auth-wrap'>
				  <div class='auth-card'>
				    <h2>Create Account</h2>
				    <p>Join the fight. Your progress, your loadout.</p>
				    <form method='post' action='/signup'>
				      <div class='form-group'><label>Username</label><input type='text' name='username' autocomplete='username' required/></div>
				      <div class='form-group'><label>Password</label><input type='password' name='password' autocomplete='new-password' required/></div>
				      <button type='submit' class='btn-primary'>Create Account</button>
				    </form>
				    <div class='auth-link'>Already have an account? <a href='/login'>Login</a></div>
				  </div>
				</div>", false));
			});

			app.MapPost("/signup", async context =>
			{
				var form = await context.Request.ReadFormAsync();
				var username = form["username"].ToString();
				var password = form["password"].ToString();

				using var scope = app.Services.CreateScope();
				var db = scope.ServiceProvider.GetRequiredService<MasterDbContext>();

				if (await db.PlayerProfiles.AnyAsync(p => p.Username == username))
				{
					context.Response.ContentType = "text/html";
					await context.Response.WriteAsync(Html.Layout("Sign Up", "<div class='page-wrap'><div class='card mt-8'>Username already taken. <a href='/signup'>Try again</a></div></div>", false));
					return;
				}

				var profile = new PlayerProfile { Username = username, PasswordHash = password, UnlockedWeapons = "[\"blaster\"]" };
				db.PlayerProfiles.Add(profile);
				await db.SaveChangesAsync();
				context.Response.Redirect("/login");
			});

			app.MapGet("/login", async context =>
			{
				context.Response.ContentType = "text/html";
				await context.Response.WriteAsync(Html.Layout("Login", @"
				<div class='auth-wrap'>
				  <div class='auth-card'>
				    <h2>Welcome Back</h2>
				    <p>Sign in to access your profile and loadout.</p>
				    <form method='post' action='/login'>
				      <div class='form-group'><label>Username</label><input type='text' name='username' autocomplete='username' required/></div>
				      <div class='form-group'><label>Password</label><input type='password' name='password' autocomplete='current-password' required/></div>
				      <button type='submit' class='btn-primary'>Login</button>
				    </form>
				    <div class='auth-link'>New here? <a href='/signup'>Create an account</a></div>
				  </div>
				</div>", false));
			});

			app.MapPost("/login", async context =>
			{
				var form = await context.Request.ReadFormAsync();
				var username = form["username"].ToString();
				var password = form["password"].ToString();

				using var scope = app.Services.CreateScope();
				var db = scope.ServiceProvider.GetRequiredService<MasterDbContext>();
				var user = await db.PlayerProfiles.FirstOrDefaultAsync(p => p.Username == username && p.PasswordHash == password);
				if (user != null)
				{
					var claims = new[] { new Claim(ClaimTypes.Name, user.Username) };
					var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
					await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
					context.Response.Redirect("/");
				}
				else
				{
					context.Response.ContentType = "text/html";
					await context.Response.WriteAsync(Html.Layout("Login", @"
					<div class='auth-wrap'>
					  <div class='auth-card'>
					    <h2>Welcome Back</h2>
					    <p style='color:var(--danger)'>⚠ Invalid username or password.</p>
					    <form method='post' action='/login'>
					      <div class='form-group'><label>Username</label><input type='text' name='username' required/></div>
					      <div class='form-group'><label>Password</label><input type='password' name='password' required/></div>
					      <button type='submit' class='btn-primary'>Login</button>
					    </form>
					    <div class='auth-link'>New here? <a href='/signup'>Create an account</a></div>
					  </div>
					</div>", false));
				}
			});

			app.MapGet("/logout", async context =>
			{
				await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
				context.Response.Redirect("/");
			});
		}

		// ── Store API ─────────────────────────────────────────────────────────
		static void MapStoreEndpoints(WebApplication app)
		{
			app.MapPost("/api/v1/store/buy", async context =>
			{
				if (context.User.Identity?.IsAuthenticated != true) { context.Response.StatusCode = 401; return; }
				using var reader = new StreamReader(context.Request.Body);
				var req = JsonSerializer.Deserialize<StorePurchaseRequest>(await reader.ReadToEndAsync(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

				using var scope = app.Services.CreateScope();
				var db = scope.ServiceProvider.GetRequiredService<MasterDbContext>();
				var user = await db.PlayerProfiles.FirstOrDefaultAsync(p => p.Username == context.User.Identity.Name);

				var unlocks = JsonSerializer.Deserialize<List<string>>(user.UnlockedWeapons ?? "[]") ?? new List<string>();
				if (unlocks.Contains(req.WeaponId)) { await context.Response.WriteAsync("Already unlocked!"); return; }

				// Find cost from weapon definitions
				var weapons = LoadWeaponEntries(app);
				var entry = weapons.FirstOrDefault(w => w.Id == req.WeaponId);
				int cost = entry?.GemCost ?? 100;

				if (req.IsMultiplayer)
				{
					if (user.MultiplayerGems < cost) { await context.Response.WriteAsync($"Not enough MP Gems! (Need {cost})"); return; }
					user.MultiplayerGems -= cost;
				}
				else
				{
					if (user.SingleplayerGems < cost) { await context.Response.WriteAsync($"Not enough SP Gems! (Need {cost})"); return; }
					user.SingleplayerGems -= cost;
				}

				unlocks.Add(req.WeaponId);
				user.UnlockedWeapons = JsonSerializer.Serialize(unlocks);
				await db.SaveChangesAsync();
				await context.Response.WriteAsync($"✓ {entry?.DisplayName ?? req.WeaponId} unlocked!");
			});

			app.MapPost("/api/v1/store/equip", async context =>
			{
				if (context.User.Identity?.IsAuthenticated != true) { context.Response.StatusCode = 401; return; }
				using var reader = new StreamReader(context.Request.Body);
				var req = JsonSerializer.Deserialize<SetLoadoutRequest>(await reader.ReadToEndAsync(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

				using var scope = app.Services.CreateScope();
				var db = scope.ServiceProvider.GetRequiredService<MasterDbContext>();
				var user = await db.PlayerProfiles.FirstOrDefaultAsync(p => p.Username == context.User.Identity.Name);

				var unlocks = JsonSerializer.Deserialize<List<string>>(user.UnlockedWeapons ?? "[]") ?? new List<string>();
				if (req.WeaponId != "blaster" && !unlocks.Contains(req.WeaponId))
				{
					context.Response.StatusCode = 403;
					await context.Response.WriteAsync("You don't own that weapon.");
					return;
				}

				if (req.IsMultiplayer) user.LoadoutId = req.WeaponId;
				else user.SingleplayerLoadoutId = req.WeaponId;

				await db.SaveChangesAsync();
				await context.Response.WriteAsync($"✓ Equipped to {(req.IsMultiplayer ? "Multiplayer" : "Singleplayer")} loadout!");
			});

			app.MapPost("/api/v1/store/equip_special", async context =>
			{
				if (context.User.Identity?.IsAuthenticated != true) { context.Response.StatusCode = 401; return; }
				using var reader = new StreamReader(context.Request.Body);
				var req = JsonSerializer.Deserialize<EquipSpecialRequest>(await reader.ReadToEndAsync(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

				using var scope = app.Services.CreateScope();
				var db = scope.ServiceProvider.GetRequiredService<MasterDbContext>();
				var user = await db.PlayerProfiles.FirstOrDefaultAsync(p => p.Username == context.User.Identity.Name);

				if (req.Type == "gadget")
				{
					if (req.IsMultiplayer) user.MultiplayerGadget = req.Id;
					else user.SingleplayerGadget = req.Id;
				}
				else if (req.Type == "perk")
				{
					var perksJson = req.IsMultiplayer ? user.MultiplayerPerks : user.SingleplayerPerks;
					var perks = JsonSerializer.Deserialize<List<string>>(perksJson ?? "[]") ?? new List<string>();
					
					if (perks.Contains(req.Id))
						perks.Remove(req.Id);
					else
						perks.Add(req.Id);

					if (req.IsMultiplayer) user.MultiplayerPerks = JsonSerializer.Serialize(perks);
					else user.SingleplayerPerks = JsonSerializer.Serialize(perks);
				}

				await db.SaveChangesAsync();
				await context.Response.WriteAsync($"✓ Updated loadout!");
			});
		}

		// ── Game API ──────────────────────────────────────────────────────────
		static void MapGameApiEndpoints(WebApplication app)
		{
			// JSON server list for AJAX refresh
			app.MapGet("/api/v1/servers", async context =>
			{
				context.Response.ContentType = "application/json";
				bool loggedIn = context.User.Identity?.IsAuthenticated == true;
				string username = context.User.Identity?.Name ?? "";

				PruneStaleGameState();

				var legacyEntries = ServerState.ActiveLobbies.Values.Select(l => {
					string joinUrl = "";
					if (loggedIn) {
						var t = Guid.NewGuid().ToString("N");
						ServerState.JoinTokens[t] = username;
						joinUrl = $"gameprotocol://join/{l.AdvertisedAddress}:{l.AdvertisedPort}?token={t}";
					}
					return new ServerListEntry {
						name = l.LobbyName,
						map = l.CurrentMap,
						mode = l.MapMode,
						hostName = l.HostName,
						players = l.CurrentPlayers,
						maxPlayers = l.MaxPlayers,
						port = l.AdvertisedPort,
						official = l.IsOfficial,
						locked = l.HasPassword,
						bots = l.UseBots,
						joinUrl = joinUrl
					};
				});
				var iceEntries = ServerState.ActiveIceLobbies.Values.Select(l => {
					string joinUrl = "";
					if (loggedIn) {
						var t = Guid.NewGuid().ToString("N");
						ServerState.JoinTokens[t] = username;
						joinUrl = $"gameprotocol://join/{l.JoinCode}?token={t}";
					}
					return new ServerListEntry {
						name = l.LobbyName,
						map = l.CurrentMap,
						mode = l.MapMode,
						hostName = l.HostName,
						players = l.CurrentPlayers,
						maxPlayers = l.MaxPlayers,
						port = l.RequestedPort,
						official = l.IsOfficial,
						locked = l.HasPassword,
						bots = l.UseBots,
						joinUrl = joinUrl
					};
				});
				await context.Response.WriteAsJsonAsync(legacyEntries.Concat(iceEntries));
			});

			app.MapGet("/api/v1/auth/hosttoken", context =>
			{
				if (context.User.Identity?.IsAuthenticated != true) { context.Response.StatusCode = 401; return Task.CompletedTask; }
				var tok = Guid.NewGuid().ToString("N");
				ServerState.JoinTokens[tok] = context.User.Identity.Name!;
				return context.Response.WriteAsync(tok);
			});

			app.MapGet("/api/v1/lobbies/{code}", async context =>
			{
				var code = context.Request.RouteValues["code"]?.ToString()?.Trim().ToUpperInvariant();
				if (code != null && ServerState.ActiveLobbies.TryGetValue(code, out var lobby))
				{
					context.Response.ContentType = "application/json";
					var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
					await JsonSerializer.SerializeAsync(context.Response.Body, new
					{
						advertisedAddress = lobby.AdvertisedAddress,
						advertisedPort = lobby.AdvertisedPort,
						joinCode = lobby.JoinCode,
						hasPassword = lobby.HasPassword,
						currentMap = lobby.CurrentMap
					}, options);
				}
				else { context.Response.StatusCode = 404; }
			});

			app.MapPost("/api/v1/host/close", async context =>
			{
				using var reader = new StreamReader(context.Request.Body);
				var body = await reader.ReadToEndAsync();
				// The body should contain the JoinCode
				var doc = JsonDocument.Parse(body);
				if (doc.RootElement.TryGetProperty("joinCode", out var codeProp))
				{
					var code = codeProp.GetString();
					if (code != null)
					{
						RemoveLegacyLobby(code);
					}
				}
				context.Response.StatusCode = 200;
			});

			app.MapPost("/api/v1/host/prepare", async context =>
			{
				using var reader = new StreamReader(context.Request.Body);
				var body = await reader.ReadToEndAsync();
				var request = JsonSerializer.Deserialize<PrepareLobbyRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

				var hostAddr = context.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
				if (hostAddr == "::1") hostAddr = "127.0.0.1";
				
				var hostIp = System.Net.IPAddress.Parse(hostAddr);
				if (hostIp.IsIPv4MappedToIPv6) hostIp = hostIp.MapToIPv4();
				hostAddr = hostIp.ToString();

				var advertisedAddress = app.Configuration["AddressOverride"];
				if (string.IsNullOrEmpty(advertisedAddress))
				{
					advertisedAddress = context.Request.Host.Host;
				}

				var hostEp = new System.Net.IPEndPoint(hostIp, request?.RequestedPort ?? 64087);

				// REUSE OR RECREATE: Check if a relay for this host already exists and is fresh.
				// We identify the same host session by HostToken.
				var existing = ServerState.ActiveRelays
					.FirstOrDefault(kv => !string.IsNullOrEmpty(kv.Value.HostToken)
						&& kv.Value.HostToken.Equals(request?.HostToken));

				if (existing.Value != null)
				{
					if (ServerState.ActiveLobbies.TryGetValue(existing.Key, out var existingLobby))
					{
						// Refresh lobby info in case name or settings changed
						existingLobby.LobbyName = !string.IsNullOrEmpty(request?.LobbyName) ? request.LobbyName : existingLobby.LobbyName;
						existingLobby.HostName = request?.HostName ?? existingLobby.HostName;
						existingLobby.MaxPlayers = request?.MaxPlayers ?? existingLobby.MaxPlayers;
						existingLobby.UseBots = request?.UseBots ?? existingLobby.UseBots;
						existingLobby.MapMode = request?.MapMode ?? existingLobby.MapMode;
						existingLobby.IsOfficial = request?.ServerApiKey == ServerState.ServerApiKey;
						existingLobby.LastHeartbeat = DateTime.UtcNow;

						Console.WriteLine($"[PROXY] Reusing existing relay {existing.Key} for host token {request?.HostToken}");
						context.Response.ContentType = "application/json";
						var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
						await JsonSerializer.SerializeAsync(context.Response.Body, new
						{
							advertisedAddress = advertisedAddress,
							advertisedPort = existing.Value.JoinPort,
							joinCode = existing.Key,
							hasPassword = existingLobby.HasPassword
						}, jsonOptions);
						return;
					}
				}

				// If no fresh one to reuse, tear down any old ones for this host (stale)
				var staleRelays = ServerState.ActiveRelays
					.Where(kv => !string.IsNullOrEmpty(kv.Value.HostToken)
						&& kv.Value.HostToken.Equals(request?.HostToken))
					.Select(kv => kv.Key).ToList();
				foreach (var staleKey in staleRelays)
				{
					Console.WriteLine($"[PROXY] Tearing down stale relay {staleKey} for host {hostIp}");
					if (ServerState.ActiveRelays.TryRemove(staleKey, out var oldRelay))
						oldRelay.Stop();
					ServerState.ActiveLobbies.TryRemove(staleKey, out _);
				}

				var relay = new RelayLobby(0, hostEp, hostIp, request?.HostToken);
				var boundPort = ((System.Net.IPEndPoint)relay.MainSocket.Client.LocalEndPoint!).Port;
				var joinCode = boundPort.ToString();
				_ = relay.Start();
				ServerState.ActiveRelays[joinCode] = relay;

				var lobby = new LobbyRecord
				{
					HostToken = request?.HostToken ?? string.Empty,
					JoinCode = joinCode,
					LobbyName = !string.IsNullOrEmpty(request?.LobbyName) ? request.LobbyName : "P2P Server",
					HostName = request?.HostName ?? "Unknown",
					AdvertisedAddress = advertisedAddress,
					AdvertisedPort = boundPort,
					IsOfficial = request?.ServerApiKey == ServerState.ServerApiKey,
					HasPassword = !string.IsNullOrEmpty(request?.PasswordHash),
					MaxPlayers = request?.MaxPlayers ?? 16,
					UseBots = request?.UseBots ?? false,
					MapMode = request?.MapMode ?? "Deathmatch"
				};
				ServerState.ActiveLobbies[joinCode] = lobby;

				context.Response.ContentType = "application/json";
				var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
				await JsonSerializer.SerializeAsync(context.Response.Body, new
				{
					advertisedAddress = advertisedAddress,
					advertisedPort = boundPort,
					joinCode = joinCode,
					hasPassword = lobby.HasPassword
				}, options);
			});

			app.MapPost("/api/v1/host/heartbeat", async context =>
			{
				using var reader = new StreamReader(context.Request.Body);
				var body = await reader.ReadToEndAsync();
				var request = JsonSerializer.Deserialize<HeartbeatRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

				if (request != null && ServerState.ActiveLobbies.TryGetValue(request.JoinCode, out var lobby))
				{
					lobby.CurrentMap = request.CurrentMap;
					lobby.CurrentPlayers = request.CurrentPlayers;
					if (lobby.LobbyName == "default" || lobby.LobbyName == "P2P Server" || string.IsNullOrEmpty(lobby.LobbyName))
					{
						lobby.LobbyName = request.CurrentMap;
					}
					lobby.LastHeartbeat = DateTime.UtcNow;
				}
				if (request != null && ServerState.ActiveIceLobbies.TryGetValue(request.JoinCode, out var iceLobby))
				{
					iceLobby.CurrentMap = request.CurrentMap;
					iceLobby.CurrentPlayers = request.CurrentPlayers;
					if (iceLobby.LobbyName == "default" || iceLobby.LobbyName == "P2P Server" || string.IsNullOrEmpty(iceLobby.LobbyName))
					{
						iceLobby.LobbyName = request.CurrentMap;
					}
					iceLobby.LastHeartbeat = DateTime.UtcNow;
				}
				context.Response.StatusCode = 200;
			});

			app.MapPost("/api/v2/ice/host/prepare", async context =>
			{
				using var reader = new StreamReader(context.Request.Body);
				var body = await reader.ReadToEndAsync();
				var request = JsonSerializer.Deserialize<IcePrepareLobbyRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
				if (request == null || string.IsNullOrWhiteSpace(request.HostToken))
				{
					context.Response.StatusCode = 400;
					await context.Response.WriteAsync("Missing hostToken.");
					return;
				}

				PruneStaleGameState();

				var existing = ServerState.ActiveIceLobbies
					.FirstOrDefault(kv => !string.IsNullOrWhiteSpace(kv.Value.HostToken) && kv.Value.HostToken == request.HostToken);

				IceLobbyRecord lobby;
				IceSessionRecord hostSession;
				if (existing.Value != null && ServerState.ActiveIceSessions.TryGetValue(existing.Value.HostSessionId, out hostSession))
				{
					lobby = existing.Value;
					lobby.LobbyName = string.IsNullOrWhiteSpace(request.LobbyName) ? lobby.LobbyName : request.LobbyName;
					lobby.HostName = string.IsNullOrWhiteSpace(request.HostName) ? lobby.HostName : request.HostName;
					lobby.HasPassword = !string.IsNullOrWhiteSpace(request.PasswordHash);
					lobby.IsOfficial = request.ServerApiKey == ServerState.ServerApiKey;
					lobby.RequestedPort = request.RequestedPort > 0 ? request.RequestedPort : lobby.RequestedPort;
					lobby.MaxPlayers = request.MaxPlayers > 0 ? request.MaxPlayers : lobby.MaxPlayers;
					lobby.UseBots = request.UseBots;
					lobby.MapMode = string.IsNullOrWhiteSpace(request.MapMode) ? lobby.MapMode : request.MapMode;
					lobby.LastHeartbeat = DateTime.UtcNow;

					hostSession.PlayerName = lobby.HostName;
					hostSession.Ice = request.Ice ?? new IceDescriptionRecord();
					hostSession.LastUpdatedUtc = DateTime.UtcNow;
				}
				else
				{
					var staleIceLobbies = ServerState.ActiveIceLobbies
						.Where(kv => !string.IsNullOrWhiteSpace(kv.Value.HostToken) && kv.Value.HostToken == request.HostToken)
						.Select(kv => kv.Key)
						.ToList();
					foreach (var staleJoinCode in staleIceLobbies)
					{
						RemoveIceLobby(staleJoinCode);
					}

					var joinCode = GenerateJoinCode();
					hostSession = new IceSessionRecord
					{
						SessionId = Guid.NewGuid().ToString("N"),
						LobbyCode = joinCode,
						Role = "host",
						PlayerName = string.IsNullOrWhiteSpace(request.HostName) ? "Unknown" : request.HostName,
						Ice = request.Ice ?? new IceDescriptionRecord(),
						LastUpdatedUtc = DateTime.UtcNow
					};
					lobby = new IceLobbyRecord
					{
						JoinCode = joinCode,
						HostToken = request.HostToken,
						LobbyName = string.IsNullOrWhiteSpace(request.LobbyName) ? "P2P Server" : request.LobbyName,
						HostName = hostSession.PlayerName,
						HasPassword = !string.IsNullOrWhiteSpace(request.PasswordHash),
						IsOfficial = request.ServerApiKey == ServerState.ServerApiKey,
						RequestedPort = request.RequestedPort > 0 ? request.RequestedPort : 64087,
						MaxPlayers = request.MaxPlayers > 0 ? request.MaxPlayers : 16,
						UseBots = request.UseBots,
						MapMode = string.IsNullOrWhiteSpace(request.MapMode) ? "Deathmatch" : request.MapMode,
						HostSessionId = hostSession.SessionId,
						LastHeartbeat = DateTime.UtcNow
					};
					ServerState.ActiveIceSessions[hostSession.SessionId] = hostSession;
					ServerState.ActiveIceLobbies[joinCode] = lobby;
				}

				await context.Response.WriteAsJsonAsync(new
				{
					joinCode = lobby.JoinCode,
					hostSessionId = lobby.HostSessionId,
					hasPassword = lobby.HasPassword,
					stunServers = GetConfiguredIceServers(app.Configuration, "Ice:StunServers"),
					turnServers = GetConfiguredIceServers(app.Configuration, "Ice:TurnServers"),
					ice = hostSession.Ice
				});
			});

			app.MapGet("/api/v2/ice/lobbies/{code}", async context =>
			{
				PruneStaleGameState();

				var code = context.Request.RouteValues["code"]?.ToString()?.Trim().ToUpperInvariant();
				if (string.IsNullOrWhiteSpace(code) || !ServerState.ActiveIceLobbies.TryGetValue(code, out var lobby) || !ServerState.ActiveIceSessions.TryGetValue(lobby.HostSessionId, out var hostSession))
				{
					context.Response.StatusCode = 404;
					return;
				}

				await context.Response.WriteAsJsonAsync(new
				{
					joinCode = lobby.JoinCode,
					lobbyName = lobby.LobbyName,
					hostName = lobby.HostName,
					hasPassword = lobby.HasPassword,
					currentMap = lobby.CurrentMap,
					mapMode = lobby.MapMode,
					currentPlayers = lobby.CurrentPlayers,
					maxPlayers = lobby.MaxPlayers,
					hostSessionId = lobby.HostSessionId,
					ice = hostSession.Ice,
					stunServers = GetConfiguredIceServers(app.Configuration, "Ice:StunServers"),
					turnServers = GetConfiguredIceServers(app.Configuration, "Ice:TurnServers")
				});
			});

			app.MapPost("/api/v2/ice/lobbies/{code}/join", async context =>
			{
				using var reader = new StreamReader(context.Request.Body);
				var body = await reader.ReadToEndAsync();
				var request = JsonSerializer.Deserialize<IceJoinLobbyRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

				var code = context.Request.RouteValues["code"]?.ToString();
				if (string.IsNullOrWhiteSpace(code) || !ServerState.ActiveIceLobbies.TryGetValue(code, out var lobby) || !ServerState.ActiveIceSessions.TryGetValue(lobby.HostSessionId, out var hostSession))
				{
					context.Response.StatusCode = 404;
					return;
				}

				var joinSession = new IceSessionRecord
				{
					SessionId = Guid.NewGuid().ToString("N"),
					LobbyCode = lobby.JoinCode,
					Role = "joiner",
					PlayerName = string.IsNullOrWhiteSpace(request?.PlayerName) ? "Joiner" : request.PlayerName,
					RemoteSessionId = hostSession.SessionId,
					Ice = request?.Ice ?? new IceDescriptionRecord(),
					LastUpdatedUtc = DateTime.UtcNow
				};
				hostSession.RemoteSessionId = joinSession.SessionId;
				hostSession.LastUpdatedUtc = DateTime.UtcNow;
				lobby.LastHeartbeat = DateTime.UtcNow;
				ServerState.ActiveIceSessions[joinSession.SessionId] = joinSession;

				ForwardIceEvent(hostSession.SessionId, new IceSessionEvent
				{
					Type = "join",
					SessionId = hostSession.SessionId,
					RemoteSessionId = joinSession.SessionId,
					Ice = joinSession.Ice,
					Candidates = joinSession.Ice.Candidates.ToList()
				});

				await context.Response.WriteAsJsonAsync(new
				{
					joinCode = lobby.JoinCode,
					sessionId = joinSession.SessionId,
					hostSessionId = hostSession.SessionId,
					lobbyName = lobby.LobbyName,
					hostName = lobby.HostName,
					hasPassword = lobby.HasPassword,
					currentMap = lobby.CurrentMap,
					mapMode = lobby.MapMode,
					currentPlayers = lobby.CurrentPlayers,
					maxPlayers = lobby.MaxPlayers,
					ice = hostSession.Ice,
					stunServers = GetConfiguredIceServers(app.Configuration, "Ice:StunServers"),
					turnServers = GetConfiguredIceServers(app.Configuration, "Ice:TurnServers")
				});
			});

			app.MapPost("/api/v2/ice/sessions/{id}/candidates", async context =>
			{
				using var reader = new StreamReader(context.Request.Body);
				var body = await reader.ReadToEndAsync();
				var request = JsonSerializer.Deserialize<IceCandidateSubmitRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

				var sessionId = context.Request.RouteValues["id"]?.ToString();
				if (string.IsNullOrWhiteSpace(sessionId) || !ServerState.ActiveIceSessions.TryGetValue(sessionId, out var session))
				{
					context.Response.StatusCode = 404;
					return;
				}

				var candidates = request?.Candidates ?? new List<IceCandidateRecord>();
				if (candidates.Count > 0)
				{
					session.Ice.Candidates.AddRange(candidates);
					session.LastUpdatedUtc = DateTime.UtcNow;
					if (!string.IsNullOrWhiteSpace(session.RemoteSessionId))
					{
						ForwardIceEvent(session.RemoteSessionId, new IceSessionEvent
						{
							Type = "candidates",
							SessionId = session.SessionId,
							RemoteSessionId = session.RemoteSessionId,
							Candidates = candidates
						});
					}
				}

				context.Response.StatusCode = 200;
			});

			app.MapGet("/api/v2/ice/sessions/{id}/events", async context =>
			{
				var sessionId = context.Request.RouteValues["id"]?.ToString();
				if (string.IsNullOrWhiteSpace(sessionId) || !ServerState.ActiveIceSessions.TryGetValue(sessionId, out var session))
				{
					context.Response.StatusCode = 404;
					return;
				}

				int after = 0;
				if (int.TryParse(context.Request.Query["after"], out var parsedAfter))
				{
					after = Math.Max(0, parsedAfter);
				}

				int timeoutMs = 10000;
				if (int.TryParse(context.Request.Query["timeoutMs"], out var parsedTimeout))
				{
					timeoutMs = Math.Clamp(parsedTimeout, 0, 15000);
				}

				var startedUtc = DateTime.UtcNow;
				List<IceSessionEvent> events;
				do
				{
					events = session.GetEventsSince(after);
					if (events.Count > 0 || timeoutMs == 0)
					{
						break;
					}

					await Task.Delay(100);
				}
				while ((DateTime.UtcNow - startedUtc).TotalMilliseconds < timeoutMs);

				session.LastUpdatedUtc = DateTime.UtcNow;
				await context.Response.WriteAsJsonAsync(new
				{
					sessionId = session.SessionId,
					events
				});
			});

			app.MapPost("/api/v2/ice/sessions/{id}/connected", async context =>
			{
				using var reader = new StreamReader(context.Request.Body);
				var body = await reader.ReadToEndAsync();
				var request = JsonSerializer.Deserialize<IceConnectedRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

				var sessionId = context.Request.RouteValues["id"]?.ToString();
				if (string.IsNullOrWhiteSpace(sessionId) || !ServerState.ActiveIceSessions.TryGetValue(sessionId, out var session))
				{
					context.Response.StatusCode = 404;
					return;
				}

				session.IsConnected = true;
				session.SelectedRemoteAddress = request?.SelectedRemoteAddress ?? string.Empty;
				session.SelectedRemotePort = request?.SelectedRemotePort ?? 0;
				session.LastUpdatedUtc = DateTime.UtcNow;

				if (!string.IsNullOrWhiteSpace(session.RemoteSessionId))
				{
					ForwardIceEvent(session.RemoteSessionId, new IceSessionEvent
					{
						Type = "connected",
						SessionId = session.SessionId,
						RemoteSessionId = session.RemoteSessionId,
						SelectedRemoteAddress = session.SelectedRemoteAddress,
						SelectedRemotePort = session.SelectedRemotePort
					});
				}

				context.Response.StatusCode = 200;
			});

			app.MapPost("/api/v2/ice/host/close", async context =>
			{
				using var reader = new StreamReader(context.Request.Body);
				var body = await reader.ReadToEndAsync();
				var doc = JsonDocument.Parse(body);
				if (doc.RootElement.TryGetProperty("joinCode", out var codeProp))
				{
					var code = codeProp.GetString();
					if (!string.IsNullOrWhiteSpace(code))
					{
						RemoveIceLobby(code);
					}
				}

				context.Response.StatusCode = 200;
			});

			app.MapPost("/api/v1/game/validate_token", async context =>
			{
				using var reader = new StreamReader(context.Request.Body);
				var body = await reader.ReadToEndAsync();
				var request = JsonSerializer.Deserialize<TokenValidationRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

				if (request != null && ServerState.JoinTokens.TryRemove(request.Token, out var username))
				{
					using var scope = app.Services.CreateScope();
					var db = scope.ServiceProvider.GetRequiredService<MasterDbContext>();
					var profile = await db.PlayerProfiles.FirstOrDefaultAsync(p => p.Username == username);

					var perksJson = profile.LoadoutId == "default" ? "[]" : profile.MultiplayerPerks;
					var perks = JsonSerializer.Deserialize<List<string>>(perksJson ?? "[]") ?? new List<string>();

					context.Response.ContentType = "application/json";
					var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
					await JsonSerializer.SerializeAsync(context.Response.Body, new
					{
						success = "true",
						username = profile!.Username,
						dynamicLoadout = new
						{
							id = profile.Username + "_loadout",
							primaryWeaponId = profile.LoadoutId,
							secondaryWeaponId = "",
							gadgetId = profile.MultiplayerGadget ?? "",
							perkIds = perks
						}
					}, options);
				}
				else
				{
					context.Response.ContentType = "application/json";
					var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
					await JsonSerializer.SerializeAsync(context.Response.Body, new { success = "false" }, options);
				}
			});

			app.MapPost("/api/v1/game/report_stats", async context =>
			{
				if (!context.Request.Headers.TryGetValue("X-Server-Api-Key", out var key) || key != ServerState.ServerApiKey)
				{
					context.Response.StatusCode = 403;
					return;
				}

				using var reader = new StreamReader(context.Request.Body);
				var request = JsonSerializer.Deserialize<StatReportRequest>(await reader.ReadToEndAsync(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

				if (request != null)
				{
					using var scope = app.Services.CreateScope();
					var db = scope.ServiceProvider.GetRequiredService<MasterDbContext>();
					var profile = await db.PlayerProfiles.FirstOrDefaultAsync(p => p.Username == request.Username);
					if (profile != null)
					{
						profile.TotalFrags += request.FragsAdded;
						profile.TimePlayedMinutes += request.MinutesPlayed;
						profile.MultiplayerGems += request.GemsAdded;
						await db.SaveChangesAsync();
					}
				}
				context.Response.StatusCode = 200;
			});

			app.MapPost("/api/v1/game/sync_singleplayer", async context =>
			{
				using var reader = new StreamReader(context.Request.Body);
				var request = JsonSerializer.Deserialize<SingleplayerSyncRequest>(await reader.ReadToEndAsync(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

				if (request != null && ServerState.JoinTokens.TryGetValue(request.Token, out var username))
				{
					using var scope = app.Services.CreateScope();
					var db = scope.ServiceProvider.GetRequiredService<MasterDbContext>();
					var profile = await db.PlayerProfiles.FirstOrDefaultAsync(p => p.Username == username);

					if (profile != null)
					{
						if (request.Gems >= profile.SingleplayerGems) profile.SingleplayerGems = request.Gems;
						if (!string.IsNullOrEmpty(request.LoadoutId)) profile.SingleplayerLoadoutId = request.LoadoutId;
						await db.SaveChangesAsync();

						context.Response.ContentType = "application/json";
						var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
						await JsonSerializer.SerializeAsync(context.Response.Body, new
						{
							success = "true",
							gems = profile.SingleplayerGems,
							loadoutId = profile.SingleplayerLoadoutId
						}, options);
						return;
					}
				}
				context.Response.StatusCode = 401;
			});

			app.MapGet("/api/v1/game/weapons/{filename}", async context =>
			{
				var filename = context.Request.RouteValues["filename"]?.ToString();
				if (filename != null && filename.EndsWith(".json") && !filename.Contains("/") && !filename.Contains("\\"))
				{
					string filePath = Path.Combine(app.Environment.ContentRootPath, "GameData", "Weapons", filename);
					if (File.Exists(filePath))
					{
						context.Response.ContentType = "application/json";
						await context.Response.SendFileAsync(filePath);
						return;
					}
				}
				context.Response.StatusCode = 404;
			});
		}
	}

	// ── Weapon shop entry helper ─────────────────────────────────────────────
	public class WeaponShopEntry
	{
		public string Id { get; set; } = "";
		public string DisplayName { get; set; } = "";
		public string TriggerMode { get; set; } = "";
		public string SimType { get; set; } = "";
		public int DmgScore { get; set; }
		public int RangeScore { get; set; }
		public int SpeedScore { get; set; }
		public string Icon { get; set; } = "🗡️";
		public int GemCost { get; set; }
		public bool IsDefault { get; set; }
	}
}
