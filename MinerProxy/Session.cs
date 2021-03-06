﻿using System;
using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;
using System.Threading;

namespace MinerProxy
{
    internal sealed class Session : IDisposable
    {
        private readonly Socket m_socket;
        private readonly byte[] m_buffer;

        private bool m_disposed;

        public bool Disposed
        {
            get
            {
                return m_disposed;
            }
        }

        public Action<byte[],int> OnDataReceived { get; set; }
        public Action OnDisconnected { get; set; }

        public Session(Socket socket)
        {
            m_socket = socket;
            m_buffer = BufferPool.Get();
            m_disposed = false;
        }

        public void Receive()
        {
            if (m_disposed) { return; }

            SocketError outError = SocketError.Success;

            m_socket.BeginReceive(m_buffer, 0, m_buffer.Length, SocketFlags.None, out outError, EndReceive, null);

            if(outError != SocketError.Success)
                Dispose();
        }

        private void EndReceive(IAsyncResult iar)
        {
            if (m_disposed) { return; }

            SocketError outError;

            int size = m_socket.EndReceive(iar, out outError);

            if (size == 0 || outError != SocketError.Success)
            {
                Dispose();
                return;
            }

            //Split the buffer on \n, and send the data forward one line at a time.
            //If it's proper JSON, this shouldn't pose any issues. However, it can no longer be used as a generic TCP Proxy :p
            //If you have an ideas to increase the performance of this, please submit a PR! I'm sure there is a slight performance impact converting from and back to bytes.
            if (OnDataReceived != null)
            {
                var lines = Encoding.UTF8.GetString(m_buffer, 0, size).Split('\n'); 
                for (int index = 0; index < lines.Length; index++)
                {
                    if (lines[index].Length > 0)
                        OnDataReceived(Encoding.UTF8.GetBytes(lines[index] + '\n'), lines[index].Length + 1);   //Claymore expects a newline at the end, we need to add it back in
                }
            }

            Receive();
            
        }

        public void Send(byte[] buffer,int length)
        {
            if (m_disposed) { return; }

            int offset = 0;

            while (offset < length)
            {
                SocketError outError = SocketError.Success;

                int size = m_socket.Send(buffer, offset, length - offset, SocketFlags.None, out outError);

                if (size == 0 || outError != SocketError.Success)
                {
                    Dispose();
                    return;
                }

                offset += size;
            }
        }

        public void Dispose()
        {
            if (!m_disposed)
            {
                m_disposed = true;

                m_socket.Shutdown(SocketShutdown.Both);
                m_socket.Close();

                BufferPool.Put(m_buffer);

                if (OnDisconnected != null)
                    OnDisconnected();

                OnDataReceived = null;
                OnDisconnected = null;
            }
        }
    }
}
