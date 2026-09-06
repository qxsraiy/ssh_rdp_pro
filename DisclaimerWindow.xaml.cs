using System.Windows;
using System.Windows.Input;

namespace 云端管理
{
    /// <summary>
    /// 免责声明窗口。
    /// isFirstRun=true：首次启动强制显示，拒绝则退出程序；
    /// isFirstRun=false：从登录页链接打开，仅查看，按钮变为"关闭"。
    /// </summary>
    public partial class DisclaimerWindow : Window
    {
        private bool _isFirstRun;

        public DisclaimerWindow(bool isFirstRun = true)
        {
            InitializeComponent();
            _isFirstRun = isFirstRun;
            if (!_isFirstRun)
            {
                BtnDecline.Content = "关 闭";
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void Agree_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void Decline_Click(object sender, RoutedEventArgs e)
        {
            if (_isFirstRun)
            {
                // 首次运行拒绝：直接退出程序
                Application.Current.Shutdown();
            }
            else
            {
                DialogResult = false;
            }
        }
    }
}