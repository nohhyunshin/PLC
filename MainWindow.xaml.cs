using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Media;
using System.Net.Configuration;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using XGCommLib;

namespace PLC
{
    /// <summary>
    /// MainWindow.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class MainWindow : Window
    {
        // WPF 실행 시 이벤트 핸들러 연결 확인
        private bool _isLoaded = false;

        // 네온 효과 - 층 버튼 클릭 시 동작
        private readonly DropShadowEffect neonEffect;

        // 층 이동 시 화살표 깜빡임
        private DispatcherTimer blinkTimer;
        private bool isUpVisible = true;

        // PLC 연결용
        private readonly CommObjectFactory20 _factory = new CommObjectFactory20();
        private bool _connected = false;
        private bool test = false;          // 연결 테스트 확인
        private string _endpoint = "";

        public MainWindow()
        {
            InitializeComponent();

            // 네온 효과 정의
            neonEffect = new DropShadowEffect
            {
                Color = Colors.Red, // 네온 불빛 색
                BlurRadius = 15,
                ShadowDepth = 0,
                Opacity = 1
            };

            // 깜빡임 타이머 초기화
            blinkTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500) // 0.5초 간격
            };
            blinkTimer.Tick += BlinkTimer_Tick;

            // 창이 로드될 때 이벤트 핸들러 연결
            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isLoaded) return; // 이미 실행된 경우 무시
            _isLoaded = true;

            try
            {
                // PLC IP/Port 설정
                string ip = "192.168.0.200";  // 예시
                int port = 2004;              // 예시

                var ep = $"{ip}:{port}";

                var drv = _factory.GetMLDPCommObject20(ep);
                int ret = drv.Connect("");

                if (ret == 1)
                {
                    _connected = true;
                    _endpoint = ep;
                    test = true;
                    
                    MessageBox.Show($"PLC 연결 성공! {ep}");
                }
                else
                {
                    _connected = false;
                    test = false;
                    MessageBox.Show("PLC 연결 실패");
                }

                try { drv.Disconnect(); } catch { }
                _connected = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("PLC 연결 중 오류: " + ex.Message);
            }

            // 버튼 이벤트 연결
            foreach (var child in ElevatorPanel.Children)
            {
                if (child is Button btn)
                {
                    btn.Click -= FloorButton_Click; // 기존 이벤트 제거
                    btn.Click += FloorButton_Click; // 새로 등록
                }
            }
        }

        private async void FloorButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show($"{_connected}");
            if (!(sender is Button btn)) return;
            if (!_connected)
            {
                MessageBox.Show("PLC와 연결되지 않았습니다.");
                return;
            }

            // 클릭한 버튼에만 네온 효과 적용 (이전 버튼 제거)
            foreach (var child in ElevatorPanel.Children)
            {
                if (child is StackPanel sp)
                {
                    foreach (var inner in sp.Children)
                    {
                        if (inner is Button b)
                        {
                            b.Effect = null;
                            b.Foreground = Brushes.Black;
                            b.FontWeight = FontWeights.Normal;
                        }
                    }
                }
            }

            // 클릭한 버튼 켜기
            btn.Effect = neonEffect;
            btn.Foreground = Brushes.DarkRed;
            btn.FontWeight = FontWeights.Bold;

            // 버튼 클릭 시 깜빡임 시작
            blinkTimer.Start();
            isUpVisible = true;

            // 방향 표시: 현재 층 < 눌린 층이면 ▲, 아니면 ▼
            int currentFloor = int.TryParse(display.Text, out int cf) ? cf : 1;
            int targetFloor = int.Parse(btn.Content.ToString());

            string direction = targetFloor > currentFloor ? "▲" : "▼";
            upDown.Text = direction;

            // 유효 층수 체크
            if (targetFloor < 1 || targetFloor > 6)
            {
                MessageBox.Show($"{targetFloor}층은 존재하지 않습니다.\n1~6층만 이용 가능합니다.",
                               "잘못된 층수", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                WithFreshDriver(drv =>
                {
                    var device = _factory.CreateDevice();
                    device.ucDataType = (byte)'B';
                    device.ucDeviceType = (byte)'M';
                    device.lOffset = 0;              // MX0부터
                    device.lSize = 1;
                    drv.AddDeviceInfo(device);

                    // 해당 층의 비트만 ON, 나머지는 OFF
                    byte targetBit = (byte)(1 << (targetFloor));
                    byte[] buf = new byte[1] { targetBit };

                    MessageBox.Show($"{targetFloor}층 → 비트패턴: {Convert.ToString(buf[0], 2).PadLeft(8, '0')}");
                    int ret = drv.WriteRandomDevice(buf);
                    return ret;
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("PLC 처리 오류: " + ex.Message);
            }

            // 층 이동 시뮬레이션 (1층씩 TON 느낌으로 1초 간격)
            int totalFloors = Math.Abs(targetFloor - currentFloor);
            await Task.Delay(2000);     // 이동에 3초만 소요

            // 이동 끝나면 깜빡임 종료 = 해당 층에 도착!
            blinkTimer.Stop();
            upDown.Text = $"{direction}"; // 깜빡임 끝나고 방향 고정
            display.Text = $"{targetFloor}";

            // 엘리베이터 도착 전 딜레이 1초
            //await Task.Delay(300);

            // 엘리베이터 도착 사운드 재생
            try
            {
                SoundPlayer player = new SoundPlayer("beep.wav");
                player.Play(); // 비동기 재생 (UI 멈추지 않음)
            }
            catch (Exception ex)
            {
                MessageBox.Show("사운드 재생 오류: " + ex.Message);
            }

            await Task.Delay(2000);
            await OpenDoor();
        }

        // 깜빡임 타이머
        private void BlinkTimer_Tick(object sender, EventArgs e)
        {
            upDown.Visibility = isUpVisible ? Visibility.Hidden : Visibility.Visible;
            isUpVisible = !isUpVisible;
        }

        // 엘리베이터 문 개폐 동작
        private async Task OpenDoor()
        {
            // 문 열림
            opCL.Text = "OPEN";
            opCL.Foreground = Brushes.IndianRed;

            // 문 열림 사운드 재생
            try
            {
                SoundPlayer player = new SoundPlayer("open.wav");
                player.Play(); // 비동기 재생 (UI 멈추지 않음)
            }
            catch (Exception ex)
            {
                MessageBox.Show("사운드 재생 오류: " + ex.Message);
            }

            await Task.Delay(3000);         // 문 열림 유지 시간

            // 문 닫힘
            opCL.Text = "CLOSE";
            opCL.Foreground = Brushes.ForestGreen;

            // 문 닫힘 사운드 재생
            try
            {
                SoundPlayer player = new SoundPlayer("close.wav");
                player.Play(); // 비동기 재생 (UI 멈추지 않음)
            }
            catch (Exception ex)
            {
                MessageBox.Show("사운드 재생 오류: " + ex.Message);
            }

            await Task.Delay(2000); // 닫힘 유지

            // 기본 공백으로 초기화
            opCL.Text = "        ";
        }

        // PLC 헬퍼
        private T WithFreshDriver<T>(Func<CommObject20, T> work)
        {
            if (string.IsNullOrWhiteSpace(_endpoint))
                throw new InvalidOperationException("엔드 포인트가 설정되지 않았습니다.");

            CommObject20 drv = null;
            try
            {
                drv = _factory.GetMLDPCommObject20(_endpoint);
                int c = drv.Connect("");
                if (c != 1) throw new Exception("작업용 연결 실패");
                return work(drv);
            }
            finally
            {
                try { drv?.Disconnect(); } catch { }
            }
        }
    }
}
