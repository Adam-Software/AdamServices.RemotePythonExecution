# AdamServices.RemotePythonExecution
[![.NET Build And Publish Release](https://github.com/Adam-Software/AdamServices.RemotePythonExecution/actions/workflows/dotnet.yml/badge.svg)](https://github.com/Adam-Software/AdamServices.RemotePythonExecution/actions/workflows/dotnet.yml)     
![GitHub License](https://img.shields.io/github/license/Adam-Software/AdamServices.RemotePythonExecution)
![GitHub Release](https://img.shields.io/github/v/release/Adam-Software/AdamServices.RemotePythonExecution)

A service for remote execution of python code using the QuickCode client or AdamStudio

Use the shared [wiki](https://github.com/Adam-Software/AdamServices.Utilities.Managment/wiki) to find information about the project.

## For users
### Permanent links to releases
* **Windows [x64]**
  ```
  https://github.com/Adam-Software/AdamServices.RemotePythonExecution/releases/latest/download/RemotePythonExecution.win64.portable.zip
  ```
* **Linux [arm64]**
  ```
  https://github.com/Adam-Software/AdamServices.RemotePythonExecution/releases/latest/download/RemotePythonExecution.arm64.portable.zip
  ```

* **Windows [x64]**
  * Download using the [permalink](#permanent-links-to-releases)
  * Unzip and run RemotePythonExecution.exe

* **Linux [arm64]**
  * Download using the [permalink](#permanent-links-to-releases)
    ```bash
    wget https://github.com/Adam-Software/AdamServices.RemotePythonExecution/releases/latest/download/RemotePythonExecution.arm64.portable.zip
    ```
  * Unzip and make the RemotePythonExecution file executable
    ```bash
    unzip RemotePythonExecution.arm64.portable.zip -d RemotePythonExecution && chmod +x RemotePythonExecution/RemotePythonExecution
    ```
  * Run CheckingAvailability
    ```bash
    cd RemotePythonExecution && ./RemotePythonExecution
    ```
### Optional command line arguments
```cmd
--help            Display this help screen.
--version         Display version information.
```
