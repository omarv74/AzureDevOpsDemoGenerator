﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using AzureDevOpsDemoBuilder.Models;
using AzureDevOpsDemoBuilder.ServiceInterfaces;
using AzureDevOpsDemoBuilder.Services;
using AzureDevOpsAPI.Viewmodel.ProjectAndTeams;
using AzureDevOpsAPI.WorkItemAndTracking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.VisualStudio.Services.Gallery.WebApi;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace VstsDemoBuilder.Controllers.Apis
{
    [Route("api/environment")]
    public class ProjectController : ControllerBase
    {
        private ITemplateService templateService;
        public delegate string[] ProcessEnvironment(Project model);
        public int usercount = 0;
        private IProjectService projectService;
        private IWebHostEnvironment HostingEnvironment;
        private ILogger<ProjectController> logger;
        public IConfiguration AppKeyConfiguration { get; }

        public ProjectController(IWebHostEnvironment _hosting, ILogger<ProjectController> _logger, IProjectService _projectService, ITemplateService _templateService)
        {
            HostingEnvironment = _hosting;
            logger = _logger;
            projectService = _projectService;
            templateService = _templateService;
        }

        [HttpPost]
        [Route("create")]
        public ActionResult create([FromBody]MultiProjects model)
        {
            //projectService.TrackFeature("api/environment/create");

            ProjectResponse returnObj = new ProjectResponse();
            returnObj.templatePath = model.templatePath;
            returnObj.templateName = model.templateName;
            string PrivateTemplatePath = string.Empty;
            string extractedTemplate = string.Empty;
            List<RequestedProject> returnProjects = new List<RequestedProject>();
            try
            {
                string ReadErrorMessages = System.IO.File.ReadAllText(string.Format(HostingEnvironment.ContentRootPath + "/JSON/" + "{0}", "ErrorMessages.json"));
                var Messages = JsonConvert.DeserializeObject<Messages>(ReadErrorMessages);
                var errormessages = Messages.ErrorMessages;
                List<string> ListOfExistedProjects = new List<string>();
                //check for Organization Name
                if (string.IsNullOrEmpty(model.organizationName))
                {
                    return BadRequest(errormessages.AccountMessages.InvalidAccountName); //"Provide a valid Account name"
                }
                //Check for AccessToken
                if (string.IsNullOrEmpty(model.accessToken))
                {
                    return Unauthorized(errormessages.AccountMessages.InvalidAccessToken); //"Token of type Basic must be provided"
                }
                else
                {
                    HttpResponseMessage response = projectService.GetprojectList(model.organizationName, model.accessToken);
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        return BadRequest(errormessages.AccountMessages.CheckaccountDetails);
                    }
                    else
                    {
                        var projectResult = response.Content.ReadAsAsync<ProjectsResponse.ProjectResult>().Result;
                        foreach (var project in projectResult.value)
                        {
                            ListOfExistedProjects.Add(project.name); // insert list of existing projects in selected organiszation to dummy list
                        }
                    }
                }
                if (model.users.Count > 0)
                {
                    List<string> ListOfRequestedProjectNames = new List<string>();
                    foreach (var project in model.users)
                    {
                        //check for Email and Validate project name
                        if (!string.IsNullOrEmpty(project.email) && !string.IsNullOrEmpty(project.projectName))
                        {
                            string pattern = @"^(?!_)(?![.])[a-zA-Z0-9!^\-`)(]*[a-zA-Z0-9_!^\.)( ]*[^.\/\\~@#$*%+=[\]{\}'"",:;?<>|](?:[a-zA-Z!)(][a-zA-Z0-9!^\-` )(]+)?$";

                            bool isProjectNameValid = Regex.IsMatch(project.projectName, pattern);
                            List<string> restrictedNames = new List<string>() { "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM10", "PRN", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LTP", "LTP8", "LTP9", "NUL", "CON", "AUX", "SERVER", "SignalR", "DefaultCollection", "Web", "App_code", "App_Browesers", "App_Data", "App_GlobalResources", "App_LocalResources", "App_Themes", "App_WebResources", "bin", "web.config" };

                            if (!isProjectNameValid)
                            {
                                project.status = errormessages.ProjectMessages.InvalidProjectName; //"Invalid Project name";
                                return BadRequest(project);
                            }
                            else if (restrictedNames.ConvertAll(d => d.ToLower()).Contains(project.projectName.Trim().ToLower()))
                            {
                                project.status = errormessages.ProjectMessages.ProjectNameWithReservedKeyword;//"Project name must not be a system-reserved name such as PRN, COM1, COM2, COM3, COM4, COM5, COM6, COM7, COM8, COM9, COM10, LPT1, LPT2, LPT3, LPT4, LPT5, LPT6, LPT7, LPT8, LPT9, NUL, CON, AUX, SERVER, SignalR, DefaultCollection, or Web";
                                return BadRequest(project);
                            }
                            ListOfRequestedProjectNames.Add(project.projectName.ToLower());
                        }
                        else
                        {
                            project.status = errormessages.ProjectMessages.ProjectNameOrEmailID;//"EmailId or ProjectName is not found";
                            return BadRequest(project);
                        }
                    }
                    //check for duplicatte project names from request body
                    bool anyDuplicateProjects = ListOfRequestedProjectNames.GroupBy(n => n).Any(c => c.Count() > 1);
                    if (anyDuplicateProjects)
                    {
                        return BadRequest(errormessages.ProjectMessages.DuplicateProject); //"ProjectName must be unique"
                    }
                    else
                    {
                        string templateName = string.Empty;
                        bool isPrivate = false;
                        if (string.IsNullOrEmpty(model.templateName) && string.IsNullOrEmpty(model.templatePath))
                        {
                            return BadRequest(errormessages.TemplateMessages.TemplateNameOrTemplatePath); //"Please provide templateName or templatePath(GitHub)"
                        }
                        else
                        {
                            //check for Private template path provided in request body
                            if (!string.IsNullOrEmpty(model.templatePath))
                            {
                                string fileName = Path.GetFileName(model.templatePath);
                                string extension = Path.GetExtension(model.templatePath);

                                if (extension.ToLower() == ".zip")
                                {
                                    extractedTemplate = fileName.ToLower().Replace(".zip", "").Trim() + "-" + Guid.NewGuid().ToString().Substring(0, 6) + extension.ToLower();
                                    templateName = extractedTemplate;
                                    model.templateName = extractedTemplate.ToLower().Replace(".zip", "").Trim();
                                    //Get template  by extarcted the template from TemplatePath and returning boolean value for Valid template
                                    PrivateTemplatePath = templateService.GetTemplateFromPath(model.templatePath, extractedTemplate, model.gitHubToken, model.userId, model.password);
                                    if (string.IsNullOrEmpty(PrivateTemplatePath))
                                    {
                                        return BadRequest(errormessages.TemplateMessages.FailedTemplate);//"Failed to load the template from given template path. Check the repository URL and the file name.  If the repository is private then make sure that you have provided a GitHub token(PAT) in the request body"
                                    }
                                    else
                                    {
                                        string privateErrorMessage = templateService.checkSelectedTemplateIsPrivate(PrivateTemplatePath);
                                        if (privateErrorMessage != "SUCCESS")
                                        {
                                            var templatepath = HostingEnvironment.ContentRootPath + "/PrivateTemplates/" + model.templateName;
                                            if (Directory.Exists(templatepath))
                                                Directory.Delete(templatepath, true);
                                            return BadRequest(privateErrorMessage);//"TemplatePath should have .zip extension file name at the end of the url"
                                        }
                                        else
                                        {
                                            isPrivate = true;
                                        }
                                    }
                                }
                                else
                                {
                                    return BadRequest(errormessages.TemplateMessages.PrivateTemplateFileExtension);//"TemplatePath should have .zip extension file name at the end of the url"
                                }
                            }
                            else
                            {
                                string response = templateService.GetTemplate(model.templateName);
                                if (response == "Template Not Found!")
                                {
                                    return BadRequest(errormessages.TemplateMessages.TemplateNotFound);
                                }
                                templateName = model.templateName;
                            }
                        }
                        //check for Extension file from selected template(public or private template)
                        string extensionJsonFile = projectService.GetJsonFilePath(isPrivate, PrivateTemplatePath, templateName, "Extensions.json");//string.Format(templatesFolder + @"{ 0}\Extensions.json", selectedTemplate);
                        if (System.IO.File.Exists(extensionJsonFile))
                        {
                            //check for Extension installed or not from selected template in selected organization
                            if (projectService.CheckForInstalledExtensions(extensionJsonFile, model.accessToken, model.organizationName))
                            {
                                if (model.installExtensions)
                                {
                                    Project pmodel = new Project();
                                    pmodel.SelectedTemplate = model.templateName;
                                    pmodel.accessToken = model.accessToken;
                                    pmodel.accountName = model.organizationName;

                                    bool isextensionInstalled = projectService.InstallExtensions(pmodel, model.organizationName, model.accessToken);
                                }
                                else
                                {
                                    return BadRequest(errormessages.ProjectMessages.ExtensionNotInstalled); //"Extension is not installed for the selected Template, Please provide IsExtensionRequired: true in the request body"
                                }
                            }
                        }
                        // continue to create project with async delegate method
                        foreach (var project in model.users)
                        {
                            var result = ListOfExistedProjects.ConvertAll(d => d.ToLower()).Contains(project.projectName.ToLower());
                            if (result == true)
                            {
                                project.status = project.projectName + " is already exist";
                            }
                            else
                            {
                                usercount++;
                                project.trackId = Guid.NewGuid().ToString().Split('-')[0];
                                project.status = "Project creation is initiated..";
                                Project pmodel = new Project();
                                pmodel.SelectedTemplate = model.templateName;
                                pmodel.accessToken = model.accessToken;
                                pmodel.accountName = model.organizationName;
                                pmodel.ProjectName = project.projectName;
                                pmodel.Email = project.email;
                                pmodel.id = project.trackId;
                                pmodel.IsApi = true;
                                if (model.templatePath != "")
                                {
                                    pmodel.PrivateTemplatePath = PrivateTemplatePath;
                                    pmodel.PrivateTemplateName = model.templateName;
                                    pmodel.IsPrivatePath = true;
                                }
                                //ProcessEnvironment processTask = new ProcessEnvironment(projectService.CreateProjectEnvironment);
                                //processTask.BeginInvoke(pmodel, new AsyncCallback(EndEnvironmentSetupProcess), processTask);

                                ProcessEnvironment processTask = new ProcessEnvironment(projectService.CreateProjectEnvironment);
                                var workTask = Task.Run(() => processTask.Invoke(pmodel));
                                workTask.ContinueWith((antecedent) =>
                                {
                                    EndEnvironmentSetupProcess(workTask, pmodel);
                                });

                            }
                            returnProjects.Add(project);
                        }
                        if (!string.IsNullOrEmpty(model.templatePath) && usercount == 0 && string.IsNullOrEmpty(extractedTemplate))
                        {
                            templateService.deletePrivateTemplate(extractedTemplate);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + "\t BulkProject \t" + ex.Message + "\t" + "\n" + ex.StackTrace + "\n");
                return StatusCode(500, ex.Message);
            }
            returnObj.users = returnProjects;
            return Ok(returnObj);
        }

        [HttpGet]
        [Route("GetCurrentProgress")]
        public IActionResult GetCurrentProgress(string TrackId)
        {
            //projectService.TrackFeature("api/environment/GetCurrentProgress");
            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            // Use SecurityProtocolType.Ssl3 if needed for compatibility reasons

            var currentProgress = projectService.GetStatusMessage(TrackId);
            return Ok(currentProgress["status"]);
        }

        /// <summary>
        /// End the process
        /// </summary>
        /// <param name="result"></param>
        public void EndEnvironmentSetupProcess(IAsyncResult result, Project model)
        {
            string templateUsed = string.Empty;
            string ID = string.Empty;
            string accName = string.Empty;
            try
            {
                ProcessEnvironment processTask = (ProcessEnvironment)result.AsyncState;
                //string[] strResult = processTask.EndInvoke(result);
                projectService.RemoveKey(model.id);
                if (ProjectService.StatusMessages.Keys.Count(x => x == model.id + "_Errors") == 1)
                {
                    string errorMessages = ProjectService.statusMessages[model.id + "_Errors"];
                    if (errorMessages != "")
                    {
                        //also, log message to file system
                        string logPath = HostingEnvironment.WebRootPath + "/log";
                        string fileName = string.Format("{0}_{1}.txt", templateUsed, DateTime.Now.ToString("ddMMMyyyy_HHmmss"));

                        if (!Directory.Exists(logPath))
                        {
                            Directory.CreateDirectory(logPath);
                        }

                        System.IO.File.AppendAllText(Path.Combine(logPath, fileName), errorMessages);

                        //Create ISSUE work item with error details in VSTSProjectgenarator account
                        string patBase64 = AppKeyConfiguration["PATBase64"];
                        string url = AppKeyConfiguration["URL"];
                        string projectId = AppKeyConfiguration["PROJECTID"];
                        string issueName = string.Format("{0}_{1}", templateUsed, DateTime.Now.ToString("ddMMMyyyy_HHmmss"));
                        IssueWI objIssue = new IssueWI();

                        errorMessages = errorMessages + "\t" + "TemplateUsed: " + templateUsed;
                        errorMessages = errorMessages + "\t" + "ProjectCreated : " + ProjectService.projectName;

                        logger.LogDebug(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + "\t  Error: " + errorMessages);

                        string logWIT = AppKeyConfiguration["LogWIT"];
                        if (logWIT == "true")
                        {
                            objIssue.CreateIssueWI(patBase64, "4.1", url, issueName, errorMessages, projectId, "Demo Generator");
                        }
                    }
                }
                usercount--;
            }
            catch (Exception ex)
            {
                logger.LogDebug(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + "\t" + ex.Message + "\t" + "\n" + ex.StackTrace + "\n");
            }
            finally
            {
                if (usercount == 0 && !string.IsNullOrEmpty(templateUsed))
                {
                    templateService.deletePrivateTemplate(templateUsed);
                }
            }
        }
    }
}

