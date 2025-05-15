using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PHS.Networking.Enums;
using RemotePythonExecution.Interface.RemotePythonExecutionServiceDependency.JsonModel;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tcp.NET.Server;
using Tcp.NET.Server.Events.Args;
using Tcp.NET.Server.Models;

namespace RemotePythonExecution.Services
{
    public class RemotePythonExecutionService : BackgroundService
    {
        #region Services

        private readonly ILogger<RemotePythonExecutionService> mLogger;

        #endregion

        #region Var

        private readonly TcpNETServer mTcpNetServer;
        private Process mProcess;
        private bool mIsOutputEnded = false;
        private bool mIsProcessEnded = false;
        public ConnectionTcpServer CurrentConnection = null;

        #endregion

        #region Const

        private const string cEndOfLineCharster = "\r\n";

        #endregion

        #region ~

        public RemotePythonExecutionService(IServiceProvider serviceProvider) 
        {
            mLogger = serviceProvider.GetRequiredService<ILogger<RemotePythonExecutionService>>();

            ParamsTcpServer paramsTcpServer = new(19000, cEndOfLineCharster);
            mTcpNetServer = new TcpNETServer(paramsTcpServer);
            mTcpNetServer.StartAsync();

            Subscribe();

            mLogger.LogInformation("Load RemotePythonExecutionService ~");
        }

        #region Subscribe/Unsubscribe

        private void Subscribe()
        {
            mTcpNetServer.MessageEvent += MessageEvent;
            mTcpNetServer.ConnectionEvent += ConnectionEvent;
            mTcpNetServer.ErrorEvent += ErrorEvent;
        }


        private void UnSubscribe()
        {
            mTcpNetServer.MessageEvent -= MessageEvent;
            mTcpNetServer.ConnectionEvent -= ConnectionEvent;
            mTcpNetServer.ErrorEvent -= ErrorEvent;
        }

        #endregion


        #region Events

        private void ConnectionEvent(object sender, TcpConnectionServerEventArgs args)
        {
            ConnectionEventType connectionEvent = args.ConnectionEventType;

            if (connectionEvent == ConnectionEventType.Connected)
            {
                mLogger.LogInformation("Client connected. Connection id: {ConnectionId}", args.Connection.ConnectionId);

                mIsOutputEnded = false;
                mIsProcessEnded = false;
            }

            if (connectionEvent == ConnectionEventType.Disconnect)
            {
                mLogger.LogInformation("Client disconnected. Connection id: {ConnectionId}", args.Connection.ConnectionId);

                try
                {
                    if (!mProcess.HasExited)
                    {
                        mIsOutputEnded = true;
                        mIsProcessEnded = true;

                        mProcess.CancelErrorRead();
                        mProcess.CancelOutputRead();

                        mProcess.Close();
                    }
                }
                catch (Exception ex) 
                {
                    mLogger.LogError("{error}", ex.Message);
                }
            }
        }

        private void MessageEvent(object sender, TcpMessageServerEventArgs args)
        {
            CurrentConnection = args.Connection;
            var messageEvent = args.MessageEventType;

            //получаем исходный код или управляющие символы
            if (messageEvent == MessageEventType.Receive)
            {
                var jsonMessage = JsonSerializer.Deserialize<ReceivedMessage>(args.Message);

                //если получаем исходный код, записываем в файл и запускаем процесс
                if (jsonMessage.MessageType == "source_code")
                {
                    mLogger.LogInformation("Source сode Receive");

                    if (!string.IsNullOrEmpty(jsonMessage.Code))
                    {
                        string code = jsonMessage.Code;
                        File.WriteAllText("C:\\Users\\Professional\\Downloads\\python-3.13.0-embed-amd64\\test2.py", code);
                        StartProcess(false);
                    }
                }

                //если получаем исходный код, записываем в файл и запускаем процесс
                if (jsonMessage.MessageType == "debug_source_code")
                {
                    mLogger.LogInformation("Source сode Receive");

                    if (!string.IsNullOrEmpty(jsonMessage.Code))
                    {
                        string code = jsonMessage.Code;
                        File.WriteAllText("C:\\Users\\Professional\\Downloads\\python-3.13.0-embed-amd64\\test2.py", code);
                        StartProcess(true);
                    }
                }

                //записывает в запущенный процесс управляющие символы
                if (jsonMessage.MessageType == "control_characters")
                {
                    mLogger.LogInformation("Сontrol characters Receive");

                    if (!string.IsNullOrEmpty(jsonMessage.ControlCharacters))
                        mProcess.StandardInput.WriteLine(jsonMessage.ControlCharacters);
                }
            }
        }

        private void ErrorEvent(object sender, TcpErrorServerEventArgs args)
        {
            mLogger.LogError("Error happened {error}", args.Exception.Message);
        }
        #endregion

        public override void Dispose()
        {
            UnSubscribe();

            base.Dispose();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                while (mIsOutputEnded && mIsProcessEnded)
                {
                    mIsOutputEnded = false;
                    mIsProcessEnded = false;

                    await mTcpNetServer.DisconnectConnectionAsync(CurrentConnection, stoppingToken);
                    CurrentConnection = null;
                }
            }
        }

        #endregion

        #region Private methods

        private void StartProcess(bool withDebug = false)
        {
            string arg = string.Format("-u -m test2");
            if (withDebug)
                arg = string.Format("-u -m pdb test2.py");

            ProcessStartInfo proccesInfo = new()
            {
                FileName = "C:\\Users\\Professional\\Downloads\\python-3.13.0-embed-amd64\\python.exe",
                WorkingDirectory = "C:\\Users\\Professional\\Downloads\\python-3.13.0-embed-amd64\\",
                Arguments = arg,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
            };
            mProcess = new()
            {
                StartInfo = proccesInfo,
                EnableRaisingEvents = true,
            };


            mProcess.Exited += ProcessExited;
            mProcess.OutputDataReceived += ProcessOutputDataReceived;
            mProcess.ErrorDataReceived += ProcessErrorDataReceived;
            mProcess.Start();
            mProcess.BeginOutputReadLine();
            mProcess.BeginErrorReadLine();
            mProcess.WaitForExit();
        }

        private void ProcessErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                try
                {
                    mTcpNetServer.SendToConnectionAsync(e.Data, CurrentConnection);
                    mIsOutputEnded = false;
                }
                catch
                {
                    mIsOutputEnded = true;
                }

            }
            else
            {
                mIsOutputEnded = true;
            }
        }

        private void ProcessOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                try
                {
                    mTcpNetServer.SendToConnectionAsync(e.Data, CurrentConnection);
                    mLogger.LogDebug("{data}", e.Data);
                    mIsOutputEnded = false;
                }
                catch
                {
                    mLogger.LogDebug("Catch happened");
                    mIsOutputEnded = true;
                }
            }
            else
            {
                mLogger.LogDebug("Output ended happened");
                mIsOutputEnded = true;
            }
        }

        private void ProcessExited(object sender, EventArgs e)
        {
            mLogger.LogDebug("Process ended happened");
            mIsProcessEnded = true;
        }

        #endregion
    }
}
