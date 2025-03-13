using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ejercicio4Tema4Servicios
{
    internal class ShiftServer
    {
        private List<string> users = new List<string>();
        private List<string> waitQueue = new List<string>();
        private List<Socket> clients = new List<Socket>();
        private Socket serverSocket;
        private bool running = true;

        private readonly object userLock = new object();
        private readonly object queueLock = new object();
        private readonly object clientLock = new object();

        public void Init()
        {
            int port = 31416;
            while (port < 65535)
            {
                try
                {
                    serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    serverSocket.Bind(new IPEndPoint(IPAddress.Any, port));
                    serverSocket.Listen(10);
                    Console.WriteLine($"Servidor iniciado en el puerto {port}");
                    break;
                }
                catch
                {
                    port++;
                }
            }
            if (port >= 65535)
            {
                Console.WriteLine("No hay puertos disponibles.");
                return;
            }

            string userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
            string userFile = userProfile + "\\usuarios.txt";
            ReadNames(userFile);
            LoadQueue();

            try
            {
                while (running)
                {
                    Socket client = serverSocket.Accept();
                    lock (clientLock)
                    {
                        clients.Add(client);
                    }

                    Thread clientThread = new Thread(() => HandleClient(client));
                    clientThread.Start();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error en el servidor: {ex.Message}");
            }
            finally
            {
                ShutdownServer();
            }
        }

        private void HandleClient(Socket client)
        {
            try
            {
                using (NetworkStream stream = new NetworkStream(client))
                using (StreamReader reader = new StreamReader(stream))
                using (StreamWriter writer = new StreamWriter(stream) { AutoFlush = true })
                {
                    writer.WriteLine("Bienvenido al servidor de turnos. Ingrese su nombre:");
                    string username = reader.ReadLine();

                    if (username == "admin")
                    {
                        writer.WriteLine("Ingrese el PIN:");
                        string userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
                        int pin = ReadPin(userProfile + "\\pin.bin");
                        string inputPin = reader.ReadLine();

                        if (pin == -1) pin = 1234;
                        if (inputPin != pin.ToString())
                        {
                            writer.WriteLine("PIN incorrecto. Desconectando...");
                            return;
                        }

                        writer.WriteLine("Acceso concedido. Comandos: list, add, del pos, chpin pin, exit, shutdown.");
                    }
                    else
                    {
                        lock (userLock)
                        {
                            if (!users.Contains(username))
                            {
                                writer.WriteLine("Usuario desconocido. Desconectando...");
                                return;
                            }
                        }

                        writer.WriteLine("Acceso concedido. Comandos: list, add.");
                    }

                    while (running)
                    {
                        string command = reader.ReadLine();
                        if (command == null) break; // Cliente desconectado
                        ProcessCommand(command, username, writer);
                    }
                }
            }
            catch (IOException)
            {
                Console.WriteLine("Cliente desconectado abruptamente.");
            }
            finally
            {
                lock (clientLock)
                {
                    clients.Remove(client);
                }
                client.Close();
            }
        }

        private void ProcessCommand(string command, string username, StreamWriter writer)
        {
            string[] parts = command.Split(' ');

            switch (parts[0])
            {
                case "list":
                    lock (queueLock)
                    {
                        writer.WriteLine("Lista de espera:");
                        waitQueue.ForEach(writer.WriteLine);
                    }
                    break;

                case "add":
                    lock (queueLock)
                    {
                        if (!waitQueue.Any(u => u.StartsWith(username)))
                        {
                            waitQueue.Add($"{username}-{DateTime.Now}");
                            writer.WriteLine("OK");
                        }
                        else
                        {
                            writer.WriteLine("Ya estás en la lista.");
                        }
                    }
                    break;

                case "del":
                    lock (queueLock)
                    {
                        if (parts.Length == 2 && int.TryParse(parts[1], out int pos) && pos >= 0 && pos < waitQueue.Count)
                        {
                            waitQueue.RemoveAt(pos);
                            writer.WriteLine("Usuario eliminado.");
                        }
                        else
                        {
                            writer.WriteLine("delete error");
                        }
                    }
                    break;

                case "chpin":
                    if (parts.Length == 2 && int.TryParse(parts[1], out int newPin) && newPin >= 1000)
                    {
                        string userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
                        lock (userLock)
                        {
                            File.WriteAllBytes(userProfile + "\\pin.bin", BitConverter.GetBytes(newPin));
                        }
                        writer.WriteLine("PIN cambiado correctamente.");
                    }
                    else
                    {
                        writer.WriteLine("Error al cambiar el PIN.");
                    }
                    break;

                case "exit":
                    writer.WriteLine("Saliendo...");
                    break;

                case "shutdown":
                    writer.WriteLine("Servidor apagándose...");
                    running = false;
                    SaveQueue();
                    ShutdownServer();
                    Environment.Exit(0);
                    break;

                default:
                    writer.WriteLine("Comando no reconocido.");
                    break;
            }
        }

        private void ReadNames(string path)
        {
            lock (userLock)
            {
                if (File.Exists(path))
                {
                    users = File.ReadAllText(path).Split(';').ToList();
                }
            }
        }

        private int ReadPin(string path)
        {
            lock (userLock)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        byte[] data = File.ReadAllBytes(path);
                        return BitConverter.ToInt32(data, 0);
                    }
                }
                catch { }
            }
            return -1;
        }

        private void SaveQueue()
        {
            lock (queueLock)
            {
                try
                {
                    File.WriteAllLines("waitQueue.txt", waitQueue);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al guardar la cola: {ex.Message}");
                }
            }
        }

        private void LoadQueue()
        {
            lock (queueLock)
            {
                if (File.Exists("waitQueue.txt"))
                {
                    waitQueue = File.ReadAllLines("waitQueue.txt").ToList();
                }
            }
        }

        private void ShutdownServer()
        {
            Console.WriteLine("Cerrando servidor...");
            running = false;
            lock (clientLock)
            {
                foreach (var client in clients)
                {
                    try
                    {
                        client.Shutdown(SocketShutdown.Both);
                        client.Close();
                    }
                    catch { }
                }
                clients.Clear();
            }

            serverSocket?.Close();
        }
    }
}
