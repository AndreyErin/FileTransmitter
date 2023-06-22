using System;
using System.Collections.Generic;
using System.IO;
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
        private Socket _socketThisClient;
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
            _socketThisClient = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await _socketThisClient.ConnectAsync("192.168.0.34", 7070);

            byte[] data = Encoding.UTF8.GetBytes("имяФайл.txt@Запись в файле.^");

            await _socketThisClient.SendAsync(data, SocketFlags.None);
            
        }

        //запускаем сервер
        private void btnStartServer_Click(object sender, RoutedEventArgs e)
        {
            StartServerMode();            
        }

        //запускаем прослушивание потрта
        private async Task StartServerMode() 
        {
            _socketServerListener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint iPEndPoint = new IPEndPoint(IPAddress.Any, 7070);
            _socketServerListener.Bind(iPEndPoint);

            _socketServerListener.Listen();

            while (true) 
            {
                var clientConnect = await _socketServerListener.AcceptAsync();

                Task.Factory.StartNew(()=> ServerModeGetData(clientConnect));
            }
        } 

        private async Task ServerModeGetData(Socket clientSocket) 
        {
            List<byte> data = new List<byte>();
            byte[] oneChar = new byte[1];
            int countBytes = 0;
            string fileName = "";
            string[] nameAndBody;
            string fileBody = "";

            while (true) 
            {
                while (true) 
                {
                    countBytes = await clientSocket.ReceiveAsync(oneChar, SocketFlags.None);
                    if (countBytes == 0 || oneChar[0] == Convert.ToByte('^'))
                        break;
                    //заполняем буфер
                    data.Add(oneChar[0]);
                }
                 var formatString = Encoding.UTF8.GetString(data.ToArray());

                nameAndBody = formatString.Split('@');  

                fileName = nameAndBody[0];
                fileBody = nameAndBody[1];

                File.WriteAllText(fileName, fileBody);


                MessageBox.Show($"файл |{fileName}| получен");


                //очищаем буфер
                data.Clear();
            }


        }

        private async Task ServerModeSetData() 
        {

        }

        private async Task ClientModeGetData()
        {

        }

        private async Task ClientModeSetData()
        {

        }

        //получаем имя файла при перетаскивание
        private void lbxMain_Drop(object sender, DragEventArgs e)
        {
            string[] data = (string[])e.Data.GetData(DataFormats.FileDrop);
            MessageBox.Show(data[0]);
        }


    }
}
