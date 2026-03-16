using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityMCP.Editor.Core;

namespace UnityMCP.Editor.Handlers
{
    /// <summary>
    /// Command handler for executing C# code in the Unity editor.
    /// Uses Mono's built-in C# evaluator for runtime code execution.
    /// </summary>
    internal sealed class CodeExecutionCommandHandler : IMcpCommandHandler
    {
        private object evaluator;
        private MethodInfo evalMethod;
        private MethodInfo runMethod;
        private bool initialized;
        private string initError;

        public string CommandPrefix => "code";

        public string Description => "Execute C# code in the Unity editor";

        public JObject Execute(string action, JObject parameters)
        {
            if (string.Equals(action, "execute", StringComparison.OrdinalIgnoreCase))
            {
                return this.ExecuteCode(parameters);
            }

            return new JObject
            {
                ["success"] = false,
                ["error"] = "Unknown action: " + action + ". Supported actions: execute"
            };
        }

        private void EnsureInitialized()
        {
            if (this.initialized) return;
            this.initialized = true;

            try
            {
                Assembly monoCSharpAssembly = null;
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    if (assemblies[i].GetName().Name == "Mono.CSharp")
                    {
                        monoCSharpAssembly = assemblies[i];
                        break;
                    }
                }

                if (monoCSharpAssembly == null)
                {
                    try
                    {
                        monoCSharpAssembly = Assembly.Load("Mono.CSharp");
                    }
                    catch
                    {
                        this.initError = "Mono.CSharp assembly not available";
                        return;
                    }
                }

                var evaluatorType = monoCSharpAssembly.GetType("Mono.CSharp.Evaluator");
                var compilerSettingsType = monoCSharpAssembly.GetType("Mono.CSharp.CompilerSettings");
                var compilerContextType = monoCSharpAssembly.GetType("Mono.CSharp.CompilerContext");
                var streamReportPrinterType = monoCSharpAssembly.GetType("Mono.CSharp.StreamReportPrinter");

                if (evaluatorType == null || compilerContextType == null)
                {
                    this.initError = "Required Mono.CSharp types not found";
                    return;
                }

                var settings = Activator.CreateInstance(compilerSettingsType);

                if (streamReportPrinterType == null)
                {
                    this.initError = "StreamReportPrinter type not found";
                    return;
                }

                var printer = Activator.CreateInstance(streamReportPrinterType, System.IO.TextWriter.Null);
                var ctx = Activator.CreateInstance(compilerContextType, settings, printer);
                this.evaluator = Activator.CreateInstance(evaluatorType, ctx);

                this.evalMethod = evaluatorType.GetMethod("Evaluate",
                    new Type[] { typeof(string), typeof(object).MakeByRefType(), typeof(bool).MakeByRefType() });

                this.runMethod = evaluatorType.GetMethod("Run", new Type[] { typeof(string) });

                if (this.evalMethod == null && this.runMethod == null)
                {
                    this.initError = "Evaluate/Run methods not found on Mono.CSharp.Evaluator";
                    return;
                }

                var refMethod = evaluatorType.GetMethod("ReferenceAssembly",
                    new Type[] { typeof(Assembly) });

                if (refMethod != null)
                {
                    var allAssemblies = AppDomain.CurrentDomain.GetAssemblies();
                    for (int i = 0; i < allAssemblies.Length; i++)
                    {
                        if (allAssemblies[i].IsDynamic) continue;
                        try
                        {
                            refMethod.Invoke(this.evaluator, new object[] { allAssemblies[i] });
                        }
                        catch { }
                    }
                }

                if (this.runMethod != null)
                {
                    string[] defaultUsings = new string[]
                    {
                        "using System;",
                        "using System.Collections;",
                        "using System.Collections.Generic;",
                        "using System.Linq;",
                        "using UnityEngine;",
                        "using UnityEditor;"
                    };

                    for (int i = 0; i < defaultUsings.Length; i++)
                    {
                        try
                        {
                            this.runMethod.Invoke(this.evaluator, new object[] { defaultUsings[i] });
                        }
                        catch { }
                    }
                }

                Debug.Log("CodeExecutionCommandHandler: Mono.CSharp evaluator initialized successfully");
            }
            catch (Exception ex)
            {
                this.initError = "Failed to initialize Mono.CSharp evaluator: " + ex.Message;
            }
        }

        private JObject ExecuteCode(JObject parameters)
        {
            var codeToken = parameters["code"];
            var code = codeToken != null ? codeToken.ToString() : null;

            if (string.IsNullOrEmpty(code))
            {
                return new JObject
                {
                    ["success"] = false,
                    ["error"] = "The 'code' parameter is required and cannot be empty."
                };
            }

            this.EnsureInitialized();

            if (this.initError != null)
            {
                return new JObject
                {
                    ["success"] = false,
                    ["error"] = this.initError
                };
            }

            try
            {
                var logMessages = new List<string>();
                Application.LogCallback logHandler = (logString, stackTrace, type) =>
                {
                    string prefix = "";
                    if (type == LogType.Error || type == LogType.Exception)
                        prefix = "[ERROR] ";
                    else if (type == LogType.Warning)
                        prefix = "[WARNING] ";
                    logMessages.Add(prefix + logString);
                };

                Application.logMessageReceived += logHandler;

                object result = null;
                string error = null;

                try
                {
                    if (this.evalMethod != null)
                    {
                        var args = new object[] { code, null, null };
                        this.evalMethod.Invoke(this.evaluator, args);
                        var returnValue = args[1];
                        var resultSet = (bool)args[2];

                        if (resultSet && returnValue != null)
                        {
                            result = returnValue;
                        }
                    }
                    else if (this.runMethod != null)
                    {
                        this.runMethod.Invoke(this.evaluator, new object[] { code });
                    }
                }
                catch (TargetInvocationException ex)
                {
                    error = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                Application.logMessageReceived -= logHandler;

                if (error != null)
                {
                    return new JObject
                    {
                        ["success"] = false,
                        ["error"] = error,
                        ["output"] = logMessages.Count > 0 ? string.Join("\n", logMessages) : null
                    };
                }

                var response = new JObject
                {
                    ["success"] = true
                };

                if (result != null)
                {
                    response["result"] = result.ToString();
                }

                if (logMessages.Count > 0)
                {
                    response["output"] = string.Join("\n", logMessages);
                }

                return response;
            }
            catch (Exception ex)
            {
                return new JObject
                {
                    ["success"] = false,
                    ["error"] = "Error executing code: " + ex.Message,
                    ["stackTrace"] = ex.StackTrace
                };
            }
        }
    }
}
