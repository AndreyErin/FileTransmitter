using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;



namespace FileTransmitter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Socket _socket;
        private Socket _socketServerListener;
        private bool _serverOn;
        private DirectoryInfo _directory;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            //разрешаем перетаскивание
            lbxMain.AllowDrop = true;
        }

        //подключаемся к серверу
        private async void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            //выключаем кнопку сервера
            btnStartServer.Visibility = Visibility.Hidden;

            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await _socket.ConnectAsync("192.168.0.34", 7070);

            //запускаем прием данных
            Task.Factory.StartNew(() => GetData());
        }

        //запускаем сервер
        private void btnStartServer_Click(object sender, RoutedEventArgs e)
        {
            //выключаем кнопку клиента
            btnConnect.Visibility = Visibility.Hidden; 

            StartServerMode();            
        }

        //запускаем прослушивание потрта
        private async Task StartServerMode() 
        {
            _socketServerListener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint iPEndPoint = new IPEndPoint(IPAddress.Any, 7070);
            _socketServerListener.Bind(iPEndPoint);

            _socketServerListener.Listen();

            //запускаем прием данных
            _socket = await _socketServerListener.AcceptAsync();
            Task.Factory.StartNew(()=> GetData());            
        } 

        //получение данных
        private async Task GetData() 
        {
            List<byte> fileNameBytes = new List<byte>();
            byte[] fileBody;
            byte[] oneChar = new byte[1];
            int countBytes = 0;
            string fileName = "";
            int fileLength;
            string[] fileInfo;
            string strBuff;

            while (true) 
            {
                //считываем имя файла
                while (true) 
                {
                    countBytes = await _socket.ReceiveAsync(oneChar, SocketFlags.None);
                    if (countBytes == 0 || oneChar[0] == '^')
                        break;
                    //заполняем буфер
                    fileNameBytes.Add(oneChar[0]);
                }

                //переводим название файла в строковый формат
                strBuff = Encoding.UTF8.GetString(fileNameBytes.ToArray());

                fileInfo = strBuff.Split('@');

                fileName = fileInfo[0];
                fileLength = int.Parse(fileInfo[1]);
               
                fileBody = new byte[fileLength];
                //считываем содержимое файла
                countBytes = await _socket.ReceiveAsync(fileBody, SocketFlags.None);

                MessageBox.Show($"файл |{fileName}| получен");

                try
                {
                    //записываем файл на диск 
                    File.WriteAllBytes(fileName, fileBody);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("запись файла на диск\n" + ex);
                }
                
                //очищаем буферы
                fileNameBytes.Clear();                
            }
        }

        //отправка данных
        private async Task SetData(string fileFullName) 
        {
            string fileName = Path.GetFileName(fileFullName);            
            byte[] bodyFile = File.ReadAllBytes(fileFullName);           
            byte[] dataCanption = Encoding.UTF8.GetBytes($"{fileName}@{bodyFile.Length}^") ;

            //отправляем имя файла и его длинну
            await _socket.SendAsync(dataCanption.ToArray(), SocketFlags.None);
            //отправляем сам файл
            await _socket.SendAsync(bodyFile.ToArray(), SocketFlags.None);
        }

        //получаем имя файла при перетаскивание
        private void lbxMain_Drop(object sender, DragEventArgs e)
        {
            string[] fileFullName = (string[])e.Data.GetData(DataFormats.FileDrop);

            foreach (string fileName in fileFullName)
            {
                SetData(fileName);
            }
        }


    }
}
