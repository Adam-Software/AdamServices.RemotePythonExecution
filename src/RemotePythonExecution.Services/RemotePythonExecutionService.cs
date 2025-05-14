using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PHS.Networking.Enums;
using RemotePythonExecution.Interface.RemotePythonExecutionServiceDependency.JsonModel;
using System;
using System.Diagnostics;
using System.IO;
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
        private Process mProcess = new()
        {
            
        };
        private bool mIsOutputEnded = false;
        private bool mIsProcessEnded = false;
        public ConnectionTcpServer CurrentConnection = null;

        #endregion

        #region Const

        private const string cEndOfLineCharster = "\r\n";
        private const string cConnectionSuccessString = "\n";

        #endregion

        #region

        public RemotePythonExecutionService(IServiceProvider serviceProvider) 
        {
            mLogger = serviceProvider.GetRequiredService<ILogger<RemotePythonExecutionService>>();

            ParamsTcpServer paramsTcpServer = new(18000, cEndOfLineCharster, connectionSuccessString: cConnectionSuccessString);
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
            //mTcpClient.ConnectionEvent += ConnectionEvent;
            //mTcpClient.MessageEvent += MessageEvent;
            //mTcpClient.ErrorEvent += ErrorEvent;
        }

        private void UnSubscribe()
        {
            mTcpNetServer.MessageEvent -= MessageEvent;
            mTcpNetServer.ConnectionEvent -= ConnectionEvent;
            //mTcpClient.ConnectionEvent -= ConnectionEvent;
            //mTcpClient.MessageEvent -= MessageEvent;
            //mTcpClient.ErrorEvent -= ErrorEvent;
        }

        #endregion


        #region Events

        private void ConnectionEvent(object sender, TcpConnectionServerEventArgs args)
        {
            ConnectionEventType connectionEvent = args.ConnectionEventType;

            if (connectionEvent == ConnectionEventType.Connected)
            {
                mLogger.LogInformation("Client connected. Connection id: {ConnectionId}", args.Connection.ConnectionId);
                //if the process is running, we reply that we are busy and disconnect.

                try
                {
                    if(!mIsProcessEnded) 
                    //if (!mProcess.HasExited)
                    {
                        mTcpNetServer.SendToConnectionAsync("Service is busy", args.Connection);
                        mTcpNetServer.DisconnectConnectionAsync(args.Connection);

                        return;
                    }
                }
                catch (Exception ex) 
                {
                    mLogger.LogError("{error}", ex);  
                }
            }

            if (connectionEvent == ConnectionEventType.Disconnect)
            {
                mLogger.LogInformation("Client disconnected. Connection id: {ConnectionId}", args.Connection.ConnectionId);

                if (CurrentConnection != null)
                {
                    CurrentConnection = null;

                    mIsOutputEnded = true;
                    mIsProcessEnded = true;
                }

                if (!mIsProcessEnded)
                //if (!mProcess.HasExited)
                {
                    mProcess.CancelErrorRead();
                    mProcess.CancelOutputRead();

                    mProcess.Close();
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
                    if (!string.IsNullOrEmpty(jsonMessage.Code))
                    {
                        string code = jsonMessage.Code;
                        File.WriteAllText("C:\\Users\\Professional\\Downloads\\python-3.13.0-embed-amd64\\test2.py", code);
                        StartProcess(false);
                    }
                }

                //записывает в запущенный процесс управляющие символы
                if (jsonMessage.MessageType == "control_characters")
                {
                    if (!string.IsNullOrEmpty(jsonMessage.ControlCharacters))
                        mProcess.StandardInput.WriteLine(jsonMessage.ControlCharacters);
                }
            }
        }

        #endregion

        public override void Dispose()
        {
            UnSubscribe();

            base.Dispose();
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            return Task.Run(() =>
            {
                while (mIsOutputEnded && mIsProcessEnded)
                {
                    mIsOutputEnded = false;
                    mIsProcessEnded = false;

                    mTcpNetServer.DisconnectConnectionAsync(CurrentConnection);
                }
            }, stoppingToken);
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

        private void ProcessExited(object sender, EventArgs e)
        {
            mIsProcessEnded = true;
        }

        #endregion
    }
}
