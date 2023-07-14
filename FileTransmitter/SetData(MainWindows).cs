using System;
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
            //прогресс бар
            Dispatcher.Invoke(() =>
            {
                prgFile.Minimum = 0;
                
            });

            byte[] dataCaption;
            FileInfo fileInfo;
            try
            {
                fileNameStruct = allFiles[--_countFilesForSet]; //поправка на индекс

                if (fileNameStruct.NameShort.Contains('|') || fileNameStruct.NameShort.Contains('*'))
                {
                    MessageBox.Show($"Имя файла {fileNameStruct.NameShort} содержит некорректные символы (| или *)\nКопирование не возможно. Файл будет пропущен.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    fileInfo = new FileInfo(fileNameStruct.NameFull);
                   
                    //если файл пустой
                    if (fileInfo.Length == 0)
                    {                        
                        //прогресс бар
                        Dispatcher.Invoke(() =>
                        {
                            lblFile.Content = fileNameStruct.NameShort;
                            prgFile.Maximum = 1;
                            prgFile.Value = 1; ;
                        });

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

                            //прогресс бар
                            Dispatcher.Invoke(() =>
                            {
                                lblFile.Content = fileNameStruct.NameShort;
                                prgFile.Maximum = binaryReader.BaseStream.Length;
                                prgFile.Value = 0;
                            });

                            while (true)
                            {
                                dataBodyFile = binaryReader.ReadBytes(dataBodyFile.Length);
                                if (dataBodyFile.Length == 0) break;

                                resultBytes = await _socket.SendAsync(dataBodyFile, SocketFlags.Partial);

                                countBytes += resultBytes;

                                //прогресс бар
                                Dispatcher.Invoke(() => prgFile.Value = countBytes);

                                //если данных в потоке больше нет(достигли конца потока), то выходим из цикла
                                if (binaryReader.BaseStream.Length == countBytes) break;
                            }
                        }
                    }

                    Dispatcher.Invoke(() =>
                    {
                        lblAllFiles.Content = $"Отправлено файлов: {++prgAllFiles.Value} из {prgAllFiles.Maximum}.";
                    });
                }

            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show("отправка данных - файлы\n" + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    prgAllFiles.Visibility = Visibility.Hidden;
                    prgFile.Visibility = Visibility.Hidden;
                    lblAllFiles.Content = "Фокус не удался.";
                    lblFile.Content = "В следующий раз точно повезет!";
                });
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
                        MessageBox.Show($"Имя папки {dirName.NameShort} содержит некорректные символы (| или *)\nКопирование не возможно. Папка будет пропущена.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show("отправка данных - папки\n" + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
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
