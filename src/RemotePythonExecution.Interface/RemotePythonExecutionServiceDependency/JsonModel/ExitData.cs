using System;

namespace RemotePythonExecution.Interface.RemotePythonExecutionServiceDependency.JsonModel
{
    public class ExitData
    {
        public bool IsExitDataUpdated { get; set; } = false;
        public int ProcessId { get; set; } 
        public DateTime StartTime { get; set; }
        public DateTime ExitTime { get; set; }
        public TimeSpan TotalProcessorTime { get; set; }
        public TimeSpan UserProcessorTime { get; set; }
        public int ExitCode { get; set; }
    }
}
