using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MasterServer.Services
{
	public class VirtualSocketPoolEntry
	{
		public UdpClient Socket;
		public int Port;
		public IPEndPoint? DiscoveredHostEndpoint;
		public IPEndPoint? InUseByClient;

		public VirtualSocketPoolEntry(UdpClient socket, int port)
		{
			Socket = socket;
			Port = port;
		}
	}

	public class RelayLobby
	{
		public string? HostToken;
		public int JoinPort;
		public UdpClient MainSocket;
		public IPEndPoint? HostEndpoint;
		public IPAddress? ExpectedHostIp;
		public ConcurrentDictionary<IPEndPoint, VirtualSocketPoolEntry> ClientSockets = new ConcurrentDictionary<IPEndPoint, VirtualSocketPoolEntry>();
		private List<VirtualSocketPoolEntry> _pool = new List<VirtualSocketPoolEntry>();
		private CancellationTokenSource _cts = new CancellationTokenSource();
		private int _poolIndex = 0;

		public RelayLobby(int port, IPEndPoint? hostEp = null, IPAddress? expectedHostIp = null, string? hostToken = null)
		{
			MainSocket = new UdpClient(port);
			JoinPort = ((IPEndPoint)MainSocket.Client.LocalEndPoint!).Port;
			HostEndpoint = NormalizeEndpoint(hostEp);
			ExpectedHostIp = expectedHostIp;
			HostToken = hostToken;
			if (ExpectedHostIp != null && ExpectedHostIp.IsIPv4MappedToIPv6)
				ExpectedHostIp = ExpectedHostIp.MapToIPv4();

			// Pre-allocate 5 virtual sockets (JoinPort + 1..5) to catch Symmetric NAT tickles.
			for (int i = 1; i <= 5; i++)
			{
				try
				{
					var s = new UdpClient(JoinPort + i);
					SuppressConnectionReset(s);
					var entry = new VirtualSocketPoolEntry(s, JoinPort + i);
					_pool.Add(entry);
					// Start the permanent receive loop for this pooled socket.
					_ = VirtualSocketLoop(entry);
				}
				catch { /* Port already in use, skip this one */ }
			}

			// Suppress WSAECONNRESET on Windows to prevent ReceiveAsync from throwing
			// when a peer is unreachable.
			SuppressConnectionReset(MainSocket);

			Console.WriteLine($"[PROXY] Created RelayLobby on JoinPort {JoinPort} for Host: {HostEndpoint}. Pool size: {_pool.Count}");
		}

		private async Task VirtualSocketLoop(VirtualSocketPoolEntry entry)
		{
			while (!_cts.Token.IsCancellationRequested)
			{
				try
				{
					var res = await entry.Socket.ReceiveAsync();
					var remoteEp = NormalizeEndpoint(res.RemoteEndPoint)!;

					// If this packet is from the host IP, record/update discovery.
					if (ExpectedHostIp != null && remoteEp.Address.Equals(ExpectedHostIp))
					{
						entry.DiscoveredHostEndpoint = remoteEp;
						if (HostEndpoint == null) HostEndpoint = remoteEp;
						
						// If this socket is in use by a client, forward the host's data back to that client.
						var clientEp = entry.InUseByClient;
						if (clientEp != null)
						{
							await MainSocket.SendAsync(res.Buffer, res.Buffer.Length, clientEp);
						}
					}
				}
				catch (ObjectDisposedException) { break; }
				catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted) { break; }
				catch (Exception ex)
				{
					if (_cts.Token.IsCancellationRequested) break;
					Console.WriteLine($"[PROXY] VirtualSocketLoop Exception (Port {entry.Port}): {ex.Message}");
				}
			}
		}

		public void Stop()
		{
			Console.WriteLine($"[PROXY] Stopping RelayLobby on JoinPort {JoinPort}");
			_cts.Cancel();
			try { MainSocket?.Close(); } catch { }
			foreach (var entry in _pool)
			{
				try { entry.Socket.Close(); } catch { }
			}
			foreach (var entry in ClientSockets.Values)
			{
				try { entry.Socket.Close(); } catch { }
			}
			_pool.Clear();
			ClientSockets.Clear();
		}

		/// <summary>
		/// Normalize an endpoint so IPv4-mapped IPv6 addresses (::ffff:127.0.0.1) become
		/// plain IPv4, ensuring endpoint comparisons work correctly on all platforms.
		/// </summary>
		private static IPEndPoint? NormalizeEndpoint(IPEndPoint? ep)
		{
			if (ep == null) return null;
			if (ep.Address.IsIPv4MappedToIPv6)
				return new IPEndPoint(ep.Address.MapToIPv4(), ep.Port);
			return ep;
		}

		private bool IsHostEndpoint(IPEndPoint ep)
		{
			if (HostEndpoint == null) return false;
			var norm = NormalizeEndpoint(ep)!;
			return norm.Address.Equals(HostEndpoint.Address) && norm.Port == HostEndpoint.Port;
		}

		private static void SuppressConnectionReset(UdpClient udp)
		{
			const int SIO_UDP_CONNRESET = -1744830452;
			try { udp.Client.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null); } catch { }
		}

		/// <summary>
		/// Main receive loop on the public-facing relay socket.
		/// Clients send packets here; we forward them to the host via per-client virtual sockets.
		/// </summary>
		public async Task Start()
		{
			Console.WriteLine($"[PROXY] Start() loop running on port {((IPEndPoint)MainSocket.Client.LocalEndPoint!).Port}");
			while (!_cts.Token.IsCancellationRequested)
			{
				try
				{
					var res = await MainSocket.ReceiveAsync();
					var remoteEp = NormalizeEndpoint(res.RemoteEndPoint)!;

					// If this packet came from the host IP, check if it's a registration packet.
					if (ExpectedHostIp != null && remoteEp.Address.Equals(ExpectedHostIp))
					{
						string? msg = null;
						try { msg = Encoding.ASCII.GetString(res.Buffer); } catch { }

						if (msg != null && msg.StartsWith("REGISTER_HOST"))
						{
							if (HostEndpoint == null || HostEndpoint.Port != remoteEp.Port)
							{
								HostEndpoint = remoteEp;
								Console.WriteLine($"[PROXY] HostEndpoint updated/set to {HostEndpoint} (Tickle)");
							}
							continue;
						}
						
						// If it's exactly from the host's actual endpoint, ignore it (we already handled the tickle).
						// Treating the host as its own client will cause loops and confusion.
						if (IsHostEndpoint(remoteEp))
						{
							continue;
						}
						
						// If it's from the same IP but a DIFFERENT port, it's likely a client on the same machine/LAN.
						// Fall through to treat it as a client.
					}

					// If host hasn't been set yet, check if this is the first sender and we have no expected IP.
					if (HostEndpoint == null && ExpectedHostIp == null)
					{
						HostEndpoint = remoteEp;
						Console.WriteLine($"[PROXY] HostEndpoint set to {HostEndpoint} (first sender)");
						continue;
					}

					// --- This is a client packet. Forward it to the host via a virtual socket. ---
					if (!ClientSockets.TryGetValue(remoteEp, out var poolEntry))
					{
						poolEntry = AssignPoolEntry(remoteEp);
						ClientSockets[remoteEp] = poolEntry;
						// No need to spin up RouteHostToClient anymore, 
						// as VirtualSocketLoop handles the reverse direction.
					}

					// Forward client data to host
					// Prefer the discovered per-port endpoint (Symmetric NAT support)
					var target = poolEntry.DiscoveredHostEndpoint ?? HostEndpoint;
					if (target != null)
					{
						await poolEntry.Socket.SendAsync(res.Buffer, res.Buffer.Length, target);
					}
					else
					{
						Console.WriteLine($"[PROXY] Warning: Dropping client packet from {remoteEp} because no HostEndpoint (main or discovered) is set.");
					}
				}
				catch (ObjectDisposedException) { break; }
				catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted) { break; }
				catch (Exception ex)
				{
					if (_cts.Token.IsCancellationRequested) break;
					Console.WriteLine($"[PROXY] MainSocket Receive Exception: {ex.Message}");
				}
			}
			Console.WriteLine($"[PROXY] Start() loop ended for JoinPort {JoinPort}");
		}

		private VirtualSocketPoolEntry AssignPoolEntry(IPEndPoint clientEndpoint)
		{
			// Try to find a pre-allocated entry that's not in use.
			foreach (var entry in _pool)
			{
				if (Interlocked.CompareExchange(ref entry.InUseByClient, clientEndpoint, null) == null)
				{
					Console.WriteLine($"[PROXY] Client {clientEndpoint} assigned pooled virtual socket on port {entry.Port}");
					return entry;
				}
			}

			// Fallback: Create a new random virtual socket
			var fallbackSocket = new UdpClient(0);
			SuppressConnectionReset(fallbackSocket);
			var fallbackEntry = new VirtualSocketPoolEntry(fallbackSocket, ((IPEndPoint)fallbackSocket.Client.LocalEndPoint!).Port);
			fallbackEntry.InUseByClient = clientEndpoint;
			Console.WriteLine($"[PROXY] Pool exhausted. Client {clientEndpoint} assigned fallback virtual socket on port {fallbackEntry.Port}");
			_ = FallbackRouteHostToClient(fallbackEntry, clientEndpoint);
			return fallbackEntry;
		}

		/// <summary>
		/// Fallback receive loop for non-pooled (overflow) virtual sockets.
		/// </summary>
		private async Task FallbackRouteHostToClient(VirtualSocketPoolEntry entry, IPEndPoint clientEndpoint)
		{
			while (!_cts.Token.IsCancellationRequested)
			{
				try
				{
					var res = await entry.Socket.ReceiveAsync();
					var remoteEp = NormalizeEndpoint(res.RemoteEndPoint)!;
					if (ExpectedHostIp != null && remoteEp.Address.Equals(ExpectedHostIp))
					{
						entry.DiscoveredHostEndpoint = remoteEp;
						if (HostEndpoint == null) HostEndpoint = remoteEp;
						await MainSocket.SendAsync(res.Buffer, res.Buffer.Length, clientEndpoint);
					}
				}
				catch { break; }
			}
		}
	}
}
