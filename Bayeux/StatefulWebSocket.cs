using Bayeux.Util;
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
            currentSocket.Close(1002, string.Empty);
        }

        public bool IsConnected { get; set; }

        private bool isConnecting = false;

        public async Task ConnectAsync()
        {
            if (IsConnected || isConnecting) return;
            isConnecting = true;
            try
            {
                await ExecuteConnectAsync();
            }
            finally
            {
                isConnecting = false;
            }
        }

        protected virtual async Task ExecuteConnectAsync()
        {
            currentSocket = new MessageWebSocket();
            currentSocket.MessageReceived += SocketMessageReceived;
            currentSocket.Closed += SocketClosed;
            Debug.WriteLine("Socket #\{currentSocket.GetHashCode()} created");
            await NetworkStatus.Instance.AutoRetry(async () =>
            {
                await currentSocket.ConnectAsync(uri);
                return true;
            }, 10, Interval);
            writer = new DataWriter(currentSocket.OutputStream);
            IsConnected = true;
            await FlushAsync();
        }

        public async virtual void Send(T message)
        {
            messageQueue.Enqueue(message);
            await FlushAsync();
        }

        private bool isFlushing = false;

        protected virtual string SerializeMessage(T message)
        {
            return message.ToString();
        }

        public async Task FlushAsync()
        {
            if (!IsConnected || isFlushing) return;
            isFlushing = true;
            try
            {
                while (messageQueue.Count > 0)
                {
                    var message = messageQueue.Peek();
                    var messageStr = SerializeMessage(message);
                    Debug.WriteLine("#\{currentSocket.GetHashCode()} >> \{messageStr}");
                    writer.WriteString(messageStr);
                    await writer.StoreAsync();
                    messageQueue.Dequeue();
                }
            }
            finally
            {
                isFlushing = false;
            }
        }

        protected virtual Task ReconnectAsync()
        {
            return ConnectAsync();
        }

        private async void ClosedWithReconnect(StatefulWebSocket<T> sender, WebSocketClosedEventArgs args)
        {
            if (isConnecting) return;
            await ReconnectAsync();
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

        public virtual async Task CloseAsync(ushort code = 1000, string reason = "")
        {
            if (!IsConnected) return;
            Closed -= ClosedWithReconnect;
            var tsc = new TaskCompletionSource<object>();
            Closed += (sender, args) => tsc.SetResult(null);
            currentSocket.Close(code, reason);
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
