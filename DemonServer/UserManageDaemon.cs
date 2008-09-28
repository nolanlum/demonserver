﻿/*
+---------------------------------------------------------------------------+
|	Demon - dAmn Emulator													|
|===========================================================================|
|	Copyright © 2008 Nol888													|
|===========================================================================|
|	This file is part of Demon.												|
|																			|
|	Demon is free software: you can redistribute it and/or modify			|
|	it under the terms of the GNU Affero General Public License as			|
|	published by the Free Software Foundation, either version 3 of the		|
|	License, or (at your option) any later version.							|
|																			|
|	This program is distributed in the hope that it will be useful,			|
|	but WITHOUT ANY WARRANTY; without even the implied warranty of			|
|	MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the			|
|	GNU Affero General Public License for more details.						|
|																			|
|	You should have received a copy of the GNU Affero General Public License|
|	along with this program.  If not, see <http://www.gnu.org/licenses/>.	|
|																			|
|===========================================================================|
|	> $Date$
|	> $Revision$
|	> $Author$
+---------------------------------------------------------------------------+
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net;
using System.Net.Sockets;

using DemonServer.Protocol;
using DemonServer.User;

namespace DemonServer
{
	public class UserManageDaemon
	{
		#region Private Properties
		private int maxConnections;

		private Dictionary<string, string> configuration;

		private Thread workerThread;

		private Socket listenSocket;
		private Dictionary<int, Net.Socket> socketList;
		private Stack<int> unusedSocketList;

		private AsyncCallback __clientConnected;
		private Net.DBConn DBConn;

		private Queue<QueueItem> authQueue;
		#endregion

		#region Public Properties
		#endregion

		#region Constructor
		public UserManageDaemon(Dictionary<string, string> config)
		{
			// Set up prelim. vars.
			this.__clientConnected = new AsyncCallback(this.clientConnected);
			this.configuration = config;

			this.maxConnections = int.Parse(this.configuration["connlimit-auc"]);

			// Set up listening sockets, etc.
			socketList = new Dictionary<int, Net.Socket>(maxConnections);

			unusedSocketList = new Stack<int>();
			for (int i = this.maxConnections; i >= 0; i--)
			{
				unusedSocketList.Push(i);
			}

			listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			listenSocket.Bind(new IPEndPoint(IPAddress.Parse(this.configuration["bind-auc-ip"]), int.Parse(this.configuration["bind-auc-port"])));

			#region Connect to the DB.
			DBConn = new Net.DBConn(config["mysql-host"], config["mysql-user"], config["mysql-pass"],
				config["mysql-database"], ((config["mysql-port"] != "") ? (int.Parse(config["mysql-port"])) : (3306)));
			lock (DBConn)
			{
				try
				{
					while (true)
					{
						if (DBConn.Connect() == false)
						{
							Console.ShowError("User manager daemon unable to connect to MySQL server!  Error: " + DBConn.MySQL_Error());
							System.Threading.Thread.Sleep(5000);
						}
						else
						{
							break;
						}
					}
				}
				catch (Exception)
				{
					Console.ShowError("User manager daemon unable to connect to MySQL server!  Error: " + DBConn.MySQL_Error());
				}
			}
			#endregion
		
			// Set up the packet queue.
			authQueue = new Queue<QueueItem>();

			// Start listening.
			listenSocket.Listen(4);
			listenSocket.BeginAccept(this.__clientConnected, null);
			Console.ShowStatus("User Management Daemon is '\x1B[37mready\x1B[0m' and listening at " + listenSocket.LocalEndPoint + ".");
		}
		#endregion

		#region Connection Functions
		void clientConnected(IAsyncResult Result)
		{
			lock (this.listenSocket)
			{
				try
				{
					int SocketID = unusedSocketList.Pop();

					// Set up the event listeners.
					socketList.Add(SocketID, null);
					socketList[SocketID] = new Net.Socket(SocketID, listenSocket.EndAccept(Result));
					socketList[SocketID].OnDataArrival += new Net.Socket.__OnDataArrival(UserManageDaemon_OnDataArrival);
					socketList[SocketID].OnDisconnect += new Net.Socket.__OnDisconnect(UserManageDaemon_OnDisconnect);
					socketList[SocketID].OnError += new Net.Socket.__OnError(UserManageDaemon_OnError);

					// Start listening for soem datars.
					socketList[SocketID].StartReceive();

					Console.ShowInfo(string.Format("New auth connection from \x1B[37m{0}\x1B[0m.", socketList[SocketID].Name));
				}
				catch (InvalidOperationException)
				{
					// _Probably_ we ran out of sockets.
					// Turn 'em down, sorry.
					Console.ShowError("Error accepting auth connection - maximum limit of " + maxConnections.ToString() + " connections reached.");

					listenSocket.EndAccept(Result).Close(1);
					return;
				}
				catch (SocketException Ex)
				{
					Console.ShowError("Auth Socket Exception: " + Ex.Message);
				}
				catch (Exception Ex)
				{
					Console.ShowError("Error accepting auth connection: " + Ex.Message);
				}
				finally
				{
					// Yea, let's stop hogging the main socket.
					listenSocket.BeginAccept(__clientConnected, null);
				}
			}
		}
		void UserManageDaemon_OnDataArrival(int SocketID, byte[] ByteArray)
		{
			string packetText = "";
			foreach (byte dataByte in ByteArray)
			{
				packetText += ((char) dataByte).ToString();
			}

			// Try to parse it into a packet...
			Packet dataPacket = (Packet) packetText;
			if (dataPacket.cmd == "")
			{
				// _Something_ happened.
				Console.ShowWarning(socketList[SocketID].InternalSocket.RemoteEndPoint + " sent a bad auth data packet.  Disconnecting.");
				socketList[SocketID].Close(10054);
				return;
			}

			// Save it for later...
			QueueItem item = new QueueItem();
			item.dataPacket = dataPacket;
			item.socketID = SocketID;

			authQueue.Enqueue(item);
		}
		void UserManageDaemon_OnDisconnect(int SocketID, SocketException Ex)
		{
			if (socketList[SocketID] == null) return;
			lock (this.socketList[SocketID])
			{
				if (Ex.ErrorCode > 0)
				{
					string ExceptionName = "";
					switch (Ex.ErrorCode)
					{
						case 10060:
							ExceptionName = "Connection timed out.";
							break;
						case 10054:
							ExceptionName = "Connection reset by peer.";
							break;
						case 10053:
							ExceptionName = "Software caused connection abort.";
							break;
						case 10052:
							ExceptionName = "Network dropped connection on reset.";
							break;
						default:
							ExceptionName = Ex.Message;
							break;
					}
					Console.ShowInfo("\x1B[37m" + socketList[SocketID].Name + "\x1B[0m disconnected: " + ExceptionName);
				}
				else
				{
					Console.ShowInfo("\x1B[37m" + socketList[SocketID].Name + "\x1B[0m disconnected: Connection closed.");
				}
				socketList[SocketID] = null;
				unusedSocketList.Push(SocketID);
			}
		}
		void UserManageDaemon_OnError(int SocketID, Exception Ex)
		{
			Console.ShowError("An error occurred\n" + Ex.StackTrace + "\n" + Ex.Message);
		}
		#endregion

		#region Queue Processor
		void queueProcessor()
		{
			while (true)
			{
				try
				{
					while (this.authQueue.Count > 0)
					{
						QueueItem item = this.authQueue.Dequeue();
						Packet response = new Packet();

						switch (item.dataPacket.cmd.ToLower())
						{
							case "create":
								string username = item.dataPacket.param;
								if (username == "")
								{
									// ...whoops.
									response.cmd = "create";
									response.param = "";
									response.args.Add("e", "failed");

									socketList[item.socketID].SendPacket(response);
									socketList[item.socketID].Close(10054);
									return;
								}
								if (!item.dataPacket.args.ContainsKey("password"))
								{
									// ...whoops.
									response.cmd = "create";
									response.param = "";
									response.args.Add("e", "failed");

									socketList[item.socketID].SendPacket(response);
									socketList[item.socketID].Close(10054);
									return;
								}
								string password = item.dataPacket.args["password"];

								string query = "";

								// XXX - TODO: Finish this!
								break;

						}
					}
				}
				catch (ThreadAbortException)
				{
					// We were called to stop...just stop.
				}
			}
		}
		#endregion
		private struct QueueItem
		{
			public Packet dataPacket;
			public int socketID;
		}
	}
}