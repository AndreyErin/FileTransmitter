using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Media;
using WinForms = System.Windows.Forms;

namespace FileTransmitter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        Mutex mutex;
        //ключ для мьютикса(случайные символы)
        private static string appGuid = "cfffff-ffff-45c5-aaaa-d693faa6e7b9";


        WinForms.NotifyIcon notifyIcon;

        private Config? config = new Config();

        private string _directory;
        private string _ipForConnect = "";
        private int _port = 0;

        System.Timers.Timer _timerCheckConnect = new System.Timers.Timer(1000);

        private CancellationTokenSource cts;
        private CancellationToken token;

        private List<PathNames> allFiles = new List<PathNames>();
        private Socket _socket;
        private Socket _socketServerListener;
        private bool _serverOn = false;
        private int _countFilesForGet = 0;//подсчет файлов которые надо принять в принимающей программе
        private int _countFilesForSet = 0;//подсчет файлов которые надо отправить, передающей программе
        private static int _errorDataGet = 0;//подсчет непонятных пакетов(будут в случае сбоя при получение файла)
        private static int _errorDataSet = 0;

        public PathNames fileNameStruct = new PathNames();

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            //запрет запуска 2го экземпляра приложения
            mutex = new Mutex(false, "Global\\" + appGuid);
            if (!mutex.WaitOne(0, false))
            {
                WinMain.Visibility = Visibility.Hidden;
                MessageBox.Show("Приложение уже запущено.", "Уведомление", MessageBoxButton.OK, MessageBoxImage.Information);
                WinMain.Close();
            }

            try
            {
                //вынаем настройки из файла

                config = JsonSerializer.Deserialize<Config>(File.ReadAllText("config.json"));

            }
            catch (Exception ex)
            {
                //применяем стандартные настройки
                config.Port = 7070;
                config.IpAddress = "192.168.0.34";
                config.DirectoryForSave = Directory.GetCurrentDirectory() + "\\Download";

                //сохранение конфигурации в файл
                string dataConfig = JsonSerializer.Serialize(config);
                File.WriteAllText("config.json", dataConfig);

                MessageBox.Show("Не удалось загрузить конфигурацию\nБудут применены настройки по умолчанию\n" + ex.Message, "Не сильно страшная ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            _ipForConnect = config.IpAddress;
            _port = config.Port;
            _directory = config.DirectoryForSave;

            txtIP.Text = _ipForConnect;
            txtPort.Text = _port.ToString();

            IconInTrey();

            _timerCheckConnect.Elapsed += _timerCheckConnect_Elapsed;
        }

        //трей
        private void IconInTrey() 
        {
            notifyIcon = new WinForms.NotifyIcon() { Icon = new System.Drawing.Icon("app.ico")};
            notifyIcon.Text = "Программа для передачи файлов";
            notifyIcon.Visible = true;
            notifyIcon.DoubleClick += NotifyIcon_DoubleClick;
        }

        // 2й клик по трею
        private void NotifyIcon_DoubleClick(object? sender, EventArgs e)
        {
            switch (WinMain.Visibility)
            {
                case Visibility.Visible:
                    WinMain.Visibility = Visibility.Hidden;
                    break;
                case Visibility.Hidden:
                    WinMain.Visibility = Visibility.Visible;
                    break;
            }
        }

        //таймер проверяет есть ли соединение (не отвалилась ли программа на том конце)
        private void _timerCheckConnect_Elapsed(object? sender, ElapsedEventArgs e)
        {
            bool connectTrue = false;

            IPGlobalProperties iPGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
            TcpConnectionInformation[] tcpConnectionInformation = iPGlobalProperties.GetActiveTcpConnections();

            foreach (TcpConnectionInformation connect in tcpConnectionInformation)
            {
                //если в списке активных подключений есть наше и оно активно, то значит все норм
                if (connect.RemoteEndPoint.Equals(_socket.RemoteEndPoint) &&
                     connect.LocalEndPoint.Equals(_socket.LocalEndPoint))
                {
                    //соединение есть
                    connectTrue = true;
                }
            }

            //если соединения нет
            if (!connectTrue)
            {
                //если программа запущенна в режиме сервера то останавливаем сервер
                if (_serverOn)
                {
                    Dispatcher.Invoke(() => StopServer());

                }
                //если в режиме клиента то останавливаем клиент
                else
                {
                    Dispatcher.Invoke(() => StopClient());
                }
            }
        }

        //подключаемся к серверу
        private async void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            switch (btnConnect.Content)
            {
                case "Подключиться к серверу":
                    try
                    {
                        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        //создаем и запускаем задачу
                        var myTask = _socket.ConnectAsync(_ipForConnect, _port);
                        //стоим и ждем ее завершения
                        await myTask;

                        //если подключение установлено
                        if (myTask.IsCompletedSuccessfully)
                        {
                            //запускаем прием данных, используем токен отмены
                            cts = new CancellationTokenSource();
                            token = cts.Token;

                            //запускаем прием данных через токен
                            Task task = new Task(() => GetData(), token);
                            task.Start();

                            //разрешаем перетаскивание
                            lblMain.AllowDrop = true;
                            lblMain.Background = Brushes.Ivory;
                            lblMain.Content = "Тащи сюда свои файлы";
                            btnConnect.Content = "Отключиться от сервера";
                            btnConnect.Margin = new Thickness(1,1,1,1);
                            btnConnect.Background = Brushes.GreenYellow;
                            //выключаем кнопку сервера
                            btnStartServer.Visibility = Visibility.Hidden;

                            _timerCheckConnect.Start();
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Не удалось подключиться к серверу.\n" + ex.Message, "Уведомление", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    break;

                case "Отключиться от сервера":
                    if (lblMain.Content.ToString() == "Идет передача файлов" || lblMain.Content.ToString() == "Идет получение файлов")
                    {
                        MessageBox.Show("Там файлы передаются, как-бэ!\nПодожди по братски.", "Ну вот надо оно тебе?", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    StopClient();
                    break;
            }
        }

        //запускаем сервер
        private void btnStartServer_Click(object sender, RoutedEventArgs e)
        {
            switch (btnStartServer.Content)
            {
                case "Запустить сервер":
                    _serverOn = true;
                    StartServerMode();

                    //выключаем кнопку клиента
                    btnConnect.Visibility = Visibility.Hidden;

                    btnStartServer.Content = "Остановить сервер";
                    btnStartServer.Margin = new Thickness(1, 1, 1, 1);
                    btnStartServer.Background = Brushes.Yellow;
                    break;

                case "Остановить сервер":
                    if (lblMain.Content.ToString() == "Идет передача файлов" || lblMain.Content.ToString() == "Идет получение файлов")
                    {
                        MessageBox.Show("Там файлы передаются, как-бэ!\nПодожди по братски.", "Ну вот надо оно тебе?", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    StopServer();
                    break;
            }
                   
        }

        private void StopClient() 
        {
            _timerCheckConnect.Stop();

            btnStartServer.Visibility = Visibility.Visible;

            //останавливаем функцию приема данных с помощью токена
            cts.Cancel();

            _socket.Shutdown(SocketShutdown.Both);
            _socket.Close();
            _socket.Dispose();

            //запрещаем перетаскивание
            lblMain.AllowDrop = false;
            lblMain.Background = Brushes.MistyRose;
            lblMain.Content = "Нет соединения";
            btnConnect.Content = "Подключиться к серверу";
            btnConnect.Margin = new Thickness(1, 1, 235, 1);
            btnConnect.Background = Brushes.SeaShell;
        }

        private void StopServer() 
        {
            _timerCheckConnect.Stop();

            //если подключение клиента было
            if (_socket != null)
            {
                //останавливаем функцию приема данных
                cts.Cancel();
                _socket.Shutdown(SocketShutdown.Both);
                _socket.Close();
                _socket.Dispose();
            }

            //останавливаем сокет прослушки подключения клиентов
            //_socketServerListener.Shutdown(SocketShutdown.Both);
            _socketServerListener.Close();
            _socketServerListener.Dispose();

            //запрещаем перетаскивание
            lblMain.AllowDrop = false;
            lblMain.Background = Brushes.MistyRose;
            lblMain.Content = "Нет соединения";

            //включаем кнопку клиента
            btnConnect.Visibility = Visibility.Visible;

            btnStartServer.Content = "Запустить сервер";
            btnStartServer.Margin = new Thickness(250, 1, 1, 1);
            btnStartServer.Background = Brushes.SeaShell;
            _serverOn = false;
        }

        //запускаем прослушивание порта
        private async Task StartServerMode() 
        {

            //создаем токен отмены для функции приема данных
            cts = new CancellationTokenSource();
            token = cts.Token;

            _socketServerListener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint iPEndPoint = new IPEndPoint(IPAddress.Any, _port);
            _socketServerListener.Bind(iPEndPoint);

            _socketServerListener.Listen(0);

            //запускаем ожидания подключения
            _socket = await _socketServerListener.AcceptAsync();
            //если мы прервали прослушку
            if (_socket == null) return;


            //запускаем прием данных через токен
            Task task = new Task(() => GetData(), token);
            task.Start();

            //разрешаем перетаскивание
            lblMain.AllowDrop = true;
            lblMain.Background = Brushes.Ivory;
            lblMain.Content = "Тащи сюда свои файлы";

            //если  подключение состоялось то запускаем таймер проверки соединения
            _timerCheckConnect.Start();  
        }

        //получаем имя файла при перетаскивание
        private async void lblMain_Drop(object sender, DragEventArgs e)
        {           
            List<PathNames> allDirectories = new List<PathNames>();           
            DirectoryInfo parentDir;
            DirectoryInfo? grantParentDir = null;
            int fixPath = 0;
            string nameShort = "";

           //очищаем список файлов
            allFiles.Clear();

            //обнуляем счетчик
            _countFilesForSet = 0;
            _errorDataSet = 0;

            try
            {
                //список того, что перетащил пользователь
                string[] dropData = (string[])e.Data.GetData(DataFormats.FileDrop);

                foreach (string drop in dropData)
                {
                    int fixFullName = 1;

                    //определяем папку из которой он ето перетащил
                    parentDir = Directory.GetParent(drop);
                    grantParentDir = Directory.GetParent(parentDir.FullName);
                    

                    if (grantParentDir == null)
                        fixFullName = 0;


                    // fixFullName - количество символов, которые будем обрезать(+2 чтобы убрать /)
                    fixPath = (grantParentDir != null ? grantParentDir.FullName.Length : 0) + (parentDir != null ? parentDir.Name.Length : 0) + fixFullName;

                    //если это файл
                    if (File.Exists(drop))
                    {
                        nameShort = drop.Remove(0, fixPath);

                        allFiles.Add(new PathNames() { NameFull = drop, NameShort = nameShort });
                    }
                    else //если это папка
                    {
                        nameShort = drop.Remove(0, fixPath);
                        //добавляем саму папку
                        allDirectories.Add(new PathNames() { NameFull = drop, NameShort = nameShort });

                        //проверяем содержит ли эта папка еще и вложенные папки
                        string[] localDirectories = Directory.GetDirectories(drop, "", SearchOption.AllDirectories);
                        foreach (string dir in localDirectories)
                        {
                            nameShort = dir.Remove(0, fixPath);
                            //если содержит то и их добавляем
                            allDirectories.Add(new PathNames() { NameFull = dir, NameShort = nameShort });
                        }

                        //содержит ли папка файлы
                        string[] filesInDir = Directory.GetFiles(drop, "", SearchOption.AllDirectories);
                        foreach (string file in filesInDir)
                        {
                            nameShort = file.Remove(0, fixPath);
                            allFiles.Add(new PathNames() { NameFull = file, NameShort = nameShort });

                            
                        }

                    }
                }
                //на этот момент мы имеем 2 полных списка файлов и папок
                //с их полными и относительными путями

                //блокируем перетаскивание Drag & Drop
                lblMain.Content = "Идет передача файлов";
                lblMain.AllowDrop = false;
                lblMain.Background = Brushes.Lavender;
                //WinMain.Title = "Отправка данных";

                //если папки есть
                if (allDirectories.Count > 0)
                {
                    //ждем, чтобы структура папок ушла по сети раньше файлов
                    await SetDataDir(allDirectories);
                }

                //если файлы есть
                if (allFiles.Count > 0)
                {

                    //прогресс бар
                    prgAllFiles.Minimum = 0;
                    prgAllFiles.Maximum = allFiles.Count;
                    prgAllFiles.Value = 0;
                    prgAllFiles.Visibility = Visibility.Visible;
                    prgFile.Visibility = Visibility.Visible;
                    prgFile.Minimum = 0;

                    _countFilesForSet = allFiles.Count;
                    lblAllFiles.Content = $"Отправлено файлов: {0} из {allFiles.Count}.";
                        lblAllFiles.InvalidateVisual();
                    

                    //отправляем количество файлов принимающей программе
                    await SetStatistic(_countFilesForSet);
                    //отправляем первый файл, если он есть
                    Task.Run(()=> SetDataFiles());
                }
                else 
                {
                    lblMain.AllowDrop = true;
                    lblMain.Background = Brushes.Ivory;
                    WinMain.Title = $"Все данные отправлены(Были только папки)";
                    //отправляем сообщение о том, что передача данных окончена
                    await _socket.SendAsync(Encoding.UTF8.GetBytes("TRANSFEREND|0|0*"), SocketFlags.None);
                }

            }
            catch (Exception ex)
            {
                MessageBox.Show("Подготовка списка файлов\n" + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnOptionsIP_Click(object sender, RoutedEventArgs e)
        {
            switch (btnOptionsIP.Content)
            {
                case "Адрес сервера":
                    txtIP.IsEnabled = true;
                    btnOptionsIP.Content = "Применить";
                    break;

                case "Применить":
                    config.IpAddress = txtIP.Text;
                    _ipForConnect = config.IpAddress;
                    txtIP.IsEnabled = false;
                    btnOptionsIP.Content = "Адрес сервера";

                    //сохранение конфигурации в файл
                    SaveConfig();
                    break;
            }
        }

        private void btnOptionsPort_Click(object sender, RoutedEventArgs e)
        {
            switch (btnOptionsPort.Content)
            {
                case "Порт":
                    txtPort.IsEnabled = true;
                    btnOptionsPort.Content = "Применить";
                    break;

                case "Применить":
                    config.Port = int.Parse(txtPort.Text);
                    _port = config.Port;
                    txtPort.IsEnabled = false;
                    btnOptionsPort.Content = "Порт";

                    //сохранение конфигурации в файл
                    SaveConfig();
                    break;
            }
        }

        private void btnOptionsDirectory_Click(object sender, RoutedEventArgs e)
        {
            WinForms.FolderBrowserDialog folderDialog = new WinForms.FolderBrowserDialog();
            folderDialog.InitialDirectory = Directory.GetCurrentDirectory();

            WinForms.DialogResult dialogResult = folderDialog.ShowDialog();

            if (dialogResult == WinForms.DialogResult.OK)
            {
                config.DirectoryForSave = folderDialog.SelectedPath;

                //сохранение конфигурации в файл
                SaveConfig();
            }
        }

        private void SaveConfig()
        {
            //сохранение конфигурации в файл
            string dataConfig = JsonSerializer.Serialize(config);
            File.WriteAllText("config.json", dataConfig);

        }

        private void btnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            //если папки для сохранения не существует, то создаем ее
            if (!Directory.Exists(config.DirectoryForSave))
                Directory.CreateDirectory(config.DirectoryForSave);

            System.Diagnostics.Process.Start( new System.Diagnostics.ProcessStartInfo(config.DirectoryForSave) { UseShellExecute = true});
        }

        //прям закрытие-закрытие)
        private void WinMain_Closed(object sender, EventArgs e)
        {
            //сохранение конфигурации в файл
            SaveConfig();
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            mutex.Dispose();
        }

        //перед закрытием
        private void WinMain_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (lblMain.Content.ToString() == "Идет передача файлов" || lblMain.Content.ToString() == "Идет получение файлов")
            {
                WinMain.Visibility = Visibility.Hidden;
                //MessageBox.Show("Там файлы передаются, как-бэ!\nПодожди по братски.", "Ну вот надо оно тебе?", MessageBoxButton.OK, MessageBoxImage.Information);
                e.Cancel = true;
            }
        }
    }
}
