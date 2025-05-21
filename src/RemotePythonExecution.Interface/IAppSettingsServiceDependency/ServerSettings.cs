namespace RemotePythonExecution.Interface.IAppSettingsServiceDependency
{
    public class ServerSettings
    {
        private string mIp = "0.0.0.0";
        public string Ip 
        { 
            get { return mIp; }
            set 
            {
                if (string.IsNullOrEmpty(value))
                {
                    mIp = "0.0.0.0";
                    return;
                }
                
                mIp = value; 
            } 
        }
        public int Port { get; set; } = 19000;
    }
}
