using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace FileTransmitter
{
    public partial class MainWindow : Window
    {
        //получение данных
        private async Task GetData()
        {
            BinaryWriter binaryWriter = new BinaryWriter(Stream.Null);
            List<byte> data = new List<byte>();
            byte[] fileBody;
            byte[] oneChar = new byte[1];
            int resultBytes = 0;
            string fileName = "";
            long fileLength;
            string[] dataInfo;
            string strBuff = "";
            char[] charArray;
            bool stopGetData = false;

            //отмена функции получения данных
            token.Register(() =>
            {
                //MessageBox.Show("Прекращаем работу метода прием данных");
                stopGetData = true;
            });

            //если папки для сохранения не существует, то создаем ее
            if (!Directory.Exists(config.DirectoryForSave))
                Directory.CreateDirectory(config.DirectoryForSave);

            while (true)
            {
                //очищаем буферы
                data.Clear();

                //считываем имя файла
                while (true)
                {
                    resultBytes = await _socket.ReceiveAsync(oneChar, SocketFlags.None);

                    //остановка через токен
                    if (stopGetData) return;

                    if (resultBytes == 0 || oneChar[0] == '*')
                        break;
                    //заполняем буфер
                    data.Add(oneChar[0]);
                }

                charArray = new char[data.Count];

                //переводим название файла в строковый формат
                int resultToConvert = Encoding.UTF8.GetDecoder().GetChars(data.ToArray(), charArray, true);

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
                            Directory.CreateDirectory(config.DirectoryForSave + @"\" + dataInfo[i]);
                        }
                        break;

                    case "STATISTIC":

                        //обнуляем счетчик
                        _errorDataGet = 0;
                        _countFilesForGet = int.Parse(dataInfo[1]);

                        Action action = () =>
                        {
                            //прогресс-бар
                            prgAllFiles.Maximum = _countFilesForGet;
                            prgAllFiles.Value = 0;
                            prgAllFiles.Visibility = Visibility.Visible;
                            lblAllFiles.Content = $"Получено файлов: {0} из {_countFilesForGet}.";


                            //блокируем перетаскивание Drag & Drop
                            lblMain.Content = "Идет получение файлов";
                            lblMain.AllowDrop = false;
                            lblMain.Background = Brushes.Lavender;
                            //WinMain.Title = "Получение данных";
                        };
                        Dispatcher.Invoke(action);
                        break;

                    case "TRANSFEREND":
                        Action action2 = () =>
                        {
                            //Разблокируем перетаскивание Drag & Drop
                            lblMain.Content = "Тащи сюда свои файлы";
                            lblMain.AllowDrop = true;
                            lblMain.Background = Brushes.Ivory;
                            //WinMain.Title = $"Все данные получены. Ошибок: {_errorDataGet}";

                            prgAllFiles.Visibility = Visibility.Hidden;
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
                                lblMain.Content = "Тащи сюда свои файлы";
                                lblMain.AllowDrop = true;
                                lblMain.Background = Brushes.Ivory;
                                //WinMain.Title = $"Все данные отправлены, Ошибок: {_errorDataSet}";
                                prgFile.Visibility = Visibility.Hidden;
                                prgAllFiles.Visibility = Visibility.Hidden;
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

                        //прогресс-бар
                        Dispatcher.Invoke(() =>
                        {
                            lblFile.Content = fileName;
                            prgFile.Minimum = 0;
                            prgFile.Maximum = 1;
                            prgFile.Visibility = Visibility.Visible;
                        });

                        try
                        {
                            //создаем файл на диске
                            using FileStream fs = File.Create(config.DirectoryForSave + @"\" + fileName);

                            _countFilesForGet--;
                            Dispatcher.Invoke(() =>
                            {
                                lblFile.Content = fileName;
                                prgFile.Visibility = Visibility.Hidden;//прогресс-бар
                                prgAllFiles.Value++;
                                lblAllFiles.Content = $"Получено файлов: {prgAllFiles.Value} из {prgAllFiles.Maximum}.";
                            });
                            
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Ошибка записи пустого файла" + ex.Message);
                            prgAllFiles.Visibility = Visibility.Hidden;
                            prgFile.Visibility = Visibility.Hidden;
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
                            //прогресс-бар
                            Dispatcher.Invoke(() =>
                            {
                                lblFile.Content = fileName;
                                prgFile.Minimum = 0;
                                prgFile.Maximum = fileLength;
                                prgFile.Visibility = Visibility.Visible;
                            });


                            FileInfo fileInfo = new FileInfo(config.DirectoryForSave + @"\" + fileName);
                            binaryWriter = new BinaryWriter(fileInfo.OpenWrite());

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

                                //прогресс-бар
                                Dispatcher.Invoke(()=> prgFile.Value = countBytes);

                                //если мы считали весь файл в поток, то выходим из цикла
                                if (countBytes == fileLength)
                                    break;
                            }

                            //закрываем поток записи и высвобождаем его память
                            binaryWriter.Close();

                            _countFilesForGet--;                            
                            Dispatcher.Invoke(() =>
                            {
                                
                                lblFile.Content = fileName;
                                prgFile.Visibility = Visibility.Hidden;//прогресс-бар
                                prgAllFiles.Value++;
                                lblAllFiles.Content = $"Получено файлов: {prgAllFiles.Value} из {prgAllFiles.Maximum}.";
                            });
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                MessageBox.Show($"Ошибка записи файла\nФайл {fileName} не был полностью записан и будет удален\n" + ex.Message);
                                prgAllFiles.Visibility = Visibility.Hidden;
                                prgFile.Visibility = Visibility.Hidden;
                                lblAllFiles.Content = "Фокус не удался.";
                                lblFile.Content = "В следующий раз точно повезет!";
                            });

                            //закрываем поток записи и высвобождаем его память
                            binaryWriter.Close();
                            //если произошла ошибка, то удаляем не полностью записанный файл
                            File.Delete(_directory + "\\" + fileName);

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

                //очищаем буферы
                data.Clear();
            }
        }
    }
}
