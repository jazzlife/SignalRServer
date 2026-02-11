using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Security.Policy;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace SignalRServer
{
    public partial class MainWindow : Window
    {
        private const int MaxLogLines = 1000;

        private IHost? _host;
        private readonly Dispatcher _dispatcher;
        private readonly List<string> _connectedClients = new();

        public MainWindow()
        {
            InitializeComponent();
            _dispatcher = Dispatcher;
            MsgHub.SetMainWindow(this);
            ApplyStartupParameters(Environment.GetCommandLineArgs());
            _ = Start();
            LogMessage("SignalR 서버 애플리케이션이 시작되었습니다.");
        }

        private void ApplyStartupParameters(string[] args)
        {
            var hubPath = GetArgumentValue(args, "--hubPath");
            if (!string.IsNullOrWhiteSpace(hubPath))
            {
                HubPath.Text = hubPath.StartsWith('/') ? hubPath : $"/{hubPath}";
            }

            var portValue = GetArgumentValue(args, "--port");
            if (!string.IsNullOrWhiteSpace(portValue) &&
                int.TryParse(portValue, out var parsedPort) &&
                parsedPort > 0 &&
                parsedPort <= 65535)
            {
                PortTextBox.Text = parsedPort.ToString();
            }

            var urlsValue = GetArgumentValue(args, "--urls");
            var portFromUrls = ExtractPortFromUrls(urlsValue);
            if (portFromUrls.HasValue)
            {
                PortTextBox.Text = portFromUrls.Value.ToString();
            }
        }

        private static string? GetArgumentValue(string[] args, string key)
        {
            for (var i = 0; i < args.Length; i++)
            {
                var current = args[i];
                if (current.StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase))
                {
                    return current[(key.Length + 1)..];
                }

                if (string.Equals(current, key, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private static int? ExtractPortFromUrls(string? urls)
        {
            if (string.IsNullOrWhiteSpace(urls))
            {
                return null;
            }

            var splitUrls = urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var url in splitUrls)
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                    uri.Port > 0 &&
                    uri.Port <= 65535)
                {
                    return uri.Port;
                }

                var colonIndex = url.LastIndexOf(':');
                if (colonIndex < 0 || colonIndex + 1 >= url.Length)
                {
                    continue;
                }

                var portPart = url[(colonIndex + 1)..];
                var slashIndex = portPart.IndexOf('/');
                if (slashIndex >= 0)
                {
                    portPart = portPart[..slashIndex];
                }

                if (int.TryParse(portPart, out var port) && port > 0 && port <= 65535)
                {
                    return port;
                }
            }

            return null;
        }

        private void StartServerButton_Click(object sender, RoutedEventArgs e)
        {
            _= Start();
        }

        async Task Start()
        {
            try
            {
                string _hubPath = HubPath.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(_hubPath))
                {
                    MessageBox.Show("메시지 경로를 설정해주세요.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!_hubPath.StartsWith('/'))
                {
                    _hubPath = "/" + _hubPath;
                    HubPath.Text = _hubPath;
                }

                if (!int.TryParse(PortTextBox.Text, out int port))
                {
                    MessageBox.Show("올바른 포트 번호를 입력하세요.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var args = new[] { $"--hubPath={_hubPath}", $"--urls=http://*:{port}" };

                await StartServer(args);

                ServerStatusText.Text = "실행 중";
                ServerStatusText.Foreground = System.Windows.Media.Brushes.Green;
                StartServerButton.IsEnabled = false;
                StopServerButton.IsEnabled = true;
                PortTextBox.IsEnabled = false;
                HubPath.IsEnabled = false;

                LogMessage($"SignalR 서버가 포트 {port}에서 시작되었습니다.");
            }
            catch (Exception ex)
            {
                LogMessage($"서버 시작 중 오류 발생: {ex.Message}");
                MessageBox.Show($"서버 시작 중 오류가 발생했습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StopServerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ServerStatusText.Text = "중지 중";
                ServerStatusText.Foreground = System.Windows.Media.Brushes.DarkOrange;
                StartServerButton.IsEnabled = false;
                StopServerButton.IsEnabled = false;
                PortTextBox.IsEnabled = false;
                HubPath.IsEnabled = false;
                
                LogMessage("SignalR 서버 중지를 요청했습니다.");

                StopServerInBackground(updateUiWhenDone: true);
            }
            catch (Exception ex)
            {
                LogMessage($"서버 중지 중 오류 발생: {ex.Message}");
                MessageBox.Show($"서버 중지 중 오류가 발생했습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task StartServer(string[] args)
        {
            _host = Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                })
                .Build();

            await _host.StartAsync();
        }

        private void StopServerInBackground(bool updateUiWhenDone)
        {
            var hostToStop = Interlocked.Exchange(ref _host, null);

            if (hostToStop == null)
            {
                if (updateUiWhenDone)
                {
                    SetStoppedUi();
                }

                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await hostToStop.StopAsync();
                }
                catch (Exception ex)
                {
                    LogMessage($"서버 백그라운드 중지 중 오류 발생: {ex.Message}");
                }
                finally
                {
                    hostToStop.Dispose();

                    if (updateUiWhenDone)
                    {
                        _ = _dispatcher.BeginInvoke(new Action(SetStoppedUi));
                    }
                }
            });
        }

        private void SetStoppedUi()
        {
            ServerStatusText.Text = "중지됨";
            ServerStatusText.Foreground = System.Windows.Media.Brushes.Red;
            StartServerButton.IsEnabled = true;
            StopServerButton.IsEnabled = false;
            PortTextBox.IsEnabled = true;
            HubPath.IsEnabled = true;

            LogMessage("SignalR 서버가 중지되었습니다.");
        }

        public async void SendMessageButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(MessageTextBox.Text))
            {
                MessageBox.Show("메시지를 입력해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                if (_host != null)
                {
                    var hubContext = _host.Services.GetRequiredService<IHubContext<MsgHub>>();

                    await hubContext.Clients.All.SendAsync("ReceiveMessage",
                        new SignalRMessage() { From="Server", To="Others", Command = "Update", Data = MessageTextBox.Text });
                    
                    LogMessage($"서버에서 전체 메시지 전송: {MessageTextBox.Text}");
                    
                    MessageTextBox.Clear();
                }
            }
            catch (Exception ex)
            {
                LogMessage($"메시지 전송 실패: {ex.Message}");
            }
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            LogListBox.Items.Clear();
        }

        public void LogMessage(string message)
        {
            if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
            {
                return;
            }

            _ = _dispatcher.BeginInvoke(new Action(() =>
            {
                LogListBox.Items.Add($"[{DateTime.Now:HH:mm:ss}] {message}");

                while (LogListBox.Items.Count > MaxLogLines)
                {
                    LogListBox.Items.RemoveAt(0);
                }

                if (LogListBox.Items.Count > 0)
                    LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
            }));
        }

        public void UpdateConnectedClients(List<string> clients)
        {
            if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
            {
                return;
            }

            _ = _dispatcher.BeginInvoke(new Action(() =>
            {
                ConnectedClientsListBox.Items.Clear();
                foreach (var client in clients)
                {
                    ConnectedClientsListBox.Items.Add(client);
                }
                
                // 연결된 클라이언트 수 표시
                var clientCount = clients.Count;
                ConnectedClientsListBox.ToolTip = $"연결된 클라이언트: {clientCount}개";
                
                // 서버 상태 업데이트
                if (ServerStatusText.Text == "실행 중")
                {
                    ServerStatusText.Text = $"실행 중 ({clientCount}개 연결)";
                }
            }));
        }

        protected override void OnClosed(EventArgs e)
        {
            StopServerInBackground(updateUiWhenDone: false);
            base.OnClosed(e);
        }
    }

    public class MsgHub : Hub
    {
        private static readonly List<string> ConnectedClients = new();
        private static MainWindow? _mainWindow;

        public static void SetMainWindow(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
        }

        public override async Task OnConnectedAsync()
        {
            var clientId = Context.ConnectionId;
            ConnectedClients.Add(clientId);
            
            _mainWindow?.LogMessage($"클라이언트 연결됨: {clientId}");
            _mainWindow?.UpdateConnectedClients(ConnectedClients);
            
            //await Clients.All.SendAsync("ReceiveMessage", "시스템", $"새로운 클라이언트가 연결되었습니다: {clientId}");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var clientId = Context.ConnectionId;
            ConnectedClients.Remove(clientId);
            
            _mainWindow?.LogMessage($"클라이언트 연결 해제됨: {clientId}");
            _mainWindow?.UpdateConnectedClients(ConnectedClients);

            await Clients.All.SendAsync("ReceiveMessage",
                new SignalRMessage()
                {
                    From = "Server",
                    To = "Others",
                    Command = "Message",
                    DataType = "StateMessage",
                    Data = new StateMessage()
                    {
                        Who = clientId,
                        State = "Disconnected",
                        Description = "클라이언트 연결 해제"
                    }
                });
                
            await base.OnDisconnectedAsync(exception);
        }

        public async Task JoinGroup(string groupName)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
            _mainWindow?.LogMessage($"클라이언트 {Context.ConnectionId}가 그룹 {groupName}에 참가했습니다.");
            //await Clients.Group(groupName).SendAsync("ReceiveMessage", "시스템", $"{Context.ConnectionId}가 그룹에 참가했습니다.");
        }

        // Hyunmu.Service 클라이언트 호환성을 위한 추가 메서드들

        /// <summary>
        /// 하트비트 응답 - 연결 상태 확인
        /// </summary>
        public async Task<object> Heartbeat()
        {
            //_mainWindow?.LogMessage($"하트비트 요청 수신: {Context.ConnectionId}");
            return new { 
                Status = "OK", 
                Timestamp = DateTime.UtcNow, 
                ConnectionId = Context.ConnectionId,
                Message = "하트비트 응답"
            };
        }

        /// <summary>
        /// 헬스체크 응답 - 서버 상태 확인
        /// </summary>
        public async Task<object> HealthCheck()
        {
            _mainWindow?.LogMessage($"헬스체크 요청 수신: {Context.ConnectionId}");
            return new { 
                Status = "Healthy", 
                Timestamp = DateTime.UtcNow, 
                ConnectedClients = ConnectedClients.Count,
                ServerUptime = DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime(),
                Message = "서버 정상 동작 중"
            };
        }

        /// <summary>
        /// 연결 통계 반환
        /// </summary>
        public async Task<object> GetConnectionStats()
        {
            _mainWindow?.LogMessage($"연결 통계 요청 수신: {Context.ConnectionId}");
            return new { 
                ConnectionId = Context.ConnectionId,
                ConnectedClients = ConnectedClients.Count,
                ServerStartTime = Process.GetCurrentProcess().StartTime.ToUniversalTime(),
                CurrentTime = DateTime.UtcNow,
                Uptime = DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()
            };
        }

        /// <summary>
        /// 그룹에서 나가기
        /// </summary>
        public async Task LeaveGroup(string groupName)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
            _mainWindow?.LogMessage($"클라이언트 {Context.ConnectionId}가 그룹 {groupName}에서 나갔습니다.");
            //await Clients.Group(groupName).SendAsync("ReceiveMessage", "시스템", $"{Context.ConnectionId}가 그룹에서 나갔습니다.");
        }


        #region 메시지 중계
        public async Task<bool> SendMessageByClientProxy(ISingleClientProxy client, object message)
        {
            try
            {
                _mainWindow?.LogMessage($"메시지 전송 요청");

                await client.SendAsync("ReceiveMessage", message);

                _mainWindow?.LogMessage($"메시지 전송 완료");
                return true;
            }
            catch (Exception ex)
            {
                _mainWindow?.LogMessage($"메시지 전송 실패: {ex.Message}");
            }

            return false;
        }

        public async Task<bool> SendMessagesByClientProxy(ISingleClientProxy client, object[] messages)
        {
            try
            {
                _mainWindow?.LogMessage($"대량메시지 전송 요청");

                foreach (var message in messages)
                    await client.SendAsync("ReceiveMessage", message);

                _mainWindow?.LogMessage($"대량메시지 전송 완료");
                return true;
            }
            catch (Exception ex)
            {
                _mainWindow?.LogMessage($"대량메시지 전송 실패: {ex.Message}");
            }

            return false;
        }

        public async Task<bool> SendMessage(string to_connection_id, object message)
        {
            try
            {
                _mainWindow?.LogMessage($"메시지 전송 요청: {to_connection_id}");

                await Clients.Client(to_connection_id).SendAsync("ReceiveMessage", message);

                _mainWindow?.LogMessage($"메시지 전송 완료: {to_connection_id}");
                return true;
            }
            catch (Exception ex)
            {
                _mainWindow?.LogMessage($"메시지 전송 실패: {ex.Message}");
            }

            return false;
        }

        public async Task<bool> SendMessages(string to_connection_id, object[] messages)
        {
            try
            {
                _mainWindow?.LogMessage($"대량메시지 전송 요청: {to_connection_id}");

                foreach (var message in messages)
                    await Clients.Client(to_connection_id).SendAsync("ReceiveMessage", message);

                _mainWindow?.LogMessage($"대량메시지 전송 완료: {to_connection_id}");
                return true;
            }
            catch (Exception ex)
            {
                _mainWindow?.LogMessage($"대량메시지 전송 실패: {ex.Message}");
            }

            return false;
        }

        public async Task<bool> SendMessageToGroup(string groupName, object message)
        {
            try
            {
                _mainWindow?.LogMessage($"그룹 메시지 요청: {groupName}");
                    
                await Clients.GroupExcept(groupName, Context.ConnectionId).SendAsync("ReceiveMessage", message);

                _mainWindow?.LogMessage($"그룹 메시지 전송 완료: {groupName}");
                return true;
            }
            catch (Exception ex)
            {
                _mainWindow?.LogMessage($"그룹 메시지 전송 실패: {ex.Message}");
            }

            return true;
        }

        public async Task<bool> SendMessagesToGroup(string groupName, object[] messages)
        {
            try
            {
                _mainWindow?.LogMessage($"그룹 대량메시지 전송 요청: {groupName}");

                foreach (var message in messages)
                    await Clients.GroupExcept(groupName, Context.ConnectionId).SendAsync("ReceiveMessage", message);

                _mainWindow?.LogMessage($"그룹 대량메시지 전송 완료: {groupName}");
                return true;
            }
            catch (Exception ex)
            {
                _mainWindow?.LogMessage($"그룹 대량메시지 전송 실패: {ex.Message}");
            }

            return true;
        }

        public async Task<bool> SendMessageToAll(object message)
        {
            _mainWindow?.LogMessage($"메시지 브로드캐스트 요청: {Context.ConnectionId}");

            try
            {
                await Clients.AllExcept(Context.ConnectionId).SendAsync("ReceiveMessage", message);

                _mainWindow?.LogMessage($"메시지 브로드캐스트 완료: {Context.ConnectionId}");
                return true;
            }
            catch (Exception ex)
            {
                _mainWindow?.LogMessage($"메시지 브로드캐스트 실패: {ex.Message}");
            }

            return false;
        }

        public async Task<bool> SendMessagesToAll(object[] messages)
        {
            _mainWindow?.LogMessage($"대량 메시지 브로드캐스트 요청: {Context.ConnectionId}, 메시지 수: {messages.Length}");

            try
            {
                foreach (var message in messages)
                    await Clients.AllExcept(Context.ConnectionId).SendAsync("ReceiveMessage", message);

                _mainWindow?.LogMessage($"대량 메시지 브로드캐스트 완료: {messages.Length}개 메시지 전송");
                return true;
            }
            catch (Exception ex)
            {
                _mainWindow?.LogMessage($"대량 메시지 브로드캐스트 실패: {ex.Message}");
            }

            return false;
        }
        #endregion
    }
} 
