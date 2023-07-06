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
using System.Windows.Media;

namespace FileTransmitter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private List<PathNames> allFiles = new List<PathNames>();
        private Socket _socket;
        private Socket _socketServerListener;
        private bool _serverOn;
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
            byte[] fileBody;
            byte[] oneChar = new byte[1];
            int resultBytes = 0;
            string fileName = "";
            long fileLength;
            string[] dataInfo;
            string strBuff = "";
            char[] charArray;

            while (true)
            {
                //очищаем буферы
                data.Clear();

                //считываем имя файла
                while (true)
                {
                    resultBytes = await _socket.ReceiveAsync(oneChar, SocketFlags.None);
                    if (resultBytes == 0 || oneChar[0] == '*')
                        break;
                    //заполняем буфер
                    data.Add(oneChar[0]);
                }

                charArray = new char[data.Count];

                //переводим название файла в строковый формат
                int resultToConvert = Encoding.UTF8.GetDecoder().GetChars(data.ToArray(), charArray, true);//.GetString(data.ToArray());

                if (resultToConvert != data.Count)
                {
                    //MessageBox.Show("Чета тута аще не камильфо");
                }

                //обрезаем массив до реально считанных символов
                Array.Resize(ref charArray, resultToConvert);

                strBuff = new string(charArray);
               
                dataInfo = strBuff.Split('|');

               
                switch (dataInfo[0])
                {
                    case "DIRS":
                        //создаем все папки
                        for (int i = 1; i < dataInfo.Length; i++)
                        {
                            Directory.CreateDirectory(@"Download\" + dataInfo[i]);
                        }
                        break;

                    case "STATISTIC":

                        //обнуляем счетчик
                        _errorDataGet = 0;
                        _countFilesForGet = int.Parse(dataInfo[1]);
                        Action action = () =>
                        {
                            //блокируем перетаскивание Drag & Drop
                            lbxMain.AllowDrop = false;
                            lbxMain.Background = Brushes.Red;
                            WinMain.Title = "Получение данных";
                        };
                        Dispatcher.Invoke(action);
                        break;

                    case "TRANSFEREND":
                        Action action2 = () =>
                        {
                            //Разблокируем перетаскивание Drag & Drop
                            lbxMain.AllowDrop = true;
                            lbxMain.Background = Brushes.Ivory;
                            WinMain.Title = $"Все данные получены. Ошибок: {_errorDataGet}";                            
                        };
                        Dispatcher.Invoke(action2);
                        break;


                    //---------/это сообщение получает передающая сторона
                    case "FILESERVISED":
                        //если файлы для отправки еще есть
                        if (_countFilesForSet > 0)
                        {
                            await SetDataFiles();//отправляем следующий файл
                        }                      
                        //если все данные отправлены то разблокируем перетаскивание Drag & Drop
                        else
                        {
                            Action action147 = () =>
                            {
                                lbxMain.AllowDrop = true;
                                lbxMain.Background = Brushes.Ivory;
                                WinMain.Title = $"Все данные отправлены, Ошибок: {_errorDataSet}";
                            };
                            Dispatcher.Invoke(action147);

                            //отправляем сообщение о том, что передача данных окончена
                            await _socket.SendAsync(Encoding.UTF8.GetBytes("TRANSFEREND|0|0*"), SocketFlags.None);
                        }
                        break;
                    
                    case "ERROR":
                        //получена ошибка
                        _errorDataSet++;
                        
                        break;
                    //---------/это сообщение получает передающая сторона




                    case "FILEZERO":
                        fileName = dataInfo[1];

                        try
                        {
                            //создаем файл на диске
                            using FileStream fs = File.Create(@"Download\" + fileName);

                            Dispatcher.Invoke(()=> WinMain.Title = fileName);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Ошибка записи пустого файла" + ex.Message);
                        }

                        //отправляем сообщение о том, что очередной файл принят и обработан
                        await _socket.SendAsync(Encoding.UTF8.GetBytes("FILESERVISED|0|0*"), SocketFlags.None);
                        break;

                    case "FILE":
                        fileName = dataInfo[1];

                        bool canParse = long.TryParse(dataInfo[2], out long result);
                        if (canParse)
                        {
                            fileLength = result;
                        }
                        else
                        {
                            //отправляем сообщение об ошибке отправляющей программе
                            await _socket.SendAsync(Encoding.UTF8.GetBytes("ERROR|0|0*"), SocketFlags.None);
                            //_countFilesForGet--;
                            _errorDataGet++;
                            break;
                        }

                        try
                        {
                            
                            FileInfo fileInfo = new FileInfo(@"Download\" + fileName);
                            BinaryWriter binaryWriter = new BinaryWriter(fileInfo.OpenWrite());
                            
                            //создаем буфер 2 MB
                            fileBody = new byte[2097152];

                            long countBytes = 0;
                            while (true)
                            {
                                //считываем 2 метра из потока
                                resultBytes = await _socket.ReceiveAsync(fileBody, SocketFlags.Partial);

                                countBytes += resultBytes;

                                //записываем считанные байты в файл
                                binaryWriter.Write(fileBody, 0, resultBytes);
                                
                                //если мы считали весь файл в поток, то выходим из цикла
                                if (countBytes == fileLength) 
                                    break;
                            }

                            //закрываем поток записи и высвобождаем его память
                            binaryWriter.Close();

                            Dispatcher.Invoke(() => WinMain.Title = fileName);
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(()=> MessageBox.Show("Ошибка записи файла\n" + ex.Message));

                            _errorDataGet++;
                            await _socket.SendAsync(Encoding.UTF8.GetBytes("ERROR|0|0*"), SocketFlags.None);
                        }

                        //отправляем сообщение о том, что очередной файл принят и обработан
                        await _socket.SendAsync(Encoding.UTF8.GetBytes("FILESERVISED|0|0*"), SocketFlags.None);
                        break;
                

                    default:
                        _errorDataGet++;// отмечаем неизвестный пакет = сбой при получение файла
                        //отправляем сообщение об ошибке отправляющей программе
                        await _socket.SendAsync(Encoding.UTF8.GetBytes("ERROR|0|0*"), SocketFlags.None);                                            
                        break;
                }

                _countFilesForGet--;

                //очищаем буферы
                data.Clear();             
            }
        }
       
        //отправка данных(файл)
        private async Task SetDataFiles() 
        {
            byte[] dataCaption;
            FileInfo fileInfo;
            try
            {
                fileNameStruct = allFiles[--_countFilesForSet]; //поправка на индекс

                if (fileNameStruct.NameShort.Contains('|') || fileNameStruct.NameShort.Contains('*'))
                {
                    MessageBox.Show("Имя файла {0} содержит некорректные символы (| или *)\nКопирование не возможно. Файл будет пропущен.", fileNameStruct.NameShort);
                }
                else
                {
                    fileInfo = new FileInfo(fileNameStruct.NameFull);

                    //если файл пустой
                    if (fileInfo.Length == 0)
                    {
                        dataCaption = Encoding.UTF8.GetBytes($"FILEZERO|{fileNameStruct.NameShort}|0*");
                        //отправляем пакет
                        await _socket.SendAsync(dataCaption, SocketFlags.None);
                    }
                    else
                    {                   
                        dataCaption = Encoding.UTF8.GetBytes($"FILE|{fileNameStruct.NameShort}|{fileInfo.Length}*");
                        //отправляем заголовок
                        await _socket.SendAsync(dataCaption, SocketFlags.None);

                        //создаем поток для чтения файла
                        using (BinaryReader binaryReader = new BinaryReader(fileInfo.OpenRead()))
                        {
                            //буфер для чтения части файла
                            byte[] dataBodyFile;
                            long countBytes = 0;
                            int resultBytes = 0;
                            dataBodyFile = new byte[2097152];

                            while (true)
                            {
                                dataBodyFile = binaryReader.ReadBytes(dataBodyFile.Length);
                                if (dataBodyFile.Length == 0) break;

                                resultBytes = await _socket.SendAsync(dataBodyFile, SocketFlags.Partial);

                                countBytes += resultBytes;

                                //если данных в потоке больше нет(достигли конца потока), то выходим из цикла
                                if (binaryReader.BaseStream.Length == countBytes) break;
                            }
                        }
                    }

                    Dispatcher.Invoke(()=> WinMain.Title = fileNameStruct.NameShort);
                }               
                
            }
            catch (Exception ex)
            {
                MessageBox.Show("отправка данных - файлы\n" + ex.Message);
            }
        }
       
        //отправка данных(папки/структуры всех папок)
        private async Task SetDataDir(List<PathNames> DirName)
        {
            StringBuilder allDirs = new StringBuilder();

            try
            {
                foreach (PathNames dirName in DirName)
                {
                    if (dirName.NameShort.Contains('|') || dirName.NameShort.Contains('*'))
                    {
                        MessageBox.Show("Имя папки {0} содержит некорректные символы (| или *)\nКопирование не возможно. Папка будет пропущена.", dirName.NameShort);
                    }
                    else
                    {
                        allDirs.Append('|' + dirName.NameShort);
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

        //отправка статистики
        private async Task SetStatistic(int countFiles) 
        {
            byte[] data = Encoding.UTF8.GetBytes($"STATISTIC|{countFiles}|0*");
            //отправляем пакет
            await _socket.SendAsync(data, SocketFlags.None);
        }

        //получаем имя файла при перетаскивание
        private async void lbxMain_Drop(object sender, DragEventArgs e)
        {           
            List<PathNames> allDirectories = new List<PathNames>();           
            DirectoryInfo parentDir;
            DirectoryInfo grantParentDir;
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

                    //определяем папку из которой он ето перетащил
                    parentDir = Directory.GetParent(drop);
                    grantParentDir = Directory.GetParent(parentDir.FullName);

                    //количество символов, которые будем обрезать(+2 чтобы убрать /)
                    fixPath = grantParentDir.FullName.Length + parentDir.Name.Length + 2;

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
                lbxMain.AllowDrop = false;
                lbxMain.Background = Brushes.Red;
                WinMain.Title = "Отправка данных";

                //если папки есть
                if (allDirectories.Count > 0)
                {
                    //ждем, чтобы структура папок ушла по сети раньше файлов
                    await SetDataDir(allDirectories);
                }

                //если файлы есть
                if (allFiles.Count > 0)
                {
                    _countFilesForSet = allFiles.Count;
                    //отправляем количество файлов принимающей программе
                    await SetStatistic(_countFilesForSet);
                    //отправляем первый файл, если он есть
                    await SetDataFiles();
                }
                else 
                {
                    lbxMain.AllowDrop = true;
                    lbxMain.Background = Brushes.Ivory;
                    WinMain.Title = $"Все данные отправлены(Были только папки)";
                    //отправляем сообщение о том, что передача данных окончена
                    await _socket.SendAsync(Encoding.UTF8.GetBytes("TRANSFEREND|0|0*"), SocketFlags.None);
                }

            }
            catch (Exception ex)
            {
                MessageBox.Show("Подготовка списка файлов\n" + ex.Message);
            }
        }

        public struct PathNames 
        {
            public string NameShort;
            public string NameFull;
        }
    }
}
