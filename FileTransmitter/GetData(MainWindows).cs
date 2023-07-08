﻿using System;
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
            List<byte> data = new List<byte>();
            byte[] fileBody;
            byte[] oneChar = new byte[1];
            int resultBytes = 0;
            string fileName = "";
            long fileLength;
            string[] dataInfo;
            string strBuff = "";
            char[] charArray;


            //----------------------------------------------
            bool stopGetData = false;

            //отмена функции получения данных
            token.Register(() =>
            {
                //MessageBox.Show("Прекращаем работу метода прием данных");
                stopGetData = true;
            });
            //----------------------------------------------



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

                            Dispatcher.Invoke(() => WinMain.Title = fileName);
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
                            Dispatcher.Invoke(() => MessageBox.Show("Ошибка записи файла\n" + ex.Message));

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
    }
}