﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Reflection;
using System.Xml;
using MoCS.BuildService.Business.Interfaces;

namespace MoCS.BuildService.Business
{
    public class SubmitValidator
    {
        private IFileSystem _fileSystem;
        private IExecuteCmd _command;
        public SubmitValidator(IFileSystem fileSystem, IExecuteCmd command)
        {
            _fileSystem = fileSystem;
            _command = command;
        }

        /// <summary>
        /// the main processing method. 
        /// </summary>
        /// <param name="sysSettings"></param>
        /// <param name="assignmentSettings"></param>
        /// <param name="submitSettings"></param>
        /// <returns></returns>
        public ValidationResult Process(SystemSettings sysSettings, AssignmentSettings assignmentSettings, SubmitSettings submitSettings)
        {
            ValidationResult submitResult = new ValidationResult();

            BatchFileCreator batchfileCreator = new BatchFileCreator(sysSettings, submitSettings, _fileSystem);
            string buildFilePath = batchfileCreator.CreateBuildFile();
            string testFilePath = batchfileCreator.CreateTestFile();


            submitResult = CompileAssembly(buildFilePath, batchfileCreator.OutputLogPath);
            //in case of an error, directly return
            if (submitResult.Status == SubmitStatusCode.CompilationError)
            {
                return submitResult;
            }

            if (!_fileSystem.FileExists(batchfileCreator.OutputDllPath))
            {
                submitResult.Status = SubmitStatusCode.CompilationError;
                return submitResult;
            }

            //check businessrules
            submitResult = CheckBusinessRules(batchfileCreator.OutputDllPath, assignmentSettings);
            if (submitResult.Status == SubmitStatusCode.ValidationError)
            {
                return submitResult;
            }

            submitResult = TestAssembly(batchfileCreator.BatchfileTestPath, batchfileCreator.TestLogPath);

            if (submitResult.Status == SubmitStatusCode.TestError)
            {
                return submitResult;
            }

            submitResult.Status = SubmitStatusCode.Success;

            return submitResult;
        }



        private void UpdateResultWithBuildLog(ValidationResult submitResult, string outputLogPath)
        {
            List<string> messages = _fileSystem.ReadErrorsFromBuildLog(outputLogPath);
            foreach (string message in messages)
            {
                submitResult.Messages.Add(message);
            }
        }


        private ValidationResult CheckBusinessRules(string outputDllPath, AssignmentSettings assignmentSettings)
        {
            ValidationResult submitResult = new ValidationResult();

            //Compilation is successfull. Now check businessrules.
            //check if the assembly is found;
            Type implementedClass = null;

            if (_fileSystem.FileExists(outputDllPath))
            {
                Assembly assembly = _fileSystem.LoadAssembly(outputDllPath);
                //loop through the types in the assembly
                foreach (Type type in assembly.GetTypes())
                {
                    //try to find the class that was implemented 
                    if (type.Name.Equals(assignmentSettings.ClassnameToImplement))
                    {
                        implementedClass = type;
                        break;
                    }
                }
            }

            //if the classToImplement cannot be found, return with an error
            if (implementedClass == null)
            {
                submitResult.Status = SubmitStatusCode.ValidationError;
                submitResult.Messages.Add(string.Format("The class to implement ({0}) is not found", assignmentSettings.ClassnameToImplement));
                return submitResult;
            }

            //check to see if it implements the required interface...
            Type requiredInterface = implementedClass.GetInterface(assignmentSettings.InterfaceNameToImplement);
            if (requiredInterface == null)
            {
                string message = string.Format("The class to implement ({0}) does not implement the required interface {1}", assignmentSettings.ClassnameToImplement, assignmentSettings.InterfaceNameToImplement);
                submitResult.Status = SubmitStatusCode.ValidationError;
                submitResult.Messages.Add(message);
                return submitResult;
            }

            return submitResult;

        }

        public void Terminate()
        {

        }


        private ValidationResult CompileAssembly(string buildFilePath, string outputLogPath)
        {

            int result = _command.ExecuteCommandSync(buildFilePath);
            ValidationResult submitResult = new ValidationResult();

            if (result != 0)
            {
                submitResult.Status = SubmitStatusCode.CompilationError;
                if (_fileSystem.FileExists(outputLogPath))
                {
                    UpdateResultWithBuildLog(submitResult, outputLogPath);
                }
                else
                {
                    submitResult.Messages.Add("build logfile not found");
                }
            }
            else
            {
                //result = 1...i did find a compilationerror
                //check the size of the logfile
                if (_fileSystem.FileExists(outputLogPath))
                {
                    List<string> errors = _fileSystem.ReadErrorsFromBuildLog(outputLogPath);
                    if (errors.Count>0)
                    {
                        submitResult.Status = SubmitStatusCode.CompilationError;
                        if (_fileSystem.FileExists(outputLogPath))
                        {
                            UpdateResultWithBuildLog(submitResult, outputLogPath);
                        }
                    }
                }
            }

            return submitResult;
        }

        private ValidationResult TestAssembly(string batchfileTestPath, string testLogPath)
        {
            ValidationResult submitResult = new ValidationResult();

            int result = _command.ExecuteCommandSync(batchfileTestPath);

            if (result != 0)
            {
                submitResult.Status = SubmitStatusCode.TestError;
                if (!_fileSystem.FileExists(testLogPath))
                {
                    submitResult.Messages.Add("no logfile found");
                    return submitResult;
                }
                else
                {
                    //interpret the xml outputfile from the unit test
                    UpdateResultWithTestLog(submitResult, testLogPath);
                }
            }
            return submitResult;
        }

        private void UpdateResultWithTestLog(ValidationResult submitResult, string testlogPath)
        {
            //interpret the xml outputfile from the unit test
            XmlDocument doc = _fileSystem.LoadXmlDocument(testlogPath);

            XmlNodeList nodes = doc.SelectNodes("//failure");

            foreach (XmlNode failureNode in nodes)
            {
                // string testName = failureNode.Attributes["name"].InnerText;
                XmlNode failureMessageNode = failureNode.SelectSingleNode("message");
                string text = failureMessageNode.InnerText;
                submitResult.Messages.Add(text);
            }
            submitResult.Status = SubmitStatusCode.TestError;

        }

    }








}
