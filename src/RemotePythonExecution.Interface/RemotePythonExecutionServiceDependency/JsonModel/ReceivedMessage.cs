namespace RemotePythonExecution.Interface.RemotePythonExecutionServiceDependency.JsonModel
{
    public class ReceivedMessage
    {
        /// <summary>
        /// source_code || control_characters
        /// </summary>
        public string MessageType { get; set; } = string.Empty;

        public string Code { get; set; } = string.Empty;

        public string ControlCharacters { get; set; } = string.Empty;
    }
}
