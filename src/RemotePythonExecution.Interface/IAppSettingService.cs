using RemotePythonExecution.Interface.IAppSettingsServiceDependency;

namespace RemotePythonExecution.Interface
{
    public interface IAppSettingService
    {
        public ServerSettings ServerSettings { get; set; }
        public PythonPaths PythonPaths { get; set; } 
        public SourceCodeSavePaths SourceCodeSavePaths { get; set; }
    }
}
