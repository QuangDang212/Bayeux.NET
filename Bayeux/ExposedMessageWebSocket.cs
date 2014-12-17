using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using ClosedEventHandler = Windows.Foundation.TypedEventHandler<Windows.Networking.Sockets.IWebSocket, Windows.Networking.Sockets.WebSocketClosedEventArgs>;

namespace Bayeux
{
    public interface IExposedMessageWebSocket : IWebSocket, IDisposable
    {
        MessageWebSocketControl Control { get; }
        MessageWebSocketInformation Information { get; }

        event TypedEventHandler<MessageWebSocket, MessageWebSocketMessageReceivedEventArgs> MessageReceived;
    }

    public class ExposedMessageWebSocket : IExposedMessageWebSocket
    {
        private MessageWebSocket innerWebSocket = new MessageWebSocket();
        private EventRegistrationTokenTable<ClosedEventHandler> closedEventTable = new EventRegistrationTokenTable<ClosedEventHandler>();

        public ExposedMessageWebSocket()
        {
            innerWebSocket.Closed += InnerWebSocket_Closed;
        }

        private void InnerWebSocket_Closed(IWebSocket sender, WebSocketClosedEventArgs args)
        {
            closedEventTable.InvocationList(sender, args);
        }

        public MessageWebSocketControl Control
        {
            get { return innerWebSocket.Control; }
        }

        public MessageWebSocketInformation Information
        {
            get { return innerWebSocket.Information; }
        }

        public IOutputStream OutputStream
        {
            get { return innerWebSocket.OutputStream; }
        }

        public event ClosedEventHandler Closed
        {
            add { return closedEventTable.AddEventHandler(value); }
            remove { closedEventTable.RemoveEventHandler(value); }
        }

        public event TypedEventHandler<MessageWebSocket, MessageWebSocketMessageReceivedEventArgs> MessageReceived
        {
            add { innerWebSocket.MessageReceived += value; }
            remove { innerWebSocket.MessageReceived -= value; }
        }

        public void Close(ushort code, string reason)
        {
            innerWebSocket.Close(code, reason);
        }

        public IAsyncAction ConnectAsync(Uri uri)
        {
            return innerWebSocket.ConnectAsync(uri);
        }

        public void Dispose()
        {
            innerWebSocket.Dispose();
        }

        public void SetRequestHeader(string headerName, string headerValue)
        {
            innerWebSocket.SetRequestHeader(headerName, headerValue);
        }
    }
}
