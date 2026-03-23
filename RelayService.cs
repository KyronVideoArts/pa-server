using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MasterServer.Services
{
	public class RelayLobby
	{
		public int JoinPort;
		public UdpClient MainSocket;
		public IPEndPoint? HostEndpoint;
		public ConcurrentDictionary<IPEndPoint, UdpClient> ClientSockets = new ConcurrentDictionary<IPEndPoint, UdpClient>();
		private CancellationTokenSource _cts = new CancellationTokenSource();

		public RelayLobby(int port, IPEndPoint? hostEp = null)
		{
			JoinPort = port;
			MainSocket = new UdpClient(port);
			HostEndpoint = NormalizeEndpoint(hostEp);

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

					// If host hasn't been set yet, treat the first sender as the host.
					if (HostEndpoint == null)
					{
						HostEndpoint = remoteEp;
						Console.WriteLine($"[PROXY] HostEndpoint set to {HostEndpoint}");
						continue;
					}

					// If this packet came from the host directly on MainSocket, ignore it.
					// Host responses should arrive on the per-client virtual sockets instead.
					if (IsHostEndpoint(remoteEp))
					{
						Console.WriteLine($"[PROXY] Received unexpected packet from Host on MainSocket. Dropping.");
						continue;
					}

					// --- This is a client packet. Forward it to the host via a virtual socket. ---
					if (!ClientSockets.TryGetValue(remoteEp, out var virtualSocket))
					{
						virtualSocket = new UdpClient(0);
						int vPort = ((IPEndPoint)virtualSocket.Client.LocalEndPoint!).Port;
						Console.WriteLine($"[PROXY] New client connected from {remoteEp}. Created virtual socket on port {vPort}");

						SuppressConnectionReset(virtualSocket);
						ClientSockets[remoteEp] = virtualSocket;

						// Spin up the reverse-direction forwarder (host → client)
						_ = RouteHostToClient(virtualSocket, remoteEp);
					}

					// Forward client data to host
					await virtualSocket.SendAsync(res.Buffer, res.Buffer.Length, HostEndpoint);
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
