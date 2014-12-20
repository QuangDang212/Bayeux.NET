using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Networking.Connectivity;
using Windows.UI.Core;

namespace Bayeux.Util
{
    public sealed class NetworkStatus : INotifyPropertyChanged
    {
        private bool isConnected;
        public event NetworkStatusChangedEventHandler Connected, Disconnected;
        public event PropertyChangedEventHandler PropertyChanged;
        private readonly Queue<TaskCompletionSource<object>> waiters = new Queue<TaskCompletionSource<object>>();

        private bool GetIsConnected()
        {
            var inetProfile = NetworkInformation.GetInternetConnectionProfile();
            if (inetProfile == null) return false;
            return inetProfile.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;
        }

        public bool IsConnected
        {
            get { return isConnected; }
            private set
            {
                if (isConnected == value) return;
                isConnected = value;
                Task.Run(async () =>
                {
                    if (isConnected != value) return;
                    Debug.WriteLine("Network \{isConnected ? "connected" : "disconnected"}");
                    if (isConnected)
                    {
                        /*
                        await Task.Delay(1000);
                        if (!isConnected) return;
                        */
                        if (Connected != null) Connected(this);
                        while (waiters.Count > 0)
                        {
                            waiters.Dequeue().SetResult(null);
                        }
                    }
                    else
                    {
                        if (Disconnected != null) Disconnected(this);
                    }
                    if (PropertyChanged != null)
                    {
                        await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                            PropertyChanged(this, new PropertyChangedEventArgs(nameof(IsConnected)))
                        );
                    }
                });
            }
        }

        private NetworkStatus()
        {
            isConnected = GetIsConnected();
            NetworkInformation.NetworkStatusChanged += NetworkInformation_NetworkStatusChanged;
        }

        public static readonly NetworkStatus Instance = new NetworkStatus();

        private void NetworkInformation_NetworkStatusChanged(object sender)
        {
            IsConnected = GetIsConnected();
        }

        public async Task WhenConnected()
        {
            if (IsConnected) return;
            var tsc = new TaskCompletionSource<object>();
            waiters.Enqueue(tsc);
            await tsc.Task;
        }

        public async Task<T> AutoRetry<T>(Func<Task<T>> execute, int retryCount = 3, int interval = 1000)
        {
            await WhenConnected();
            while (--retryCount > 0)
            {
                try
                {
                    return await execute();
                }
                catch (Exception)
                {
                    await Task.Delay(interval);
                }
            }
            return await execute();
        }
    }
}
