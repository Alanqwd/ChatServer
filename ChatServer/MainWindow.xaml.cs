using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ChatServer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private TcpListener listener;
        private readonly ConcurrentDictionary<TcpClient, string> clients = new ConcurrentDictionary<TcpClient, string>();
        private UdpClient udpListener;
        private bool isRunning = false;
        private const int UDP_PORT = 5001;

        public MainWindow()
        {
            InitializeComponent();
            StartServer();
        }

        private async void StartServer()
        {
            listener = new TcpListener(IPAddress.Any, 5000);

         
            udpListener = new UdpClient(UDP_PORT);

            try
            {
                listener.Start();
                isRunning = true;
                AppendChat("Сервер запущен.");

               
                _ = Task.Run(HandleUdpMessages);

                while (isRunning)
                {
                    var client = await listener.AcceptTcpClientAsync();
                    _ = HandleClientAsync(client);
                }
            }
            catch (Exception ex)
            {
                AppendChat($"Ошибка сервера: {ex.Message}");
            }
        }

        private async Task HandleUdpMessages()
        {
            while (isRunning)
            {
                try
                {
                    var result = await udpListener.ReceiveAsync();
                    string message = Encoding.UTF8.GetString(result.Buffer).Trim();

                    if (message.EndsWith("ONLINE"))
                    {
                        string clientName = message.Replace("ONLINE", "").Trim();
                        AppendChat($"Новый клиент {clientName} объявил себя онлайн (UDP)");
                    }
                }
                catch (Exception ex)
                {
                    if (isRunning)
                        AppendChat($"UDP ошибка: {ex.Message}");
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            var stream = client.GetStream();
            string clientEndPoint = client.Client.RemoteEndPoint.ToString();
            string clientName = "";
            byte[] buffer = new byte[1024];

            try
            {
                int byteCount = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (byteCount > 0)
                {
                    string nameMessage = Encoding.UTF8.GetString(buffer, 0, byteCount).Trim();
                    clientName = nameMessage;
                }

                clients.TryAdd(client, clientEndPoint);
                AppendChat($"Подключился: {clientName}");

               
                await BroadcastUdpNotification($"{clientName} ONLINE");

                string oldName = clientName;

                while (true)
                {
                    byteCount = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (byteCount == 0) break;
                    string message = Encoding.UTF8.GetString(buffer, 0, byteCount);

                    if (message.StartsWith("/login "))
                    {
                        string newLogin = message.Substring("/login ".Length).Trim();
                        if (!string.IsNullOrEmpty(newLogin))
                        {
                            clients[client] = newLogin;
                            clientName = newLogin;
                            AppendChat($"Пользователь {oldName} сменил логин на: {newLogin}");
                            oldName = newLogin;
                            continue;
                        }
                        else
                        {
                            AppendChat($"Ошибка: Пустой логин после команды /login от {clientEndPoint}");
                        }
                    }
                    else
                    {
                        AppendChat($"От {clientName}: {message}");

                        foreach (var kvp in clients)
                        {
                            if (kvp.Key != client && kvp.Key.Connected)
                            {
                                try
                                {
                                    var sendStream = kvp.Key.GetStream();
                                    string outMsg = $"От {clients[client]}: {message}";
                                    byte[] msgBytes = Encoding.UTF8.GetBytes(outMsg);
                                    await sendStream.WriteAsync(msgBytes, 0, msgBytes.Length);
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            finally
            {
                clients.TryRemove(client, out _);
                client.Close();
                AppendChat($"Отключен: {clientName}");

               
                await BroadcastUdpNotification($"{clientName} OFFLINE");
            }
        }

        private async Task BroadcastUdpNotification(string message)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(message);

                
                var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, UDP_PORT);
                await udpListener.SendAsync(data, data.Length, broadcastEndpoint);


                var localEndpoint = new IPEndPoint(IPAddress.Loopback, UDP_PORT);
                await udpListener.SendAsync(data, data.Length, localEndpoint);
            }
            catch (Exception ex)
            {
                AppendChat($"Ошибка UDP рассылки: {ex.Message}");
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            isRunning = false;
            listener?.Stop();
            udpListener?.Close();
        }

        private void AppendChat(string message)
        {
            Dispatcher.Invoke(() =>
            {
                ChatTextBox.AppendText(message + Environment.NewLine);
                ChatTextBox.ScrollToEnd();
            });
        }
    }
}