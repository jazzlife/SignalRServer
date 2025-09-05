using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace SignalRServer
{
    public partial class MainWindow : Window
    {
        private IHost? _host;
        private readonly Dispatcher _dispatcher;
        private readonly List<string> _connectedClients = new();

        public MainWindow()
        {
            InitializeComponent();
            _dispatcher = Dispatcher;
            ChatHub.SetMainWindow(this);
            _ = Start();
            LogMessage("SignalR 서버 애플리케이션이 시작되었습니다.");
        }

        private void StartServerButton_Click(object sender, RoutedEventArgs e)
        {
            _= Start();
        }

        async Task Start()
        {
            try
            {
                if (!int.TryParse(PortTextBox.Text, out int port))
                {
                    MessageBox.Show("올바른 포트 번호를 입력하세요.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                await StartServer(port);

                ServerStatusText.Text = "실행 중";
                ServerStatusText.Foreground = System.Windows.Media.Brushes.Green;
                StartServerButton.IsEnabled = false;
                StopServerButton.IsEnabled = true;
                PortTextBox.IsEnabled = false;

                LogMessage($"SignalR 서버가 포트 {port}에서 시작되었습니다.");
            }
            catch (Exception ex)
            {
                LogMessage($"서버 시작 중 오류 발생: {ex.Message}");
                MessageBox.Show($"서버 시작 중 오류가 발생했습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void StopServerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await StopServer();
                
                ServerStatusText.Text = "중지됨";
                ServerStatusText.Foreground = System.Windows.Media.Brushes.Red;
                StartServerButton.IsEnabled = true;
                StopServerButton.IsEnabled = false;
                PortTextBox.IsEnabled = true;
                
                LogMessage("SignalR 서버가 중지되었습니다.");
            }
            catch (Exception ex)
            {
                LogMessage($"서버 중지 중 오류 발생: {ex.Message}");
                MessageBox.Show($"서버 중지 중 오류가 발생했습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task StartServer(int port)
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseUrls($"http://localhost:{port}");
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

        private async Task StopServer()
        {
            if (_host != null)
            {
                await _host.StopAsync();
                _host.Dispose();
                _host = null;
            }
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
                    var hubContext = _host.Services.GetRequiredService<IHubContext<ChatHub>>();

                    await hubContext.Clients.All.SendAsync("ReceiveMessage",
                        await TypelessMessageHelper.SerializeAsync(
                            new TypelessMessage() { To = "첫번째 플레이어", Command = "Update", Data = MessageTextBox.Text }));
                    
                    LogMessage($"서버에서 전체 메시지 전송: {MessageTextBox.Text}");
                    
                    MessageTextBox.Clear();
                }
            }
            catch (Exception ex)
            {
                LogMessage($"메시지 전송 실패: {ex.Message}");
            }
        }

        public void LogMessage(string message)
        {
            _dispatcher.Invoke(() =>
            {
                LogListBox.Items.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
                if (LogListBox.Items.Count > 0)
                    LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
            });
        }

        public void UpdateConnectedClients(List<string> clients)
        {
            _dispatcher.Invoke(() =>
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
            });
        }

        protected override async void OnClosed(EventArgs e)
        {
            await StopServer();
            base.OnClosed(e);
        }
    }

    public class ChatHub : Hub
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
                await TypelessMessageHelper.SerializeAsync(
                            new TypelessMessage()
                            {
                                To = "서버",
                                Command = "Message",
                                DataType = typeof(StateMessage).Name,
                                Data = new StateMessage()
                                {
                                    Who = clientId,
                                    State = "Disconnected",
                                    Description = "클라이언트 연결 해제"
                                }
                            }));
            await base.OnDisconnectedAsync(exception);
        }

        public async Task SendMessage(string user, string message)
        {
            _mainWindow?.LogMessage($"메시지 수신: {user} - {message}");

            //await Clients.Users(user).SendAsync("ReceiveMessage", 
            //    await TypelessMessageHelper.GenerateMessage(
            //                new TypelessMessage() { To = user, Command = "Message", Data = message }));

            await Clients.AllExcept(new List<string>() { Context.ConnectionId }).SendAsync("ReceiveMessage", 
                await TypelessMessageHelper.SerializeAsync(
                            new TypelessMessage() { To = user, Command = "Message", Data = message }));
        }

        public async Task JoinGroup(string groupName)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
            _mainWindow?.LogMessage($"클라이언트 {Context.ConnectionId}가 그룹 {groupName}에 참가했습니다.");
            //await Clients.Group(groupName).SendAsync("ReceiveMessage", "시스템", $"{Context.ConnectionId}가 그룹에 참가했습니다.");
        }

        public async Task SendToGroup(string groupName, string user, string message)
        {
            _mainWindow?.LogMessage($"그룹 메시지: {groupName} - {user} - {message}");

            //await Clients.Group(groupName).SendAsync("ReceiveMessage", user, message);

            await Clients.GroupExcept(groupName, new List<string>() { Context.ConnectionId }).SendAsync("ReceiveMessage",
                await TypelessMessageHelper.SerializeAsync(
                            new TypelessMessage() { To = user, Command = "Message", Data = message }));

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
        /// 대량 메시지 브로드캐스트
        /// </summary>
        public async Task<bool> BroadcastBulkMessages(object[] messages)
        {
            _mainWindow?.LogMessage($"대량 메시지 브로드캐스트 요청 수신: {Context.ConnectionId}, 메시지 수: {messages.Length}");
            
            try
            {
                foreach (var message in messages)
                {
                    //await Clients.All.SendAsync("ReceiveMessage", "시스템", $"대량 메시지: {message}");

                    await Clients.AllExcept(new List<string>() { Context.ConnectionId }).SendAsync("ReceiveMessage",
                        await TypelessMessageHelper.SerializeAsync(
                                    new TypelessMessage() { Command = "Message", Data = message }));
                }
                
                _mainWindow?.LogMessage($"대량 메시지 브로드캐스트 완료: {messages.Length}개 메시지 전송");
                return true;
            }
            catch (Exception ex)
            {
                _mainWindow?.LogMessage($"대량 메시지 브로드캐스트 실패: {ex.Message}");
                return false;
            }
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
    }
} 