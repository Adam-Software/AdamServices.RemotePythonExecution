using RemotePythonExecution.Interface.IAppSettingsServiceDependency;

namespace RemotePythonExecution.Services
{
    public class AppSettings
    {
        public ServerSettings ServerSettings { get; set; } = new ServerSettings();
        public PythonPaths PythonPaths { get; set; } = new PythonPaths();
        public SourceCodeSavePath SourceCodeSavePath { get; set ; } = new SourceCodeSavePath();

    }
}
