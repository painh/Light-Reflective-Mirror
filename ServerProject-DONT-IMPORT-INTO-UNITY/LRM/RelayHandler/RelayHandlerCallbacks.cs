using LightReflectiveMirror.Endpoints;
using System;

namespace LightReflectiveMirror
{
    public partial class RelayHandler
    {
        /// <summary>
        /// Invoked when a client connects to this LRM server.
        /// </summary>
        /// <param name="clientId">The ID of the client who connected.</param>
        public void ClientConnected(int clientId)
        {
            _pendingAuthentication.Add(clientId);
            Program.WriteLogMessage($"[클라이언트:{clientId}] 연결됨, 인증 대기중...", ConsoleColor.Cyan);
            var buffer = _sendBuffers.Rent(1);
            int pos = 0;
            buffer.WriteByte(ref pos, (byte)OpCodes.AuthenticationRequest);
            Program.transport.ServerSend(clientId, 0, new ArraySegment<byte>(buffer, 0, pos));
            _sendBuffers.Return(buffer);
        }

        /// <summary>
        /// Handles the processing of data from a client.
        /// </summary>
        /// <param name="clientId">The client who sent the data</param>
        /// <param name="segmentData">The binary data</param>
        /// <param name="channel">The channel the client sent the data on</param>
        public void HandleMessage(int clientId, ArraySegment<byte> segmentData, int channel)
        {
            try
            {
                var data = segmentData.Array;
                int pos = segmentData.Offset;

                OpCodes opcode = (OpCodes)data.ReadByte(ref pos);

                if (_pendingAuthentication.Contains(clientId))
                {
                    if (opcode == OpCodes.AuthenticationResponse)
                    {
                        string authResponse = data.ReadString(ref pos);
                        if (authResponse == Program.conf.AuthenticationKey)
                        {
                            _pendingAuthentication.Remove(clientId);
                            Program.WriteLogMessage($"[클라이언트:{clientId}] 인증 성공", ConsoleColor.Green);
                            int writePos = 0;
                            var sendBuffer = _sendBuffers.Rent(1);
                            sendBuffer.WriteByte(ref writePos, (byte)OpCodes.Authenticated);
                            Program.transport.ServerSend(clientId, 0, new ArraySegment<byte>(sendBuffer, 0, writePos));

                            _sendBuffers.Return(sendBuffer);
                        }
                        else
                        {
                            Program.WriteLogMessage($"[클라이언트:{clientId}] 인증 실패 - 잘못된 인증키", ConsoleColor.Red);
                            Program.transport.ServerDisconnect(clientId);
                        }
                    }
                    return;
                }

                switch (opcode)
                {
                    case OpCodes.CreateRoom: // bruh
                        CreateRoom(clientId, data.ReadInt   (ref pos), 
                                             data.ReadString(ref pos), 
                                             data.ReadBool  (ref pos), 
                                             data.ReadString(ref pos), 
                                             data.ReadBool  (ref pos), 
                                             data.ReadString(ref pos), 
                                             data.ReadBool  (ref pos), 
                                             data.ReadInt   (ref pos));
                        break;
                    case OpCodes.RequestID:
                        SendClientID(clientId);
                        break;
                    case OpCodes.LeaveRoom:
                        LeaveRoom(clientId);
                        break;
                    case OpCodes.JoinServer:
                        JoinRoom(clientId, data.ReadString(ref pos), data.ReadBool(ref pos), data.ReadString(ref pos));
                        break;
                    case OpCodes.KickPlayer:
                        LeaveRoom(data.ReadInt(ref pos), clientId);
                        break;
                    case OpCodes.SendData:
                        ProcessData(clientId, data.ReadBytes(ref pos), channel, data.ReadInt(ref pos));
                        break;
                    case OpCodes.UpdateRoomData:
                        var plyRoom = _cachedClientRooms[clientId];

                        if (plyRoom == null || plyRoom.hostId != clientId)
                            return;

                        if (data.ReadBool(ref pos))
                            plyRoom.serverName = data.ReadString(ref pos);

                        if (data.ReadBool(ref pos))
                            plyRoom.serverData = data.ReadString(ref pos);

                        if (data.ReadBool(ref pos))
                            plyRoom.isPublic = data.ReadBool(ref pos);

                        if (data.ReadBool(ref pos))
                            plyRoom.maxPlayers = data.ReadInt(ref pos);

                        Endpoint.RoomsModified();
                        break;
                }
            }
            catch
            {
                // sent invalid data, boot them hehe
                Program.WriteLogMessage($"[클라이언트:{clientId}] 잘못된 데이터 전송 - 연결 종료", ConsoleColor.Red);
                Program.transport.ServerDisconnect(clientId);
            }
        }

        /// <summary>
        /// Invoked when a client disconnects from the relay.
        /// </summary>
        /// <param name="clientId">The ID of the client who disconnected</param>
        public void HandleDisconnect(int clientId) => LeaveRoom(clientId);
    }
}
