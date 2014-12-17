using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace Bayeux
{
    public class StatefulWebSocket : IDisposable
    {
        private readonly Uri uri;
        private MessageWebSocket currentSocket;
        private DataWriter writer;
        private readonly Queue<string> messageQueue = new Queue<string>();

        public StatefulWebSocket(string url)
        {
            uri = new Uri(url, UriKind.Absolute);
        }

        public bool IsConnected { get; set; }

        public virtual async Task ConnectAsync()
        {
            if (IsConnected) return;
            currentSocket = new MessageWebSocket();
            currentSocket.MessageReceived += SocketMessageReceived;
            currentSocket.Closed += SocketClosed;
            currentSocket.Closed += CurrentSocket_ClosedWithReconnect;
            var hasError = true;
            while (hasError)
            {
                try
                {
                    await currentSocket.ConnectAsync(uri);
                    hasError = false;
                }
                catch (Exception)
                {
                    hasError = true;
                }
            }
            writer = new DataWriter(currentSocket.OutputStream);
            IsConnected = true;
            await FlushAsync();
        }

        public async void Send(string message)
        {
            messageQueue.Enqueue(message);
            await FlushAsync();
        }

        private bool isFlushing = false;

        public async Task FlushAsync()
        {
            if (!IsConnected || isFlushing) return;
            isFlushing = true;
            try
            {
                while (messageQueue.Count > 0)
                {
                    var message = messageQueue.Peek();
                    Debug.WriteLine(">> \{message}");
                    writer.WriteString(message);
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

        private async void CurrentSocket_ClosedWithReconnect(IWebSocket sender, WebSocketClosedEventArgs args)
        {
            await ReconnectAsync();
        }

        protected void SocketMessageReceived(MessageWebSocket sender, MessageWebSocketMessageReceivedEventArgs args)
        {
            if (MessageReceived == null) return;
            using (var reader = args.GetDataReader())
            {
                var message = reader.ReadString(reader.UnconsumedBufferLength);
                Debug.WriteLine("<< \{message}");
                MessageReceived(this, message);
            }
        }

        protected virtual void SocketClosed(IWebSocket sender, WebSocketClosedEventArgs args)
        {
            IsConnected = false;
            if (Closed != null) Closed(this, args);
            Dispose();
        }

        public virtual async Task CloseAsync(ushort code = 1000, string reason = "")
        {
            if (!IsConnected) return;
            currentSocket.Closed -= CurrentSocket_ClosedWithReconnect;
            var tsc = new TaskCompletionSource<object>();
            currentSocket.Closed += (sender, args) => tsc.SetResult(null);
            currentSocket.Close(code, reason);
            await tsc.Task;
        }

        public void Dispose()
        {
            if (writer != null)
            {
                writer.Dispose();
                writer = null;
            }
            if (currentSocket != null)
            {
                currentSocket.Dispose();
                currentSocket = null;
            }
        }

        public event TypedEventHandler<StatefulWebSocket, string> MessageReceived;
        public event TypedEventHandler<StatefulWebSocket, WebSocketClosedEventArgs> Closed;
    }
}
