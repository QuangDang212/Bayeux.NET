using Bayeux.Util;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.Web;

namespace Bayeux
{
    public class StatefulWebSocket<T> : IDisposable
    {
        private readonly Uri uri;
        private MessageWebSocket currentSocket;
        private DataWriter writer;
        private readonly Queue<T> messageQueue = new Queue<T>();

        public virtual int Interval
        {
            get { return 1000; }
        }

        public StatefulWebSocket(string url)
        {
            uri = new Uri(url, UriKind.Absolute);
            NetworkStatus.Instance.Disconnected += NetworkDisconnected;
            Closed += ClosedWithReconnect;
        }

        private void NetworkDisconnected(object sender)
        {
            currentSocket?.Close(1002, string.Empty);
        }

        public bool IsConnected { get; set; }

        private readonly AsyncLock connectingLock = new AsyncLock();

        public async Task ConnectAsync()
        {
            using (await connectingLock.LockAsync())
            {
                if (IsConnected) return;
                await NetworkStatus.Instance.WhenConnected();
                await ExecuteConnectAsync();
            }
        }

        protected virtual async Task ExecuteConnectAsync()
        {
            var hasError = true;
            var isDisposed = true;
            while (hasError)
            {
                try
                {
                    if (isDisposed)
                    {
                        currentSocket = new MessageWebSocket();
                        currentSocket.MessageReceived += SocketMessageReceived;
                        currentSocket.Closed += SocketClosed;
                    }
                    await currentSocket.ConnectAsync(uri);
                    hasError = false;
                }
                catch (Exception e)
                {
                    isDisposed = e is ObjectDisposedException;
                    await Task.Delay(Interval);
                }
            }
            Debug.WriteLine("Socket #\{currentSocket.GetHashCode()} created");
            writer = new DataWriter(currentSocket.OutputStream);
            IsConnected = true;
            await FlushAsync();
        }

        public async virtual void Send(T message)
        {
            messageQueue.Enqueue(message);
            await FlushAsync();
        }

        private readonly AsyncLock flushingLock = new AsyncLock();

        protected virtual string SerializeMessage(T message)
        {
            return message.ToString();
        }

        public async Task FlushAsync()
        {
            if (!IsConnected) return;
            using (await flushingLock.LockAsync())
            {
                while (messageQueue.Count > 0)
                {
                    var message = messageQueue.Peek();
                    var count = messageQueue.Count;
                    var messageStr = SerializeMessage(message);
                    Debug.WriteLine("#\{currentSocket.GetHashCode()} >> \{messageStr}");
                    writer.WriteString(messageStr);
                    await writer.StoreAsync();
                    messageQueue.Dequeue();
                }
            }
        }

        protected virtual Task ReconnectAsync()
        {
            return ConnectAsync();
        }

        private async void ClosedWithReconnect(StatefulWebSocket<T> sender, WebSocketClosedEventArgs args)
        {
            using (await connectingLock.LockAsync())
            {
                if (IsConnected) return;
                await ReconnectAsync();
            }
        }

        protected void SocketMessageReceived(MessageWebSocket sender, MessageWebSocketMessageReceivedEventArgs args)
        {
            if (MessageReceived == null) return;
            DataReader reader;
            try
            {
                reader = args.GetDataReader();
            }
            catch (Exception e)
            {
                var status = WebSocketError.GetStatus(e.HResult);
                if (status == WebErrorStatus.ConnectionAborted)
                {
                    sender.Close(1002, string.Empty);
                    return;
                }
                throw e;
            }
            using (reader)
            {
                var message = reader.ReadString(reader.UnconsumedBufferLength);
                Debug.WriteLine("#\{sender.GetHashCode()} << \{message}");
                MessageReceived(this, message);
            }
        }

        protected void SocketClosed(IWebSocket sender, WebSocketClosedEventArgs args)
        {
            Debug.WriteLine("Socket #\{sender.GetHashCode()} closed");
            if (currentSocket == sender)
            {
                IsConnected = false;
                Dispose();
                if (Closed != null) Closed(this, args);
            }
            else
            {
                sender.Dispose();
            }
        }

#pragma warning disable CS1998
        protected virtual async Task ExecuteCloseAsync(ushort code, string reason)
#pragma warning restore CS1998
        {
            currentSocket?.Close(code, reason);
        }

        public async Task CloseAsync(ushort code = 1000, string reason = "")
        {
            if (!IsConnected) return;
            Closed -= ClosedWithReconnect;
            var tsc = new TaskCompletionSource<object>();
            TypedEventHandler<StatefulWebSocket<T>, WebSocketClosedEventArgs> handler = null;
            handler = (sender, args) =>
            {
                tsc.SetResult(null);
                Closed -= handler;
            };
            Closed += handler;
            await ExecuteCloseAsync(code, reason);
            await tsc.Task;
        }

        public void Dispose()
        {
            if (currentSocket != null)
            {
                currentSocket.Dispose();
                currentSocket = null;
                writer = null;
            }
        }

        public event TypedEventHandler<StatefulWebSocket<T>, string> MessageReceived;
        public event TypedEventHandler<StatefulWebSocket<T>, WebSocketClosedEventArgs> Closed;
    }
}
