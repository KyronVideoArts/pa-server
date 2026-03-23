using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MasterServer.Services
{
	public class RelayLobby
	{
		public string? HostToken;
		public int JoinPort;
		public UdpClient MainSocket;
		public IPEndPoint? HostEndpoint;
		public IPAddress? ExpectedHostIp;
		public ConcurrentDictionary<IPEndPoint, UdpClient> ClientSockets = new ConcurrentDictionary<IPEndPoint, UdpClient>();
		private CancellationTokenSource _cts = new CancellationTokenSource();
		private int _virtualPortCounter = 0;

		public RelayLobby(int port, IPEndPoint? hostEp = null, IPAddress? expectedHostIp = null, string? hostToken = null)
		{
			MainSocket = new UdpClient(port);
			JoinPort = ((IPEndPoint)MainSocket.Client.LocalEndPoint!).Port;
			HostEndpoint = NormalizeEndpoint(hostEp);
			ExpectedHostIp = expectedHostIp;
			HostToken = hostToken;
			if (ExpectedHostIp != null && ExpectedHostIp.IsIPv4MappedToIPv6)
				ExpectedHostIp = ExpectedHostIp.MapToIPv4();

			// Suppress WSAECONNRESET on Windows to prevent ReceiveAsync from throwing
			// when a peer is unreachable.
			SuppressConnectionReset(MainSocket);

			Console.WriteLine($"[PROXY] Created RelayLobby on JoinPort {((IPEndPoint)MainSocket.Client.LocalEndPoint!).Port} for Host: {HostEndpoint}");
		}

		public void Stop()
		{
			Console.WriteLine($"[PROXY] Stopping RelayLobby on JoinPort {JoinPort}");
			_cts.Cancel();
			try { MainSocket?.Close(); } catch { }
			foreach (var client in ClientSockets.Values)
			{
				try { client.Close(); } catch { }
			}
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
					if (!ClientSockets.TryGetValue(remoteEp, out var virtualSocket))
					{
						// Try to use a port the host might have tickled (JoinPort + 1-5)
						// This helps with Port-Restricted NAT.
						virtualSocket = CreateVirtualSocket();
						
						int vPort = ((IPEndPoint)virtualSocket.Client.LocalEndPoint!).Port;
						Console.WriteLine($"[PROXY] New client connected from {remoteEp}. Created virtual socket on port {vPort}");

						ClientSockets[remoteEp] = virtualSocket;

						// Spin up the reverse-direction forwarder (host → client)
						_ = RouteHostToClient(virtualSocket, remoteEp);
					}

					// Forward client data to host
					if (HostEndpoint != null)
					{
						await virtualSocket.SendAsync(res.Buffer, res.Buffer.Length, HostEndpoint);
					}
					else
					{
						Console.WriteLine($"[PROXY] Warning: Dropping client packet from {remoteEp} because HostEndpoint is not set yet.");
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

		private UdpClient CreateVirtualSocket()
		{
			// Try to bind to ports the host might have tickled (JoinPort + 1-5)
			for (int i = 0; i < 5; i++)
			{
				int offset = (Interlocked.Increment(ref _virtualPortCounter) % 5) + 1;
				int port = JoinPort + offset;
				try
				{
					var s = new UdpClient(port);
					SuppressConnectionReset(s);
					return s;
				}
				catch { /* Port already in use, try next */ }
			}

			// Fallback to random port
			var fallback = new UdpClient(0);
			SuppressConnectionReset(fallback);
			return fallback;
		}

		/// <summary>
		/// Reverse-direction forwarder: receives packets from the host on the virtual socket
		/// and sends them back to the client through the MainSocket (so the client sees
		/// responses originating from the relay's public port).
		/// </summary>
		private async Task RouteHostToClient(UdpClient virtualSocket, IPEndPoint clientEndpoint)
		{
			int vPort = ((IPEndPoint)virtualSocket.Client.LocalEndPoint!).Port;
			Console.WriteLine($"[PROXY] RouteHostToClient started for virtual port {vPort} -> client {clientEndpoint}");

			while (!_cts.Token.IsCancellationRequested)
			{
				try
				{
					var res = await virtualSocket.ReceiveAsync();
					// Forward host's response back to the original client through MainSocket
					await MainSocket.SendAsync(res.Buffer, res.Buffer.Length, clientEndpoint);
				}
				catch (ObjectDisposedException) { break; }
				catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted) { break; }
				catch (Exception ex)
				{
					if (_cts.Token.IsCancellationRequested) break;
					Console.WriteLine($"[PROXY] RouteHostToClient Exception (vPort {vPort}): {ex.Message}");
					break;
				}
			}

			Console.WriteLine($"[PROXY] RouteHostToClient ended for virtual port {vPort} -> client {clientEndpoint}");
		}
	}
}
