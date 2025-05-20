using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RemotePythonExecution.Interface;
using RemotePythonExecution.Interface.RemotePythonExecutionServiceDependency.JsonModel;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WatsonTcp;

namespace RemotePythonExecution.Services
{
    public class RemotePythonExecutionService : BackgroundService
    {
        #region Services

        private readonly ILogger<RemotePythonExecutionService> mLogger;
        private readonly IAppSettingService mAppSettingService;

        #endregion

        #region Var

        private readonly WatsonTcpServer mTcpServer;
        private Process mProcess;
        private bool mIsOutputEnded = false;
        private bool mIsProcessEnded = false;
        public Guid? CurrentConnectionGuid = null;

        private string InterpreterPath = "";
        private string WorkingDirrectoryPath = "";
        private string SourceCodeSavePaths = "";

        #endregion

        #region Const

        private const string cEndOfLineCharster = "\r\n";

        #endregion

        #region ~

        public RemotePythonExecutionService(IServiceProvider serviceProvider) 
        {
            mLogger = serviceProvider.GetRequiredService<ILogger<RemotePythonExecutionService>>();
            mAppSettingService = serviceProvider.GetRequiredService<IAppSettingService>();

            //ParamsTcpServer paramsTcpServer = new(19000, cEndOfLineCharster, pingIntervalSec:2);
            mTcpServer = new WatsonTcpServer("0.0.0.0", 19000);
        
            Subscribe();

            mTcpServer.Start();

            mLogger.LogInformation("Load RemotePythonExecutionService ~");

            SetPath();
        }

        private void SetPath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                SourceCodeSavePaths = mAppSettingService.SourceCodeSavePaths.Windows;
                InterpreterPath = mAppSettingService.PythonPaths.InterpreterPath.Windows;
                WorkingDirrectoryPath = mAppSettingService.PythonPaths.WorkingDirrectoryPath.Windows;
                return;
            }

            SourceCodeSavePaths = mAppSettingService.SourceCodeSavePaths.Linux;
            InterpreterPath = mAppSettingService.PythonPaths.InterpreterPath.Linux;
            WorkingDirrectoryPath = mAppSettingService.PythonPaths.WorkingDirrectoryPath.Linux;
        }

        #region Subscribe/Unsubscribe

        private void Subscribe()
        {
            mTcpServer.Events.ClientConnected += ClientConnected;
            mTcpServer.Events.ClientDisconnected += ClientDisconnected;
            mTcpServer.Events.MessageReceived += MessageReceived;
            mTcpServer.Events.ExceptionEncountered += ExceptionEncountered;
        }

        private void UnSubscribe()
        {
            mTcpServer.Events.ClientConnected -= ClientConnected;
            mTcpServer.Events.ClientDisconnected -= ClientDisconnected;
            mTcpServer.Events.MessageReceived -= MessageReceived;
            mTcpServer.Events.ExceptionEncountered -= ExceptionEncountered; 
        }

        #endregion

        #region Events

        private void ClientConnected(object sender, ConnectionEventArgs e)
        {
            mLogger.LogInformation("Client connected. Connection id: {Guid}", e.Client.Guid);

            mIsOutputEnded = false;
            mIsProcessEnded = false;
        }

        private void ClientDisconnected(object sender, DisconnectionEventArgs e)
        {
            mLogger.LogInformation("Client disconnected. Connection id: {Guid}", e.Client.Guid);

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

        private void ExceptionEncountered(object sender, ExceptionEventArgs e)
        {
            mLogger.LogError("Error happened {errorMessage}", e.Exception.Message);
        }

        private void MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            CurrentConnectionGuid = e.Client.Guid;
            
            var message = System.Text.Encoding.Default.GetString(e.Data);
            var jsonMessage = JsonSerializer.Deserialize<ReceivedMessage>(message);

            switch (jsonMessage.MessageType)
            {
                case "source_code":
                    {
                        if (string.IsNullOrEmpty(jsonMessage.Code))
                            return;

                        string code = jsonMessage.Code;

                        SaveCodeAndStartProcess(code, false);
                    }

                    break;
                case "debug_source_code":
                    {
                        if (string.IsNullOrEmpty(jsonMessage.Code))
                            return;

                        string code = jsonMessage.Code;
                        SaveCodeAndStartProcess(code, true);
                    }

                    break;
                case "control_characters":
                    {
                        if (string.IsNullOrEmpty(jsonMessage.ControlCharacters))
                            return;

                        mProcess.StandardInput.WriteLine(jsonMessage.ControlCharacters);
                    }

                    break;
            }
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

                    await mTcpServer.DisconnectClientAsync((Guid)CurrentConnectionGuid,  MessageStatus.Removed, true, stoppingToken);
                    CurrentConnectionGuid = null;
                }
            }
        }

        #endregion

        #region Private methods

        private void SaveCodeAndStartProcess(string code, bool withDebug)
        {
            File.WriteAllText(SourceCodeSavePaths, code);
            Task.Run(() => StartProcess(withDebug: withDebug));
        }

        private void StartProcess(bool withDebug = false)
        {
            string arg = string.Format($"-u -m {Path.GetFileNameWithoutExtension(SourceCodeSavePaths)}");
            
            if (withDebug)
                arg = string.Format($"-u -m pdb {SourceCodeSavePaths}");

            ProcessStartInfo proccesInfo = new()
            {
                FileName = InterpreterPath, 
                WorkingDirectory = WorkingDirrectoryPath,
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
                    mTcpServer.SendAsync((Guid)CurrentConnectionGuid, e.Data);
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
                    mTcpServer.SendAsync((Guid) CurrentConnectionGuid, e.Data);
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
