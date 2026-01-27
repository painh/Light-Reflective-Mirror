using LightReflectiveMirror.Endpoints;
using System;
using System.Collections.Generic;
using System.Net;

namespace LightReflectiveMirror
{
    public partial class RelayHandler
    {
        /// <summary>
        /// Creates a room on the LRM node.
        /// </summary>
        /// <param name="clientId">The client requesting to create a room</param>
        /// <param name="maxPlayers">The maximum amount of players for this room</param>
        /// <param name="serverName">The name for the server</param>
        /// <param name="isPublic">Whether or not the server should show up on the server list</param>
        /// <param name="serverData">Extra data the host can include</param>
        /// <param name="useDirectConnect">Whether or not, the host is capable of doing direct connections</param>
        /// <param name="hostLocalIP">The hosts local IP</param>
        /// <param name="useNatPunch">Whether or not, the host is supporting NAT Punch</param>
        /// <param name="port">The port of the direct connect transport on the host</param>
        private void CreateRoom(int clientId, int maxPlayers, string serverName, bool isPublic, string serverData, bool useDirectConnect, string hostLocalIP, bool useNatPunch, int port)
        {
            LeaveRoom(clientId);
            Program.instance.NATConnections.TryGetValue(clientId, out IPEndPoint hostIP);

            Room room = new()
            {
                hostId = clientId,
                maxPlayers = maxPlayers,
                serverName = serverName,
                isPublic = isPublic,
                serverData = serverData,
                clients = new List<int>(),
                serverId = GetRandomServerID(),
                hostIP = hostIP,
                hostLocalIP = hostLocalIP,
                supportsDirectConnect = hostIP != null && useDirectConnect,
                port = port,
                useNATPunch = useNatPunch,
                relayInfo = new RelayAddress { address = Program.publicIP, port = Program.conf.TransportPort, endpointPort = Program.conf.EndpointPort }
            };

            rooms.Add(room);
            _cachedClientRooms.Add(clientId, room);
            _cachedRooms.Add(room.serverId, room);

            string connectType = room.supportsDirectConnect ? (useNatPunch ? "NAT펀치" : "Direct") : "릴레이전용";
            Program.WriteLogMessage($"[클라이언트:{clientId}] 방 생성 - ID:{room.serverId}, 이름:{serverName}, 연결:{connectType}, 최대:{maxPlayers}명", ConsoleColor.Yellow);

            int pos = 0;
            byte[] sendBuffer = _sendBuffers.Rent(5);

            sendBuffer.WriteByte(ref pos, (byte)OpCodes.RoomCreated);
            sendBuffer.WriteString(ref pos, room.serverId);

            Program.transport.ServerSend(clientId, 0, new ArraySegment<byte>(sendBuffer, 0, pos));
            _sendBuffers.Return(sendBuffer);

            Endpoint.RoomsModified();
        }

        /// <summary>
        /// Attempts to join a room for a client.
        /// </summary>
        /// <param name="clientId">The client requesting to join the room</param>
        /// <param name="serverId">The server ID of the room</param>
        /// <param name="canDirectConnect">If the client is capable of a direct connection</param>
        /// <param name="localIP">The local IP of the client joining</param>
        private void JoinRoom(int clientId, string serverId, bool canDirectConnect, string localIP)
        {
            LeaveRoom(clientId);

            if (_cachedRooms.ContainsKey(serverId))
            {
                var room = _cachedRooms[serverId];

                if (room.clients.Count < room.maxPlayers)
                {
                    room.clients.Add(clientId);
                    _cachedClientRooms.Add(clientId, room);

                    int sendJoinPos = 0;
                    byte[] sendJoinBuffer = _sendBuffers.Rent(500);

                    if (canDirectConnect && Program.instance.NATConnections.ContainsKey(clientId) && room.supportsDirectConnect)
                    {
                        sendJoinBuffer.WriteByte(ref sendJoinPos, (byte)OpCodes.DirectConnectIP);

                        bool sameNetwork = Program.instance.NATConnections[clientId].Address.Equals(room.hostIP.Address);
                        string connectIP;
                        if (sameNetwork)
                            connectIP = room.hostLocalIP == localIP ? "127.0.0.1" : room.hostLocalIP;
                        else
                            connectIP = room.hostIP.Address.ToString();

                        sendJoinBuffer.WriteString(ref sendJoinPos, connectIP);
                        sendJoinBuffer.WriteInt(ref sendJoinPos, room.useNATPunch ? room.hostIP.Port : room.port);
                        sendJoinBuffer.WriteBool(ref sendJoinPos, room.useNATPunch);

                        string p2pType = room.useNATPunch ? "NAT펀치" : "Direct";
                        string networkInfo = sameNetwork ? "동일네트워크" : "외부";
                        Program.WriteLogMessage($"[클라이언트:{clientId}] 방 입장 - ID:{serverId}, P2P({p2pType}, {networkInfo}) → {connectIP}", ConsoleColor.Green);

                        Program.transport.ServerSend(clientId, 0, new ArraySegment<byte>(sendJoinBuffer, 0, sendJoinPos));

                        if (room.useNATPunch)
                        {
                            sendJoinPos = 0;
                            sendJoinBuffer.WriteByte(ref sendJoinPos, (byte)OpCodes.DirectConnectIP);

                            sendJoinBuffer.WriteString(ref sendJoinPos, Program.instance.NATConnections[clientId].Address.ToString());
                            sendJoinBuffer.WriteInt(ref sendJoinPos, Program.instance.NATConnections[clientId].Port);
                            sendJoinBuffer.WriteBool(ref sendJoinPos, true);

                            Program.transport.ServerSend(room.hostId, 0, new ArraySegment<byte>(sendJoinBuffer, 0, sendJoinPos));
                        }

                        _sendBuffers.Return(sendJoinBuffer);

                        Endpoint.RoomsModified();
                        return;
                    }
                    else
                    {
                        Program.WriteLogMessage($"[클라이언트:{clientId}] 방 입장 - ID:{serverId}, 릴레이 연결", ConsoleColor.Cyan);

                        sendJoinBuffer.WriteByte(ref sendJoinPos, (byte)OpCodes.ServerJoined);
                        sendJoinBuffer.WriteInt(ref sendJoinPos, clientId);

                        Program.transport.ServerSend(clientId, 0, new ArraySegment<byte>(sendJoinBuffer, 0, sendJoinPos));
                        Program.transport.ServerSend(room.hostId, 0, new ArraySegment<byte>(sendJoinBuffer, 0, sendJoinPos));
                        _sendBuffers.Return(sendJoinBuffer);

                        Endpoint.RoomsModified();
                        return;
                    }
                }
            }

            // If it got to here, then the server was not found, or full. Tell the client.
            Program.WriteLogMessage($"[클라이언트:{clientId}] 방 입장 실패 - ID:{serverId} (방 없음 또는 정원초과)", ConsoleColor.Red);

            int pos = 0;
            byte[] sendBuffer = _sendBuffers.Rent(1);

            sendBuffer.WriteByte(ref pos, (byte)OpCodes.ServerLeft);

            Program.transport.ServerSend(clientId, 0, new ArraySegment<byte>(sendBuffer, 0, pos));
            _sendBuffers.Return(sendBuffer);
        }

