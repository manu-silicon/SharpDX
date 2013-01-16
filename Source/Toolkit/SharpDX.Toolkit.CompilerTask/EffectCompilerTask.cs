﻿// Copyright (c) 2010-2012 SharpDX - Alexandre Mutel
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace SharpDX.Toolkit.Graphics
{
    public class EffectCompilerTask : Task
    {
        [Required]
        public ITaskItem ProjectDirectory { get; set; }

        [Required]
        public ITaskItem OutputDirectory { get; set; }

        [Required]
        public ITaskItem[] Files { get; set; }

        [Output]
        public ITaskItem[] OutputFiles { get; set; }

        public bool DynamicCompiling { get; set; }

        public bool Debug { get; set; }

        [Required]
        public ITaskItem IntermediateDirectory { get; set; }

        private static Regex parseMessage = new Regex(@"(.*)\s*\(\s*(\d+)\s*,\s*([^ \)]+)\)\s*:\s*(\w+)\s+(\w+)\s*:\s*(.*)");
        private static Regex matchNumberRange = new Regex(@"(\d+)-(\d+)");

        public override bool Execute()
        {
            var projectDirectory = ProjectDirectory.ItemSpec;
            var outputDirectory = OutputDirectory.ItemSpec;
            var intermediateDirectory = IntermediateDirectory.ItemSpec;

            var compiler = new EffectCompiler();

            bool hasErrors = false;

            var outputFiles = new List<ITaskItem>();

            foreach (ITaskItem file in Files)
            {
                var effectFileName = file.ItemSpec;
                var outputFileName = Path.ChangeExtension(effectFileName, "fxo");

                var effectFilePath = Path.Combine(projectDirectory, effectFileName);
                var dependencyFilePath = Path.Combine(Path.Combine(projectDirectory, intermediateDirectory), effectFileName + ".deps");
                var outputFilePath = Path.Combine(outputDirectory, outputFileName);

                bool dynamicCompiling;
                if (!bool.TryParse(file.GetMetadata("DynamicCompiling"), out dynamicCompiling))
                {
                    dynamicCompiling = DynamicCompiling;
                }

                Log.LogMessage(MessageImportance.High, "Check Toolkit FX file to compile {0} with dependency file {1}", effectFilePath, dependencyFilePath);

                if (compiler.CheckForChanges(dependencyFilePath) || !File.Exists(outputFilePath))
                {
                    Log.LogMessage(MessageImportance.High, "Start to compile {0}", effectFilePath);

                    var compilerResult = compiler.CompileFromFile(effectFilePath, Debug ? EffectCompilerFlags.Debug : EffectCompilerFlags.None, null, null, dynamicCompiling, dependencyFilePath);

                    if (compilerResult.HasErrors)
                    {
                        hasErrors = true;
                    }
                    else
                    {
                        Log.LogMessage(MessageImportance.High, "Compiled successfull {0} to {1}", effectFileName, outputFileName);
                    }

                    foreach (var message in compilerResult.Logger.Messages)
                    {
                        var text = message.ToString();


                        var match = parseMessage.Match(text);
                        if (match.Success)
                        {
                            var filePath = match.Groups[1].Value;
                            var lineNumber = int.Parse(match.Groups[2].Value);
                            var colNumberText = match.Groups[3].Value;
                            int colStartNumber;
                            int colEndNumber;
                            var colMatch = matchNumberRange.Match(colNumberText);
                            if (colMatch.Success)
                            {
                                int.TryParse(colMatch.Groups[1].Value, out colStartNumber);
                                int.TryParse(colMatch.Groups[2].Value, out colEndNumber);
                            }
                            else
                            {
                                int.TryParse(colNumberText, out colStartNumber);
                                colEndNumber = colStartNumber;
                            }
                            var msgType = match.Groups[4].Value;
                            var msgCode = match.Groups[5].Value;
                            var msgText = match.Groups[6].Value;
                            if (string.Compare(msgType, "error", StringComparison.InvariantCultureIgnoreCase) == 0)
                            {
                                Log.LogError(string.Empty, msgCode, string.Empty, filePath, lineNumber, colStartNumber, lineNumber, colEndNumber, msgText);
                            }
                            else if (string.Compare(msgType, "warning", StringComparison.InvariantCultureIgnoreCase) == 0)
                            {
                                Log.LogWarning(string.Empty, msgCode, string.Empty, filePath, lineNumber, colStartNumber, lineNumber, colEndNumber, msgText);
                            }
                            else if (string.Compare(msgType, "info", StringComparison.InvariantCultureIgnoreCase) == 0)
                            {
                                Log.LogWarning(string.Empty, msgCode, string.Empty, filePath, lineNumber, colStartNumber, lineNumber, colEndNumber, msgText);
                            }
                            else
                            {
                                Log.LogWarning("Unable to parse: " + text);
                            }
                        }
                        else
                        {
                            Log.LogWarning("Unable to parse: " + text);
                        }
                    }

                    if (!compilerResult.HasErrors && compilerResult.EffectData != null)
                    {
                        try
                        {
                            var directoryName = Path.GetDirectoryName(outputFilePath);
                            if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
                            {
                                Directory.CreateDirectory(directoryName);
                            }

                            compilerResult.EffectData.Save(outputFilePath);
                        }
                        catch (Exception ex)
                        {
                            Log.LogError("Cannot write compiled file to {0} : {1}", effectFilePath, ex.Message);
                            hasErrors = true;
                        }
                    }
                }

                // Only add existing file
                if (File.Exists(outputFilePath))
                {
                    // Add the file
                    outputFiles.Add(new TaskItem(outputFilePath));
                }
            }

            OutputFiles = outputFiles.ToArray();

            return !hasErrors;
        }
    }
}