﻿namespace RemotePythonExecution.Interface.AppSettingsDependency
{
    public class PythonPaths
    {
        public InterpreterPath InterpreterPath { get; set; } = new InterpreterPath();
        public WorkingDirrectoryPath WorkingDirrectoryPath { get; set; } = new WorkingDirrectoryPath();
    }
}
