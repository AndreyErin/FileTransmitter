using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
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

        //запускаем прослушивание порта
        private async Task StartServerMode() 
        {
            try
            {
                _socketServerListener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                IPEndPoint iPEndPoint = new IPEndPoint(IPAddress.Any, 7070);
                _socketServerListener.Bind(iPEndPoint);

                _socketServerListener.Listen();

                //запускаем прием данных
                _socket = await _socketServerListener.AcceptAsync();
                Task.Factory.StartNew(() => GetData());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }

        } 

        //получение данных
        private async Task GetData() 
        {
            List<byte> data = new List<byte>();
            byte[]? fileBody = null;
            byte[] oneChar = new byte[1];
            int countBytes = 0;
            string fileName = "";
            int fileLength;
            string[] dataInfo;
            string strBuff = "";

            try
            {
                while (true)
                {
                    //считываем имя файла
                    while (true)
                    {
                        countBytes = await _socket.ReceiveAsync(oneChar, SocketFlags.None);
                        if (countBytes == 0 || oneChar[0] == '*')
                            break;
                        //заполняем буфер
                        data.Add(oneChar[0]);
                    }

                    //переводим название файла в строковый формат
                    strBuff = Encoding.UTF8.GetString(data.ToArray());

                    dataInfo = strBuff.Split('|');

                    switch (dataInfo[0])
                    {
                        case "DIRS":
                            //создаем все папки
                            for (int i = 1; i < dataInfo.Length; i++)
                            {
                                Directory.CreateDirectory(@"Download\" + dataInfo[i]) ;
                            }
                            break;


                        default:
                            fileName = dataInfo[0];
                            fileLength = int.Parse(dataInfo[1]);

                            //если файл пустой
                            if (fileLength == 0) 
                            {
                                //создаем файл на диске
                                using FileStream fs = File.Create(@"Download\" + fileName);
                            }
                            //если файл содержит данные, то считываем их
                            else 
                            {
                                fileBody = new byte[fileLength];

                                //считываем содержимое файла
                                countBytes = await _socket.ReceiveAsync(fileBody, SocketFlags.None);

                                //записываем файл на диск 

                                File.WriteAllBytes(@"Download\" + fileName, fileBody) ;
                            }

                            break;
                    }

                    //очищаем буферы
                    data.Clear();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        //отправка данных(файл)
        private async Task SetDataFiles(List<string> filesFullName, string parentDir) 
        {
            byte[] data;
            FileInfo fileInfo;
            try
            {
                foreach (string fileName in filesFullName) 
                {
                    //обрезаем родительскую папку
                    string fileNameShort = fileName.Remove(0, parentDir.Length);

                    if (fileNameShort.Contains('|') || fileNameShort.Contains('*'))
                    {
                        MessageBox.Show("Имя файла {0} содержит некорректные символы (| или *)\nКопирование не возможно. Файл будет пропущен.", fileNameShort);
                    }
                    else
                    {
                        fileInfo = new FileInfo(fileName);

                        //если файл пустой
                        if (fileInfo.Length == 0)
                        {
                            data = Encoding.UTF8.GetBytes($"{fileNameShort}|0*");
                        }
                        else
                        {
                            byte[] bodyFile = File.ReadAllBytes(fileName);
                            byte[] dataCaption = Encoding.UTF8.GetBytes($"{fileNameShort}|{bodyFile.Length}*");

                            //объединяем все в один пакет
                            data = dataCaption.Concat(bodyFile).ToArray();
                        }                       

                        //отправляем пакет
                        await _socket.SendAsync(data, SocketFlags.None);
                    }
                }
                fileInfo = null;
            }
            catch (Exception ex)
            {
                MessageBox.Show("отправка данных - файлы\n" + ex.Message);
            }
        }
       
        //отправка данных(папки)
        private async Task SetDataDir(List<string> DirFullName, string parentDir)
        {
            StringBuilder allDirs = new StringBuilder();

            try
            {
                foreach (string dirName in DirFullName)
                {
                    //обрезаем родительскую папку
                    string dirNameShort = dirName.Remove(0, parentDir.Length);

                    if (dirNameShort.Contains('|') || dirNameShort.Contains('*'))
                    {
                        MessageBox.Show("Имя папки {0} содержит некорректные символы (| или *)\nКопирование не возможно. Папка будет пропущена.", dirNameShort);
                    }
                    else
                    {
                        allDirs.Append('|' + dirNameShort);
                    }
                }

                byte[] data = Encoding.UTF8.GetBytes($"DIRS{allDirs}*");

                //отправляем пакет
                await _socket.SendAsync(data, SocketFlags.None);
            }
            catch (Exception ex)
            {
                MessageBox.Show("отправка данных - папки\n" + ex.Message);
            }            
        }

        //получаем имя файла при перетаскивание
        private async void lbxMain_Drop(object sender, DragEventArgs e)
        {           
            List<string> allDirectories = new List<string>();
            List<string> allFiles = new List<string>();

            //список того, что перетащил пользователь
            string[] dropData = (string[])e.Data.GetData(DataFormats.FileDrop);
            //определяем папку из которой он ето перетащил
            DirectoryInfo? parentDir = Directory.GetParent(dropData[0]); ;

            try
            {              
                foreach(string drop in dropData) 
                {
                    //если это файл
                    if (File.Exists(drop))
                    {
                        allFiles.Add(drop);                       
                    }
                    else //если это папка
                    {
                        //добавляем саму папку
                        allDirectories.Add(drop);
                       
                        //проверяем содержит ли эта папка еще и вложенные папки
                        string[] localDirectories = Directory.GetDirectories(drop, "", SearchOption.AllDirectories);
                        foreach (string dir in localDirectories) 
                        {
                            //если содержит то и их добавляем
                            allDirectories.Add(dir);
                        }

                        //содержит ли папка файлы
                        string[] filesInDir = Directory.GetFiles(drop, "", SearchOption.AllDirectories);
                        foreach(string file in filesInDir) 
                        {
                            allFiles.Add(file);
                        }

                    }
                }
                //на этот момент мы имеем 2 полных списка файлов и папок

                //ждем, чтобы структура папок ушла по сети раньше файлов
                await SetDataDir(allDirectories, parentDir.FullName);
                
                await SetDataFiles(allFiles, parentDir.FullName);                              
            }
            catch (Exception ex)
            {
                MessageBox.Show("Подготовка списка файлов\n" + ex.Message);
            }
        }
    }
}
