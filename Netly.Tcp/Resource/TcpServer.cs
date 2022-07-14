﻿using Netly.Core;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Netly.Tcp
{
    public class TcpServer : ITcpServer
    {
        #region Var

        #region Public

        public Host Host { get; private set; }
        public bool IsEncrypted { get; private set; }
        public bool Opened { get => IsOpened(); }
        public List<TcpClient> Clients { get; private set; }

        #endregion

        #region Private

        private Socket _socket;

        private bool _tryOpen;
        private bool _tryClose;
        private bool _invokeClose;
        private bool _opened;

        #region Events

        private EventHandler _OnOpen;
        private EventHandler _OnClose;
        private EventHandler<Exception> _OnError;
        private EventHandler<TcpClient> _OnEnter;
        private EventHandler<TcpClient> _OnExit;
        private EventHandler<(TcpClient client, byte[] data)> _OnData;
        private EventHandler<(TcpClient client, string name, byte[] data)> _OnEvent;

        private EventHandler<Socket> _OnBeforeOpen;
        private EventHandler<Socket> _OnAfterOpen;

        #endregion

        #endregion

        #endregion

        #region Builder
        
        public TcpServer()
        {
            Host = new Host(IPAddress.Any, 0);
            Clients = new List<TcpClient>();
            _socket = new Socket(Host.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        }

        #endregion

        #region Init

        public void Open(Host host, int backlog = 0)
        {
            if (Opened || _tryOpen || _tryClose) return;

            _tryOpen = true;

            Async.SafePool(() =>
            {
                try
                {
                    _socket = new Socket(host.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

                    _OnBeforeOpen?.Invoke(this, _socket);

                    _socket.Bind(host.EndPoint);
                    _socket.Listen(backlog);

                    Host = host;

                    _opened = true;
                    _invokeClose = false;

                    _OnAfterOpen?.Invoke(this, _socket);

                    _OnOpen?.Invoke(this, EventArgs.Empty);

                    BeginAccept();
                }
                catch (Exception e)
                {
                    _OnError?.Invoke(this, e);
                }

                _tryOpen = false;
            });
        }

        public void Close()
        {
            if (!Opened || _tryOpen || _tryClose) return;

            _tryClose = true;

            _socket.Shutdown(SocketShutdown.Both);

            Async.SafePool(() =>
            {
                try
                {
                    _socket.Close();
                    _socket.Dispose();
                }
                finally
                {
                    _socket = null;

                    foreach (TcpClient client in Clients.ToArray())
                    {
                        try
                        {
                            client?.Close();
                        }
                        catch { }
                    }

                    _opened = false;
                    Clients.Clear();

                    if (!_invokeClose)
                    {
                        _invokeClose = true;
                        _OnClose?.Invoke(this, EventArgs.Empty);
                    }
                }

                _tryClose = false;
            });
        }

        private bool IsOpened()
        {
            if (_socket == null) return false;

            return _opened;
        }

        private void BeginAccept()
        {
            try
            {
                _socket.BeginAccept(EndAccept, null);
            }
            catch
            {
                if (Opened)
                {
                    BeginAccept();
                }
            }
        }

        private void EndAccept(IAsyncResult result)
        {
            Socket socket;

            try
            {
                socket = _socket.EndAccept(result);
            }
            catch
            {
                BeginAccept();
                return;
            }

            TcpClient client = new TcpClient(Guid.NewGuid().ToString(), socket);
            Clients.Add(client);

            client.OnOpen(() =>
            {
                _OnEnter?.Invoke(this, client);
            });

            client.OnClose(() =>
            {
                foreach (TcpClient target in Clients.ToArray())
                {
                    if (client.Id == target.Id)
                    {
                        try
                        {
                            Clients.Remove(target);
                        }
                        catch { }

                        _OnExit?.Invoke(this, client);
                    }
                }
            });

            client.OnData((data) =>
            {
                _OnData?.Invoke(this, (client, data));
            });

            client.OnEvent((name, data) =>
            {
                _OnEvent?.Invoke(this, (client, name, data));
            });

            client.InitServer();

            BeginAccept();
        }

        public void UseEncryption(bool value)
        {
            if (Opened)
            {
                throw new Exception("Error, you can't add encryption configuration to an open socket");
            }

            throw new NotImplementedException(nameof(UseEncryption));

            // IsEncrypted = value;
        }

        #endregion

        #region Customization Event

        public void OnBeforeOpen(Action<Socket> callback)
        {
            _OnBeforeOpen += (sender, socket) => callback?.Invoke(socket);
        }

        public void OnAfterOpen(Action<Socket> callback)
        {
            _OnAfterOpen += (sender, socket) => callback?.Invoke(socket);
        }

        #endregion

        #region Events

        public void OnOpen(Action callback)
        {
            _OnOpen += (sender, args) => callback?.Invoke();
        }

        public void OnClose(Action callback)
        {
            _OnClose += (sender, args) => callback?.Invoke();
        }

        public void OnError(Action<Exception> callback)
        {
            _OnError += (sender, exception) => callback?.Invoke(exception);
        }

        public void OnEnter(Action<TcpClient> callback)
        {
            _OnEnter += (sender, client) => callback?.Invoke(client);
        }

        public void OnExit(Action<TcpClient> callback)
        {
            _OnExit += (sender, client) => callback?.Invoke(client);
        }

        public void OnData(Action<TcpClient, byte[]> callback)
        {
            _OnData += (sender, value) => callback?.Invoke(value.client, value.data);
        }

        public void OnEvent(Action<TcpClient, string, byte[]> callback)
        {
            _OnEvent += (sender, value) => callback?.Invoke(value.client, value.name, value.data);
        }

        #endregion
    }
}