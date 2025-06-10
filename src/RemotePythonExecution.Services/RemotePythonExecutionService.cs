using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemotePythonExecution.Interface.RemotePythonExecutionServiceDependency.JsonModel;
using SimpleUdp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WatsonTcp;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace RemotePythonExecution.Services
{
    public class RemotePythonExecutionService :  BackgroundService, IHostedService
    {
        #region Services

        private readonly ILogger<RemotePythonExecutionService> mLogger;
        private readonly IOptionsMonitor<AppSettings> mAppSettingsMonitor;
        private readonly IHostApplicationLifetime mAppLifetime;

        #endregion

        #region Var

        private WatsonTcpServer mTcpServer;
        private Process mProcess;
        //private Guid CurrentConnectionGuid;
        private UdpEndpoint mUdpEndpoint;
        private (Guid Guid, string ip) mCurrentClientParam;

        private bool mIsDisposed;

        private string mInterpreterPath = string.Empty;
        private string mWorkingDirrectoryPath = string.Empty;
        private string mSourceCodeSavePath = string.Empty;

        private bool mIsProcessEnded = false;
        private bool mIsOutputEnded = false;

        
        #endregion

        #region ~

        public RemotePythonExecutionService(IServiceProvider serviceProvider) 
        {
            mLogger = serviceProvider.GetRequiredService<ILogger<RemotePythonExecutionService>>();
            mAppSettingsMonitor = serviceProvider.GetRequiredService<IOptionsMonitor<AppSettings>>();
            mAppLifetime = serviceProvider.GetService<IHostApplicationLifetime>();

            Ip = mAppSettingsMonitor.CurrentValue.ServerSettings.Ip;
            Port = mAppSettingsMonitor.CurrentValue.ServerSettings.Port;

            SetPath(mAppSettingsMonitor.CurrentValue);
            
            mTcpServer = new WatsonTcpServer(Ip, Port);
            mUdpEndpoint = new UdpEndpoint(Ip, Port);

            mAppSettingsMonitor.OnChange(OnChangeSettings);
        
            Subscribe();
            mTcpServer.Start();
            mLogger.LogInformation("Server runing on {ip}:{port}", Ip, Port);
        }

        #endregion

        #region Subscribe/Unsubscribe

        private void Subscribe()
        {
            mTcpServer.Events.ClientConnected += ClientConnected;
            mTcpServer.Events.ClientDisconnected += ClientDisconnected;
            mTcpServer.Events.MessageReceived += MessageReceived;
            mTcpServer.Events.ExceptionEncountered += ExceptionEncountered;
 
            AppDomain.CurrentDomain.ProcessExit += AppProcessExit;

            mAppLifetime.ApplicationStopping.Register(OnStopping);
        }

        private void UnSubscribe()
        {
            mTcpServer.Events.ClientConnected -= ClientConnected;
            mTcpServer.Events.ClientDisconnected -= ClientDisconnected;
            mTcpServer.Events.MessageReceived -= MessageReceived;
            mTcpServer.Events.ExceptionEncountered -= ExceptionEncountered;
            AppDomain.CurrentDomain.ProcessExit -= AppProcessExit;
        }

        #endregion

        #region Events

        private void OnChangeSettings(AppSettings settings, string arg2)
        {
            SetPath(settings);
            SetServerAddress(settings);
        }

        private void ClientConnected(object sender, ConnectionEventArgs e)
        {
            mLogger.LogInformation("Client connected. Connection id: {Guid}", e.Client.Guid);
            var ip = e.Client.IpPort.Split(':').FirstOrDefault();
            mCurrentClientParam = (e.Client.Guid, ip);

            IsOutputEnded = false;
            IsProcessEnded = false;
        }

        private void ClientDisconnected(object sender, DisconnectionEventArgs e)
        {
            mLogger.LogInformation("Client disconnected. Connection id: {Guid}", e.Client.Guid);
            
            KillProcess();
            IsOutputEnded = true;
        }

        private void ExceptionEncountered(object sender, ExceptionEventArgs e)
        {
            if (e.Exception is IOException)
                return;

            /*if (e.Exception is SocketException)
                return;

            if (e.Exception is OperationCanceledException)
            {
                mLogger.LogError("OperationCanceledException");
                return;
            }*/
            
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

                    case "exit":
                        {
                            mLogger.LogInformation("Exit by client request");
                            
                            KillProcess();
                            mIsOutputEnded = true;
                            break;
                        }
                }
            }
        }

        private void ProcessErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                try
                {
                    var data = Encoding.UTF8.GetBytes(e.Data);

                    mUdpEndpoint.SendAsync(mCurrentClientParam.ip, 19000, data);
      
                    mLogger.LogDebug("{data}", e.Data);
                    IsOutputEnded = false;
                }
                catch (Exception exp)
                {
                    mLogger.LogError("Catch happened {exp}", exp);
                    IsOutputEnded = true;
                }
            }
        }

        private void ProcessOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                try
                {
                    var data = Encoding.UTF8.GetBytes(e.Data);
                    mUdpEndpoint.SendAsync(mCurrentClientParam.ip, 19000, data);

                    
                    mLogger.LogDebug("{data}", e.Data);
                }
                catch (TaskCanceledException)
                {
                    mLogger.LogError("Task was canceled");
                    IsOutputEnded = true;
                }
                catch (TimeoutException)
                {
                    mLogger.LogError("Task timeout");                    
                }
                catch (OperationCanceledException)
                {
                    mLogger.LogError("Operation canceled");
                }
                catch (Exception exp)
                {
                    mLogger.LogError("Catch happened {exp}", exp);
                    IsOutputEnded = true;
                }
            }
            else
            {
                mLogger.LogDebug("Output ended happened");
                //await Task.Delay(100);
                IsOutputEnded = true;
            }
        }

        private void ProcessExited(object sender, EventArgs e)
        {
            mLogger.LogDebug("Process ended happened");
            IsProcessEnded = true;
        }

        private void AppProcessExit(object sender, EventArgs e)
        {
            mAppLifetime.StopApplication();
        }

        private void OnStopping()
        {
            Dispose();
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
                while (IsOutputEnded && IsProcessEnded)
                {
                    IsOutputEnded = false;
                    IsProcessEnded = false;

                    /*if (mProcess == null)
                        return;*/

                    ExitData exitData = new(); 

                    try
                    {
                        if (mProcess.HasExited) 
                        {
                            exitData = new ExitData
                            {
                                IsExitDataUpdated = true,

                                ProcessId = mProcess.Id,
                                StartTime = mProcess.StartTime,
                                ExitTime = mProcess.ExitTime,
                                TotalProcessorTime = mProcess.TotalProcessorTime,
                                UserProcessorTime = mProcess.UserProcessorTime,
                                ExitCode = mProcess.ExitCode
                            };

                            
                            //mProcess.Dispose();
                            //mProcess = null;
                        }
                    }
                    catch(Exception exception)
                    {
                        mLogger.LogError("Error when procces close {error}", exception);
                    }

                    var exitJson = mTcpServer.SerializationHelper.SerializeJson(exitData);
                    var exitDictonary = new Dictionary<string, object>() { { "exitData", exitJson } };
                    
                    if (mTcpServer.IsClientConnected(mCurrentClientParam.Guid))
                    {
                        await mTcpServer.SendAsync(mCurrentClientParam.Guid, string.Empty, exitDictonary, token: stoppingToken);
                        await mTcpServer.DisconnectClientAsync(mCurrentClientParam.Guid, MessageStatus.Removed, true, stoppingToken);
                    }

                    mCurrentClientParam.Guid = Guid.Empty;
                    mCurrentClientParam.ip = string.Empty;
                }
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            mLogger.LogError("Host stopped");

            return base.StopAsync(cancellationToken);
        }
        #endregion

        #region Private fields

        private bool IsOutputEnded
        {
            get { return mIsOutputEnded; }
            set 
            { 
                if(value ==  mIsOutputEnded) 
                    return;

                mIsOutputEnded = value; 
            }
        }

        private bool IsProcessEnded
        {
            get { return mIsProcessEnded; }
            set 
            { 
                if(value == mIsProcessEnded) 
                    return;

                mIsProcessEnded = value; 
            }
        }

        private string InterpreterPath
        {
            get { return mInterpreterPath; }
            set
            {
                if (string.IsNullOrEmpty(value))
                    return;

                if (value.Equals(mInterpreterPath))
                    return;

                mInterpreterPath = value;
                mLogger.LogDebug("New path for {name} register with values {value}", nameof(InterpreterPath), InterpreterPath);
            }
        }

        private string WorkingDirrectoryPath
        {
            get { return mWorkingDirrectoryPath; }
            set
            {
                if (string.IsNullOrEmpty(value))
                    return;

                if (value.Equals(mWorkingDirrectoryPath))
                    return;

                mWorkingDirrectoryPath = value;
                mLogger.LogDebug("New path for {name} register with values {value}", nameof(WorkingDirrectoryPath), WorkingDirrectoryPath);
            }
        }
        
        private string SourceCodeSavePath
        {
            get { return mSourceCodeSavePath; }
            set
            {
                if (string.IsNullOrEmpty(value))
                    return;

                if (value.Equals(mSourceCodeSavePath))
                    return;

                mSourceCodeSavePath = value;
                mLogger.LogDebug("New path for {name} register with values {value}", nameof(SourceCodeSavePath), SourceCodeSavePath);
            }

        }

        private string mIp = string.Empty;
        private string Ip
        {
            get { return mIp; }
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    mIp = "0.0.0.0";
                    return;
                }

                if (value.Equals(mIp))
                    return;

                mIp = value;
                mLogger.LogDebug("New ip for server register with values {value}", Ip);

                OnServerAddressChange();
            }
        }


        private int mPort;    

        private int Port
        {
            get { return mPort; }
            set
            {
                if (value.Equals(mPort))
                    return;

                mPort = value;
                mLogger.LogDebug("New port for server register with values {value}", Port);

                OnServerAddressChange();
            }
        }

        #endregion

        #region Private methods

        private void KillProcess()
        {   
            if(mProcess == null) 
                return;

            try
            {
                if (mProcess.HasExited)
                    return;

                mProcess.CancelErrorRead();
                mProcess.CancelOutputRead();

                mProcess.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                mLogger.LogError("KillProcess(): {error},", ex);
            }
        }

        private void OnServerAddressChange()
        {
            if (mTcpServer == null)
                return;

            if(mTcpServer.IsListening)
            {
                if(mTcpServer.Connections != 0)
                {
                    KillProcess();
                    IsOutputEnded = true;
                }
                
                mTcpServer.Stop();
                UnSubscribe();
            }

            mTcpServer = new WatsonTcpServer(Ip, Port);
            Subscribe();
            mTcpServer.Start();
            mLogger.LogInformation("Server runing on {ip}:{port}", Ip, Port);
        }

        private void SetPath(AppSettings appSettings)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                SourceCodeSavePath = appSettings.SourceCodeSavePath.Windows;
                InterpreterPath = appSettings.PythonPaths.InterpreterPath.Windows;
                WorkingDirrectoryPath = appSettings.PythonPaths.WorkingDirrectoryPath.Windows;
                return;
            }

            SourceCodeSavePath = appSettings.SourceCodeSavePath.Linux;
            InterpreterPath = appSettings.PythonPaths.InterpreterPath.Linux;
            WorkingDirrectoryPath = appSettings.PythonPaths.WorkingDirrectoryPath.Linux;
        }

        private void SetServerAddress(AppSettings appSettings)
        {
            Ip = appSettings.ServerSettings.Ip;
            Port = appSettings.ServerSettings.Port;
        }

        private void SaveCodeAndStartProcess(string code, bool withDebug)
        {
            Encoding utf8WithoutBom = new UTF8Encoding(false);
            File.WriteAllText(SourceCodeSavePath, code, utf8WithoutBom);
            Task.Run(() => StartProcess(withDebug: withDebug));
        }

        private void StartProcess(bool withDebug = false)
        {
            Encoding utf8WithoutBom = new UTF8Encoding(false);
            string arg = string.Format($"-u -X utf8 {SourceCodeSavePath}");

            if (withDebug)
                arg = string.Format($"-u -X utf8 -m pdb {SourceCodeSavePath}");

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
                StandardErrorEncoding = utf8WithoutBom,
                StandardInputEncoding = utf8WithoutBom,
                StandardOutputEncoding = utf8WithoutBom
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
                    KillProcess();
                    IsOutputEnded = true;

                    UnSubscribe();
                    mTcpServer.Stop();
                    mTcpServer.Dispose();
                    mLogger.LogInformation("Service stop and dispose");
                }
            }
        }

        #endregion
    }
}