        /// <summary>
        /// Makes the client leave their room.
        /// </summary>
        /// <param name="clientId">The client of which to remove from their room</param>
        /// <param name="requiredHostId">The ID of the client who kicked the client. -1 if the client left on their own terms</param>
        private void LeaveRoom(int clientId, int requiredHostId = -1)
        {
            for (int i = 0; i < rooms.Count; i++)
            {
                // if host left
                if (rooms[i].hostId == clientId)
                {
                    Program.WriteLogMessage($"[클라이언트:{clientId}] 방 종료 - ID:{rooms[i].serverId}, 접속자 {rooms[i].clients.Count}명 퇴장", ConsoleColor.Yellow);

                    int pos = 0;
                    byte[] sendBuffer = _sendBuffers.Rent(1);
                    sendBuffer.WriteByte(ref pos, (byte)OpCodes.ServerLeft);

                    for (int x = 0; x < rooms[i].clients.Count; x++)
                    {
                        Program.transport.ServerSend(rooms[i].clients[x], 0, new ArraySegment<byte>(sendBuffer, 0, pos));
                        _cachedClientRooms.Remove(rooms[i].clients[x]);
                    }

                    _sendBuffers.Return(sendBuffer);
                    rooms[i].clients.Clear();
                    _cachedRooms.Remove(rooms[i].serverId);
                    rooms.RemoveAt(i);
                    _cachedClientRooms.Remove(clientId);
                    Endpoint.RoomsModified();
                    return;
                }
                else
                {
                    // if the person that tried to kick wasnt host and it wasnt the client leaving on their own
                    if (requiredHostId != -1 && rooms[i].hostId != requiredHostId)
                        continue;

                    if (rooms[i].clients.RemoveAll(x => x == clientId) > 0)
                    {
                        string leaveReason = requiredHostId != -1 ? "강퇴" : "퇴장";
                        Program.WriteLogMessage($"[클라이언트:{clientId}] 방 {leaveReason} - ID:{rooms[i].serverId}", ConsoleColor.Cyan);

                        int pos = 0;
                        byte[] sendBuffer = _sendBuffers.Rent(5);

                        sendBuffer.WriteByte(ref pos, (byte)OpCodes.PlayerDisconnected);
                        sendBuffer.WriteInt(ref pos, clientId);

                        Program.transport.ServerSend(rooms[i].hostId, 0, new ArraySegment<byte>(sendBuffer, 0, pos));
                        _sendBuffers.Return(sendBuffer);

                        // temporary solution to kicking bug
                        // this tells the local player that got kicked that he, well, got kicked.
                        pos = 0;
                        sendBuffer = _sendBuffers.Rent(1);

                        sendBuffer.WriteByte(ref pos, (byte)OpCodes.ServerLeft);

                        Program.transport.ServerSend(clientId, 0, new ArraySegment<byte>(sendBuffer, 0, pos));
                        _sendBuffers.Return(sendBuffer);

                        //end temporary solution

                        Endpoint.RoomsModified();
                        _cachedClientRooms.Remove(clientId);
                    }
                }
            }
        }
    }
}
