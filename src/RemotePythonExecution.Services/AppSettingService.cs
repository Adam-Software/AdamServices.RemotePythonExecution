using RemotePythonExecution.Interface;
using RemotePythonExecution.Interface.IAppSettingsServiceDependency;

namespace RemotePythonExecution.Services
{
    public class AppSettingService : IAppSettingService
    {
        public ServerSettings ServerSettings { get; set; } = new ServerSettings();
        public PythonPaths PythonPaths { get; set; } = new PythonPaths();
        public SourceCodeSavePaths SourceCodeSavePaths { get; set ; } = new SourceCodeSavePaths();

    }
}
