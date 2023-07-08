﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace FileTransmitter
{
    public partial class MainWindow : Window
    {
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

                    Dispatcher.Invoke(() => WinMain.Title = fileNameStruct.NameShort);
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
    }
}