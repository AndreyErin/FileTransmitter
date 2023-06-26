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
            byte[]? fileBody = null;
            byte[] oneChar = new byte[1];
            int countBytes = 0;
            string fileName = "";
            int fileLength;
            string[] fileInfo;
            string strBuff = "";

            while (true) 
            {
                //считываем имя файла
                while (true) 
                {
                    countBytes = await _socket.ReceiveAsync(oneChar, SocketFlags.None);
                    if (countBytes == 0 || oneChar[0] == '*')
                        break;
                    //заполняем буфер
                    fileNameBytes.Add(oneChar[0]);
                }

                try
                {
                    //переводим название файла в строковый формат
                    strBuff = Encoding.UTF8.GetString(fileNameBytes.ToArray());

                    fileInfo = strBuff.Split('|');

                    fileName = fileInfo[0];
                    fileLength = int.Parse(fileInfo[1]);


                    //------------------------------------------------------------------------
                    //если файл или папка пустые
                    if (fileLength == 0) 
                    {
                        countBytes = await _socket.ReceiveAsync(oneChar, SocketFlags.None);
                        string isFile = Encoding.UTF8.GetString(oneChar.ToArray());

                        switch (isFile)
                        {
                            case "1":

                                string directoryName = Path.GetDirectoryName(fileName);
                                //если папка существует то записываем файл
                                if (Directory.Exists(@"Download\" + directoryName))
                                {
                                    //записываем файл на диск 
                                    using (File.Create(@"Download\" + fileName)) ;                              
                                }
                                //если папки нет, то создаем ее изаписываем файл
                                else
                                {
                                    Directory.CreateDirectory(@"Download\" + directoryName);
                                    //записываем файл на диск 
                                    using (File.Create(@"Download\" + fileName)) ;
                                }
                                break;

                            case "0":
                                Directory.CreateDirectory(@"Download\" + fileName);
                                break;
                        }

                        return;
                    }
                    //------------------------------------------------------------------------





                    fileBody = new byte[fileLength];

                    //считываем содержимое файла
                    countBytes = await _socket.ReceiveAsync(fileBody, SocketFlags.None);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"{strBuff}\n " + ex);
                }
                          
                //если файл не находится во вложенной папке то просто записываем его
                string isHavePath = Path.GetFileName(fileName);
                if (isHavePath == fileName)
                {
                    //записываем файл на диск 
                    File.WriteAllBytes(@"Download\" + fileName, fileBody);
                }
                //если файл находится во вложенной папке
                else
                {
                    string directoryName = Path.GetDirectoryName(fileName);
                    //если папка существует то записываем файл
                    if (Directory.Exists(@"Download\" + directoryName))
                    {
                        //записываем файл на диск 
                        File.WriteAllBytes(@"Download\" + fileName, fileBody);
                    }
                    //если папки нет, то создаем ее изаписываем файл
                    else 
                    {
                        Directory.CreateDirectory(@"Download\" + directoryName);
                        //записываем файл на диск 
                        File.WriteAllBytes(@"Download\" + fileName, fileBody);
                    }                    
                }
                //очищаем буферы
                fileNameBytes.Clear();
            }               

        }

        //отправка данных(файл)
        private async Task SetData(string fileFullName) 
        {
            try
            {
                string fileName = Path.GetFileName(fileFullName);

                byte[] bodyFile = File.ReadAllBytes(fileFullName);
                byte[] dataCanption = Encoding.UTF8.GetBytes($"{fileName}|{bodyFile.Length}*");

                //объединяем все в один пакет
                byte[] data = dataCanption.Concat(bodyFile).ToArray();

                //отправляем паккет
                await _socket.SendAsync(data, SocketFlags.None);
            }
            catch (Exception ex)
            {
                MessageBox.Show("отправка данных - файлы\n" + ex.Message);
            }
        }
       
        //отправка данных(папки)
        private async Task SetData(string fileFullName, string perentDir)
        {
            try
            {
                string fileName = fileFullName.Remove(0, perentDir.Length);

                byte[] bodyFile = File.ReadAllBytes(fileFullName);
                byte[] dataCanption = Encoding.UTF8.GetBytes($"{fileName}|{bodyFile.Length}*");

                //объединяем все в один пакет
                byte[] data = dataCanption.Concat(bodyFile).ToArray();

                //отправляем паккет
                await _socket.SendAsync(data, SocketFlags.None);
            }
            catch (Exception ex)
            {
                MessageBox.Show("отправка данных - папки\n" + ex.Message);
            }            
        }

        //отправка данных(пустой файл(папка))
        private async Task SetData(string FullName, bool isFile)
        {
            byte[] data;
            string fileName = Path.GetFileName(FullName);

            try
            {
                switch (isFile)
                {
                    //это файл
                    case true:
                        data = Encoding.UTF8.GetBytes($"{fileName}|0*1");
                        break;
                    //это папка
                    case false:
                        data = Encoding.UTF8.GetBytes($"{fileName}|0*0");
                        break;
                }
              
                //отправляем паккет
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
            try
            {
                string[] fileFullName = (string[])e.Data.GetData(DataFormats.FileDrop);

                foreach (string fileName in fileFullName)
                {
                    //если это файл то передаем его для отправки по сети
                    if (File.Exists(fileName))
                    {
                        if (fileName.Contains('|') || fileName.Contains('*'))
                        {
                            MessageBox.Show("Имя файла {0} содержит некорректные символы (| или *)\nКопирование не возможно. Файл будет пропущен.", fileName);
                            return;
                        }
                        

                         FileInfo fileInfo = new FileInfo(fileName);

                            //файл пустой или нет
                            if (fileInfo.Length == 0)
                                await SetData(fileName, true);
                            else
                                await SetData(fileName);

                           
                    }
                    //если это папка
                    else
                    {

                        if (fileName.Contains('|') || fileName.Contains('*'))
                        {
                            MessageBox.Show("Имя папки/файла {0} содержит некорректные символы (| или *)\nКопирование не возможно. Папка/файл будет пропущен(а).", fileName);
                            return;
                        }                      

                        string[] filesInDir = Directory.GetFiles(fileName, "", SearchOption.AllDirectories);
                        string[] DirsInDir = Directory.GetDirectories(fileName);
                        

                        //eсли папка пустая
                        if (filesInDir.Length == 0 && DirsInDir.Length == 0)
                        {
                            await SetData(fileName, false);
                            return;
                        }

                        //MessageBox.Show(filesInDir.Length.ToString());

                        string perentDir = Directory.GetParent(fileName).FullName;

                        foreach (string file in filesInDir)
                        {
                            if (file.Contains('|') || file.Contains('*'))
                            {
                                MessageBox.Show("Имя файла {0} содержит некорректные символы (| или *)\nКопирование не возможно. Файл будет пропущен.", fileName);
                                return;
                            }

                            FileInfo fileInfo = new FileInfo(file);

                            //файл пустой или нет
                            if (fileInfo.Length == 0)
                                await SetData(file, true);
                            else
                                await SetData(file, perentDir);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Подготовка списка файлов\n" + ex.Message);
            }
        }
    }
}
