using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RemotePythonExecution.Interface;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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
        public Guid CurrentConnectionGuid;

        private string InterpreterPath = "";
        private string WorkingDirrectoryPath = "";
        private string SourceCodeSavePaths = "";

        private bool mIsDisposed;

        #endregion

        #region ~

        public RemotePythonExecutionService(IServiceProvider serviceProvider) 
        {
            mLogger = serviceProvider.GetRequiredService<ILogger<RemotePythonExecutionService>>();
            mAppSettingService = serviceProvider.GetRequiredService<IAppSettingService>();

            string ip =mAppSettingService.ServerSettings.Ip;
            int port =mAppSettingService.ServerSettings.Port;

            mTcpServer = new WatsonTcpServer(ip, port);
        
            Subscribe();

            mTcpServer.Start();

            SetPath();

            mLogger.LogInformation("Server runing on {ip}:{port}", ip, port);
        }

        #endregion

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
            CurrentConnectionGuid = e.Client.Guid;

            mIsOutputEnded = false;
            mIsProcessEnded = false;
        }

        private void ClientDisconnected(object sender, DisconnectionEventArgs e)
        {
            mLogger.LogInformation("Client disconnected. Connection id: {Guid}", e.Client.Guid);

            mIsOutputEnded = true;
            mIsProcessEnded = true;
        }

        private void ExceptionEncountered(object sender, ExceptionEventArgs e)
        {
            if (e.Exception is IOException)
                return;

            mLogger.LogError("Error happened {errorMessage}", e.Exception);
        }

        private void MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            Dictionary<string, object> metadata = e.Metadata;

            foreach(var key in metadata.Keys)
            {
                switch (key)
                {
                    case "source_code":
                        {
                            var code = metadata[key].ToString();
                            
                            if (string.IsNullOrEmpty(code))
                                return;

                            SaveCodeAndStartProcess(code, false);
                            break;
                        }

                    case "debug_source_code":
                        {
                            string code = metadata[key].ToString();
                            if (string.IsNullOrEmpty(code))
                                return;

                            SaveCodeAndStartProcess(code, true);
                            break;
                        }

                    case "control_characters":
                        {
                            string characters = metadata[key].ToString();
                            if (string.IsNullOrEmpty(characters))
                                return;

                            mProcess?.StandardInput?.WriteLine(characters);
                            break;  
                        }
                }
            }
        }

        private void ProcessErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                try
                {
                    mTcpServer.SendAsync(CurrentConnectionGuid, e.Data);
                    mIsOutputEnded = false;
                }
                catch (Exception exp)
                {
                    mLogger.LogError("Catch happened {exp}", exp);
                    mIsOutputEnded = true;
                }
            }
        }

        private void ProcessOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                try
                {
                    mTcpServer.SendAsync(CurrentConnectionGuid, e.Data);
                    mLogger.LogDebug("{data}", e.Data);
                    mIsOutputEnded = false;
                }
                catch (Exception exp)
                {
                    mLogger.LogError("Catch happened {exp}", exp);
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

        #region Public methods

        public override void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);

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

                    try
                    {
                        if (!mProcess?.HasExited ?? false)
                        {
                            mProcess.CancelErrorRead();
                            mProcess.CancelOutputRead();

                            mProcess.Close();
                        }
                    }
                    catch(Exception exception)
                    {
                        mLogger.LogError("Error when procces close {error}", exception.Message);
                    }
                    
                    if (mTcpServer.IsClientConnected(CurrentConnectionGuid))
                        await mTcpServer.DisconnectClientAsync(CurrentConnectionGuid, MessageStatus.Removed, true, stoppingToken);

                    CurrentConnectionGuid = Guid.Empty;
                }
            }
        }

        #endregion

        #region Private methods

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

        protected virtual void Dispose(bool disposing)
        {
            if (!mIsDisposed)
            {
                mIsDisposed = true;

                if (disposing)
                {
                    UnSubscribe();
                    mProcess?.Close();
                    mProcess?.Dispose();

                    mTcpServer.DisconnectClientsAsync();
                    mTcpServer.Stop();
                    mTcpServer.Dispose();
                    mLogger.LogInformation("Service stop and dispose");
                }
            }
        }

        #endregion
    }
}
