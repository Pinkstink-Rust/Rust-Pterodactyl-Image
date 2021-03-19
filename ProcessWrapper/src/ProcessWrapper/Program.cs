using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Websocket.Client;

namespace ProcessWrapper
{
    class Program
    {
        static WebsocketClient _websocketClient;
        static string RconIP { get; }
        static string RconPort { get; }
        static string RconPassword { get; }
        static Uri WebsocketUrl => new Uri($"ws://{RconIP}:{RconPort}/{RconPassword}", UriKind.Absolute);

        static Program()
        {
            RconIP = Environment.GetEnvironmentVariable("RCON_IP");
            if (string.IsNullOrWhiteSpace(RconIP) || !IPAddress.TryParse(RconIP, out IPAddress serverHostname) || RconIP == "0.0.0.0")
            {
                RconIP = "127.0.0.1";
            }

            RconPort = Environment.GetEnvironmentVariable("RCON_PORT");
            RconPassword = Environment.GetEnvironmentVariable("RCON_PASS");
        }

        static Process _rustProcess;
        static readonly ConcurrentStack<Action> _postProcessActions = new ConcurrentStack<Action>();
        static readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        static readonly string _logFilePath = $"logs/RustServer-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.log";
        static readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        static void Main(string[] args)
        {
            CreateLoggingDirectory();
            CreateWebsocketClient();
            CreateProcess(args);
            _websocketClient.Start();
            var readThread = new Thread(new ThreadStart(() => ReadFileOutput(_cancellationTokenSource.Token)));
            readThread.Start();
            ListenToInput().Wait();
            _rustProcess.WaitForExit();
            _cancellationTokenSource.Cancel();
            Console.WriteLine("Rust Process Exited");
            while (_postProcessActions.TryPop(out Action action))
            {
                try
                {
                    action.Invoke();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
            _websocketClient.Dispose();
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            _websocketClient.Dispose();
            _rustProcess.Kill();
            _rustProcess.WaitForExit();
            Environment.Exit(1);
        }

        static void MoveFiles(FileInfo fileInfo)
        {
            if (!fileInfo.Exists)
            {
                File.WriteAllText(fileInfo.FullName, "[]");
                Console.WriteLine($"File: {fileInfo.FullName} was not found, Move Files routine aborted");
                return;
            }

            Console.WriteLine("Performing Move Files routine");
            var fileBytes = File.ReadAllBytes(fileInfo.FullName);
            var directories = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(fileBytes);
            var backupDirectoryName = Path.GetFileNameWithoutExtension(fileInfo.Name);
            var backupDirectory = Path.Join(Directory.GetCurrentDirectory(), backupDirectoryName, Math.Round((DateTime.Now - DateTime.UnixEpoch).TotalMilliseconds, 0).ToString());
            if (!Directory.Exists(backupDirectory)) Directory.CreateDirectory(backupDirectory);
            foreach (var directory in directories)
            {
                if (!Directory.Exists(directory.Key))
                {
                    Console.WriteLine($"Directory not found: {directory.Key}");
                    continue;
                }

                var files = directory.Value;
                foreach (var file in files)
                {
                    var filesInDirectory = Directory.EnumerateFiles(directory.Key, file);
                    if (filesInDirectory.Count() < 1)
                    {
                        Console.WriteLine($"Failed to find any files matching the pattern \"{file}\" within \"{directory.Key}\"");
                        continue;
                    }

                    foreach (var filePath in filesInDirectory)
                    {
                        if (!File.Exists(filePath))
                        {
                            Console.WriteLine($"File does not exist: {filePath}");
                            continue;
                        }

                        try
                        {
                            var fileName = Path.GetFileName(filePath);
                            var destinationPath = Path.Join(backupDirectory, directory.Key, fileName);
                            var dirInfo = Directory.GetParent(destinationPath);
                            if (!dirInfo.Exists) dirInfo.Create();
                            File.Move(filePath, destinationPath);
                            Console.WriteLine($"Wiped: {filePath}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to wipe: {filePath}\nError: {ex.Message}");
                        }
                    }
                }
            }
        }

        static Task<string> ReadInput()
        {
            return Task.Run(() =>
            {
                string command = Console.ReadLine();
                return command;
            });
        }

        static async Task ListenToInput()
        {
            Console.CancelKeyPress += Console_CancelKeyPress;
            Task<string> commandTask = null;
            do
            {
                if (commandTask == null || commandTask.IsCompleted) commandTask = ReadInput();
                await Task.WhenAny(Task.Delay(250), commandTask);
                if (!commandTask.IsCompleted) continue;
                string command = commandTask.Result;

                if (command.StartsWith("__move-files"))
                {
                    command = command.Replace("__move-files", string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(command)) return;
                    var fileInfo = new FileInfo(command);
                    if (!fileInfo.Exists)
                    {
                        Console.WriteLine($"Failed to find a file at location: {fileInfo.FullName}");
                        return;
                    }

                    Console.WriteLine("Moving of Files queued for next server shutdown");
                    _postProcessActions.Push(() => MoveFiles(fileInfo));
                    continue;
                }

                if (_websocketClient?.IsRunning == true)
                {
                    var rconCommand = new RConCommand
                    {
                        Identifier = 1000,
                        Message = command
                    };
                    var rconCommandString = JsonSerializer.Serialize(rconCommand, _jsonSerializerOptions);
                    _websocketClient.Send(rconCommandString);
                }
                else
                {
                    Console.WriteLine($"Failed to send command, RCON is disconnected.");
                }
            }
            while (!_rustProcess.HasExited);
        }

        static void OnRconMessage(ResponseMessage responseMessage)
        {
            if (responseMessage.MessageType != WebSocketMessageType.Text) return;
            if (!_cancellationTokenSource.IsCancellationRequested) _cancellationTokenSource.Cancel();
            var message = JsonSerializer.Deserialize<RConResponse>(responseMessage.Text);
            Console.WriteLine(message.Message);
        }

        static void CreateProcess(string[] args)
        {
            var argsString = string.Join(" ", args.Skip(1));
            argsString = $"-logfile \"{_logFilePath}\" {argsString}";
            _rustProcess = Process.Start(new ProcessStartInfo
            {
                FileName = args[0],
                Arguments = argsString,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            Console.WriteLine("Process Started");
        }

        static void ReadFileOutput(CancellationToken cancellationToken)
        {
            while (!File.Exists(_logFilePath) && !cancellationToken.IsCancellationRequested)
            {
                Thread.Sleep(500);
            }

            if (cancellationToken.IsCancellationRequested) return;

            bool previousLineDebug = false;
            using var fs = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs, Encoding.Default);
            do
            {
                var line = sr.ReadLine();
                if (line == null)
                {
                    Thread.Sleep(100);
                    continue;
                }
                if (line == string.Empty && previousLineDebug)
                {
                    previousLineDebug = false;
                    continue;
                }
                if (line.StartsWith("(Filename: "))
                {
                    previousLineDebug = true;
                    continue;
                }
                Console.WriteLine(line);
            }
            while (!cancellationToken.IsCancellationRequested);
            Console.WriteLine("File Output Terminated");
        }

        static void CreateWebsocketClient()
        {
            _websocketClient = new WebsocketClient(WebsocketUrl)
            {
                IsReconnectionEnabled = true,
                ReconnectTimeout = TimeSpan.FromSeconds(5),
                ErrorReconnectTimeout = TimeSpan.FromSeconds(5)
            };
            _websocketClient.MessageReceived.Subscribe(OnRconMessage);

            var rconCommand = new RConCommand
            {
                Identifier = 1000,
                Message = "status",
                Name = "PterodactylClient"
            };
            var rconCommandString = JsonSerializer.Serialize(rconCommand, _jsonSerializerOptions);
            _websocketClient.Send(rconCommandString);
        }

        static void CreateLoggingDirectory()
        {
            if (!Directory.Exists("logs"))
                Directory.CreateDirectory("logs");
        }
    }
}